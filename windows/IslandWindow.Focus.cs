using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DynamicIsland;

/// <summary>
/// One focus for both devices. A focus session (the island's timer, the cat's "Focus 25", the iPhone app, or your iPhone's own
/// Focus via the Update Laptop automation) silences banners and pop-ups on the laptop; they wait in Alerts.
/// Starting focus on the laptop can tap your iPhone to switch its Focus on too. Sessions are logged for weekly stats.
/// </summary>
public partial class IslandWindow
{
    public int FocusHeld { get; private set; }
    DateTime? _focusStarted;
    bool _phoneFocusFollowing;

    /// True while banners and pop-ups should stay quiet.
    public bool FocusSilencing => _s.FocusSilence && (_timerEnd != null || _phoneFocusFollowing);
    public bool FocusActive => _timerEnd != null || _phoneFocusFollowing;

    public void BeginFocus(int minutes, bool fromPhone = false)
    {
        if (_timerEnd != null && _focusStarted != null) LogFocus(completed: false); // restarting
        _timerTotal = TimeSpan.FromMinutes(minutes);
        _timerEnd = DateTime.Now + _timerTotal;
        _focusStarted = DateTime.Now;
        FocusHeld = 0;
        RefreshTimer(); UpdateCompact(); UpdateTimers();
        if (!fromPhone && _s.FocusTapsPhone && Push.Count > 0)
            Push.Send($"{minutes}-minute focus on your laptop", "Tap to turn on Focus on this iPhone too.", "link",
                url: "shortcuts://run-shortcut?name=" + Uri.EscapeDataString(_s.FocusShortcutOn), tag: "focus");
    }

    /// completed = the timer ran out (not stopped early).
    public void EndFocus(bool completed)
    {
        if (_focusStarted != null) LogFocus(completed);
        _focusStarted = null;
        _timerEnd = null;
        RefreshTimer(); UpdateCompact(); UpdateTimers();
        FocusEnded();
    }

    void FocusEnded()
    {
        if (FocusActive) return;
        if (FocusHeld > 0)
        {
            int n = FocusHeld; FocusHeld = 0;
            ShowToast(new ToastSpec($"{n} notification{(n == 1 ? "" : "s")} while you focused", "Tap to see them", Glyph: "", Seconds: 6, OnClick: () => SelectTab("alerts")));
        }
        if (_s.FocusTapsPhone && Push.Count > 0 && !string.IsNullOrWhiteSpace(_s.FocusShortcutOff))
            Push.Send("Focus finished on your laptop", "Tap to turn Focus off on this iPhone.", "link",
                url: "shortcuts://run-shortcut?name=" + Uri.EscapeDataString(_s.FocusShortcutOff), tag: "focus");
    }

    /// Your iPhone's Focus changed (Update Laptop automation).
    void PhoneFocusChanged(string? focus)
    {
        bool on = focus != null;
        if (!_s.FollowPhoneFocus) { _phoneFocusFollowing = false; return; }
        if (on == _phoneFocusFollowing) return;
        _phoneFocusFollowing = on;
        if (on)
        {
            FocusHeld = 0;
            ShowToast(new ToastSpec($"{focus} focus is on", _s.FocusSilence ? "Notifications will wait until you're done" : null, Glyph: "", Seconds: 3));
            if (_s.PhoneFocusStartsTimer && _timerEnd == null) BeginFocus(_s.PomodoroMinutes, fromPhone: true);
        }
        else
        {
            if (_timerEnd != null && _s.PhoneFocusStartsTimer) { EndFocus(completed: false); return; }
            FocusEnded();
        }
        UpdateCompact();
    }

    object Bridge.IBridgeHost.FocusStats() => Ui(() =>
    {
        var st = GetFocusStats();
        return (object)new { st.TodayMinutes, st.TodaySessions, st.Week, st.WeekLabels, st.WeekMinutes, st.Streak, st.Completed,
            active = FocusActive, secondsLeft = _timerEnd is DateTime e ? Math.Max(0, (int)(e - DateTime.Now).TotalSeconds) : 0, held = FocusHeld };
    });

    // ───────── history + stats ─────────

    public sealed record FocusSession(DateTime Start, double Minutes, bool Completed);
    static string FocusLogPath => Path.Combine(AppSettings.Dir, "focus-history.json");
    List<FocusSession>? _focusLog;

    List<FocusSession> FocusLog
    {
        get
        {
            if (_focusLog != null) return _focusLog;
            try { _focusLog = File.Exists(FocusLogPath) ? JsonSerializer.Deserialize<List<FocusSession>>(File.ReadAllText(FocusLogPath)) : null; } catch { }
            return _focusLog ??= new();
        }
    }

    void LogFocus(bool completed)
    {
        if (_focusStarted is not DateTime start) return;
        var minutes = Math.Min((DateTime.Now - start).TotalMinutes, _timerTotal.TotalMinutes);
        if (minutes < 1) return;
        FocusLog.Add(new FocusSession(start, Math.Round(minutes, 1), completed));
        var cutoff = DateTime.Now.AddDays(-120);
        FocusLog.RemoveAll(f => f.Start < cutoff);
        try { Directory.CreateDirectory(AppSettings.Dir); File.WriteAllText(FocusLogPath, JsonSerializer.Serialize(FocusLog)); } catch (Exception ex) { App.Log(ex); }
    }

    public sealed record FocusStats(double TodayMinutes, int TodaySessions, double[] Week, string[] WeekLabels, double WeekMinutes, int Streak, int Completed);

    /// Last 7 days (oldest first), today's totals and the streak of days with at least one session.
    public FocusStats GetFocusStats()
    {
        var log = FocusLog;
        var today = DateTime.Today;
        var week = new double[7]; var labels = new string[7];
        for (int i = 0; i < 7; i++)
        {
            var d = today.AddDays(i - 6);
            week[i] = Math.Round(log.Where(f => f.Start.Date == d).Sum(f => f.Minutes));
            labels[i] = i == 6 ? "Today" : d.ToString("ddd");
        }
        int streak = 0;
        for (var d = today; log.Any(f => f.Start.Date == d); d = d.AddDays(-1)) streak++;
        var t = log.Where(f => f.Start.Date == today).ToList();
        return new FocusStats(Math.Round(t.Sum(f => f.Minutes)), t.Count, week, labels, week.Sum(), streak, log.Count(f => f.Completed && f.Start >= today.AddDays(-6)));
    }
}
