using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Bridge;

namespace DynamicIsland;

/// <summary>The island's side of the Cat Bridge. Bridge calls arrive on background threads and hop to the UI thread.</summary>
public partial class IslandWindow : IBridgeHost
{
    BridgeServer? _bridge;
    public string? BridgeError { get; private set; }

    /// "file" (path) or "ping": lets the desktop cat react.
    public event Action<string, string?>? BridgeEvent;

    public void StartBridge()
    {
        StopBridge();
        BridgeError = null;
        if (!_s.BridgeEnabled) return;
        try
        {
            _bridge = new BridgeServer(this, _s.BridgeKey, _s.BridgePort) { Log = App.Log };
            _bridge.Start(BridgeAddresses());
            _tailnetWatch ??= StartTailnetWatch();
        }
        catch (Exception ex)
        {
            _bridge = null;
            BridgeError = $"Port {_s.BridgePort} is already in use by another app.";
            App.Log(ex);
        }
    }

    public void StopBridge() { _bridge?.Dispose(); _bridge = null; }

    public IReadOnlyList<System.Net.IPAddress> BridgeBound => _bridge?.Bound ?? Array.Empty<System.Net.IPAddress>();

    List<System.Net.IPAddress> BridgeAddresses()
    {
        var list = _s.BridgeListenTailscale ? Tailnet.Addresses() : new List<System.Net.IPAddress>();
        if (System.Net.IPAddress.TryParse(_s.BridgeExtraAddress.Trim(), out var extra) && !extra.Equals(System.Net.IPAddress.Any) && !extra.Equals(System.Net.IPAddress.IPv6Any))
            list.Add(extra);
        return list;
    }

    System.Windows.Threading.DispatcherTimer? _tailnetWatch;

    /// Tailscale may connect after the island starts (or reconnect with the same IP): pick the address up when it appears.
    System.Windows.Threading.DispatcherTimer StartTailnetWatch()
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        t.Tick += (_, _) =>
        {
            if (_bridge == null) return;
            foreach (var a in BridgeAddresses())
                _bridge.Listen(a);
        };
        t.Start();
        return t;
    }

    HelloInfo IBridgeHost.Hello()
    {
        var v = typeof(IslandWindow).Assembly.GetName().Version;
        return new HelloInfo(Environment.MachineName, v == null ? "2" : $"{v.Major}.{v.Minor}", Tailnet.IPv4?.ToString(), _s.BridgePort,
            new[] { "status", "clipboard", "files", "shelf", "link", "media", "focus", "lock", "ping", "notify", "phone", "alerts", "inbox", "input", "screen" });
    }

    IReadOnlyList<AlertEntry> IBridgeHost.Alerts() => Ui(() =>
        _notes.History.Take(40).Select(n => new AlertEntry(n.App, n.Title, n.Body, n.Time, n.Aumid == PhoneAumid)).ToList());

    T Ui<T>(Func<T> f) => Dispatcher.CheckAccess() ? f() : Dispatcher.Invoke(f);
    void UiAsync(Action a) => Dispatcher.BeginInvoke(a);

    BridgeStatus IBridgeHost.Status() => Ui(() =>
    {
        var m = _media.Current;
        bool hasTrack = m != null && !string.IsNullOrWhiteSpace(m.Title);
        int left = _timerEnd is DateTime end ? Math.Max(0, (int)(end - DateTime.Now).TotalSeconds) : 0;
        return new BridgeStatus(Environment.MachineName, Math.Max(0, _power.Percent), _power.Charging, _power.HasBattery,
            hasTrack ? m!.Title : null, hasTrack ? m!.Artist : null, hasTrack && m!.IsPlaying, left, _shelf.Paths.Count);
    });

    string? IBridgeHost.GetClipboard() => Ui(() =>
    {
        for (int i = 0; i < 3; i++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
            catch (COMException) { System.Threading.Thread.Sleep(40); }
        }
        return "";
    });

    void IBridgeHost.SetClipboard(string text) => UiAsync(() =>
    {
        for (int i = 0; i < 3; i++)
        {
            try { Clipboard.SetText(text); break; }
            catch (COMException) { System.Threading.Thread.Sleep(40); }
        }
        var preview = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (preview.Length > 60) preview = preview[..60] + "…";
        ShowToast(new ToastSpec("Copied from your iPhone", preview, Glyph: "", Seconds: 3));
    });

    void IBridgeHost.OpenLink(string url) => UiAsync(() =>
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { App.Log(ex); }
        ShowToast(new ToastSpec("Opened from your iPhone", new Uri(url).Host, Glyph: "", Seconds: 3));
    });

    string IBridgeHost.ReceiveFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Cat Bridge");

    void IBridgeHost.FileReceived(string path) => UiAsync(() =>
    {
        AddToShelf(new[] { path });
        var dir = Path.GetDirectoryName(path)!;
        ShowToast(new ToastSpec("Caught a file from your iPhone", Path.GetFileName(path), ShelfStore.IconFor(path), "", Seconds: 4,
            OnClick: () => { try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { } }));
        BridgeEvent?.Invoke("file", path);
    });

    IReadOnlyList<string> IBridgeHost.ShelfPaths() => Ui(() => _shelf.Paths.ToList());

    void IBridgeHost.Media(string action) => UiAsync(() =>
    {
        switch (action)
        {
            case "toggle": _media.TogglePlay(); break;
            case "next": _media.Next(); break;
            case "prev": _media.Previous(); break;
        }
    });

    void IBridgeHost.Focus(int minutes) => UiAsync(() => { BeginFocus(minutes, fromPhone: true); ShowToast(new ToastSpec($"{minutes}-minute focus started from your iPhone", null, Glyph: "\uE916", Seconds: 2.5)); });

    [DllImport("user32.dll")] static extern bool LockWorkStation();
    void IBridgeHost.Lock() => UiAsync(() => LockWorkStation());

    void IBridgeHost.Ping() => UiAsync(() =>
    {
        if (_hidden) ToggleHidden();
        SystemSounds.Asterisk.Play();
        ShowToast(new ToastSpec("Meow! Over here!", "Your iPhone is looking for this laptop", Glyph: "", Seconds: 6));
        BridgeEvent?.Invoke("ping", null);
    });
}

/// <summary>iPhone notifications and status on the island, and alerts pushed to the iPhone.</summary>
public partial class IslandWindow
{
    PushService? _push;
    public PushService Push => _push ??= CreatePush();

    // what the iPhone last told us (via Shortcuts automations)
    int? _phoneBattery; bool? _phoneCharging; string? _phoneFocus; DateTime _phoneSeen = DateTime.MinValue;
    string? _phoneLastApp; DateTime _phoneLastTime = DateTime.MinValue;
    readonly Dictionary<string, DateTime> _recentPhone = new();
    DateTime _lastPushProblem = DateTime.MinValue;
    bool _phoneWired;

    PushService CreatePush()
    {
        var p = new PushService(_s);
        p.Problem += msg => UiAsync(() =>
        {
            if (DateTime.Now - _lastPushProblem < TimeSpan.FromMinutes(10)) return;
            _lastPushProblem = DateTime.Now;
            ShowToast(new ToastSpec("Couldn't alert your iPhone", msg, Glyph: "", GlyphBrush: Red, Seconds: 6));
        });
        return p;
    }

    /// Called once on startup: push the things worth knowing about when you're away from the laptop.
    void WirePhoneAlerts()
    {
        if (_phoneWired) return;
        _phoneWired = true;
        TimerFinished += () => { if (_s.PushTimer && Away) Push.Send("Time's up", $"Your {_timerTotal.TotalMinutes:0}-minute focus timer finished.", tag: "timer"); };
        PowerEvent += (ev, pct) =>
        {
            if (!_s.PushBattery) return;
            if (ev is "low20" or "low10") Push.Send(ev == "low10" ? "Laptop battery critically low" : "Laptop battery low", $"{Environment.MachineName} is at {pct}%. Plug it in?", "alert", tag: "battery");
            else if (ev == "full" && Away) Push.Send("Laptop fully charged", $"{Environment.MachineName} is at 100%.", tag: "battery");
        };
    }

    bool Away => Idle.Seconds >= Math.Max(0, _s.PushAwayMinutes) * 60;

    void PushServerChange(string name, bool up)
    {
        if (_s.PushServers) Push.Send(up ? $"{name} is back" : $"{name} is down", up ? "Reachable again" : "Not reachable from your laptop", "alert", tag: "srv-" + name);
    }

    /// Windows notifications are forwarded to the iPhone only while you're away from the laptop.
    void ForwardIfAway(NotificationItem n)
    {
        if (!_s.PushForwardNotifications || !Away || n.Aumid == PhoneAumid) return;
        Push.Send(string.IsNullOrEmpty(n.App) || n.App == n.Title ? n.Title : $"{n.App}: {n.Title}", n.Body, tag: "win-" + n.Id);
    }

    // ───────── iPhone → laptop ─────────

    const string PhoneAumid = "catbridge.iphone";
    static string PhoneKey(string title, string body) => (title + "|" + body).Trim().ToLowerInvariant();

    bool SeenRecently(string title, string body)
    {
        var k = PhoneKey(title, body);
        foreach (var old in _recentPhone.Where(kv => DateTime.Now - kv.Value > TimeSpan.FromSeconds(30)).Select(kv => kv.Key).ToList()) _recentPhone.Remove(old);
        if (_recentPhone.ContainsKey(k)) return true;
        _recentPhone[k] = DateTime.Now;
        return false;
    }

    /// Phone Link (Bluetooth) and Cat Bridge (Tailscale) can both deliver the same iPhone notification. Show it once.
    bool IsPhoneLinkDuplicate(NotificationItem n)
    {
        bool phoneLink = n.Aumid.Contains("YourPhone", StringComparison.OrdinalIgnoreCase) || n.App.Contains("Phone Link", StringComparison.OrdinalIgnoreCase);
        if (!phoneLink) return false;
        if (SeenRecently(n.Title, n.Body)) { _notes.History.Remove(n); return true; }
        return false;
    }

    void IBridgeHost.PhoneNotification(string app, string title, string body) => UiAsync(() =>
    {
        if (SeenRecently(title, body)) return;
        if (title.Length == 0) { title = body; body = ""; }
        var shownApp = string.IsNullOrWhiteSpace(app) ? "iPhone" : app;
        _phoneLastApp = shownApp; _phoneLastTime = DateTime.Now; _phoneSeen = DateTime.Now;
        var item = new NotificationItem { Id = (uint)Random.Shared.Next(), App = "iPhone · " + shownApp, Title = title, Body = body, Aumid = PhoneAumid, Time = DateTime.Now };
        _notes.History.Insert(0, item);
        if (_notes.History.Count > 50) _notes.History.RemoveAt(_notes.History.Count - 1);
        if (_mode == Mode.Expanded && _tab == "alerts") RefreshAlerts();
        if (_tab == "home") UpdateHome();
        NotificationShown?.Invoke(item);
        if (Present(item, true, shownApp)) return;
        ShowToast(new ToastSpec(title, string.IsNullOrEmpty(body) ? null : body, null, "", null, null, shownApp.Length > 14 ? shownApp[..14] : shownApp, 5,
            OnClick: () => SelectTab("alerts")));
    });

    void IBridgeHost.PhoneStatus(int? battery, bool? charging, string? focus) => UiAsync(() =>
    {
        bool wasLow = _phoneBattery is int b0 && b0 <= 20;
        if (battery != null) _phoneBattery = battery;
        if (charging != null) _phoneCharging = charging;
        if (focus != null) { _phoneFocus = focus.Length == 0 || focus.Equals("off", StringComparison.OrdinalIgnoreCase) ? null : focus; PhoneFocusChanged(_phoneFocus); }
        _phoneSeen = DateTime.Now;
        if (_phoneBattery is int b && b <= 20 && !wasLow && _phoneCharging != true)
            ShowToast(new ToastSpec("iPhone battery low", $"{b}%. Plug it in?", null, "", Yellow, b / 100.0, $"{b}%", 5));
        else if (charging == true && battery != null)
            ShowToast(new ToastSpec("iPhone charging", null, null, "", null, battery / 100.0, $"{battery}%", 2.5, "iphone-power"));
        if (_tab == "home") UpdateHome();
    });

    void IBridgeHost.StopFocus() => UiAsync(() =>
    {
        if (_timerEnd == null) return;
        EndFocus(completed: false);
        ShowToast(new ToastSpec("Focus ended from your iPhone", null, Glyph: "", Seconds: 2.5));
    });

    // ───────── laptop → iPhone ─────────

    string IBridgeHost.VapidPublicKey => _s.VapidPublicKey;

    void IBridgeHost.AddPushSubscription(WebPush.Subscription sub)
    {
        int before = Push.Count;
        Push.Add(sub);
        if (Push.Count > before) UiAsync(() => ShowToast(new ToastSpec("iPhone alerts are on", "The cat will tap your iPhone when something happens", Glyph: "", Seconds: 4)));
    }

    void IBridgeHost.TestPush() => Push.Send("Meow from your laptop", "Alerts from " + Environment.MachineName + " are working.", tag: "test");

    IReadOnlyList<InboxItem> IBridgeHost.Inbox() => Push.Inbox();

    public bool SendClipboardToPhone()
    {
        string text = "";
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowToast(new ToastSpec("Nothing to send", "Copy some text first", Glyph: "", Seconds: 2.5));
            return false;
        }
        bool link = Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
        if (link) Push.Send("Link from your laptop", text.Trim(), "link", url: u!.AbsoluteUri);
        else Push.Send("Clipboard from your laptop", text, "text");
        ShowToast(new ToastSpec(Push.Count > 0 ? "Sent to your iPhone" : "No iPhone is set up for alerts", Push.Count > 0 ? (link ? u!.Host : null) : "Set up Bark in Cat Center → iPhone", Glyph: "", Seconds: 2.5));
        return Push.Count > 0;
    }

    public void RingPhone()
    {
        if (Push.Count == 0) { ShowToast(new ToastSpec("No iPhone is set up for alerts", "Set up Bark in Cat Center → iPhone", Glyph: "", Seconds: 3)); return; }
        Push.Send("Meow! Here I am!", "Your laptop is looking for this iPhone.", "ring", tag: "ring");
        ShowToast(new ToastSpec("Ringing your iPhone", "It shows up as a Cat Bridge notification", Glyph: "", Seconds: 3));
    }

    // ───────── iPhone widget on the island home ─────────

    Border PhoneWidget()
    {
        var lead = RingLead(out var ring, out var c);
        c.FontFamily = (FontFamily)Resources["Icons"]; c.Text = ""; c.FontSize = 13;
        var card = Small("alerts", lead, "iPhone", out var sub, out _);
        _homeUpdaters.Add(() =>
        {
            if (_phoneBattery is int b)
            {
                ring.Value = b / 100.0;
                ring.Stroke = _phoneCharging == true ? (Brush)Resources["Accent"] : b <= 20 ? Red : Brushes.White;
                c.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); c.FontSize = 11; c.Text = _phoneCharging == true ? "⚡" : $"{b}";
            }
            else { ring.Value = 0; }
            string? last = _phoneLastApp != null ? $"{_phoneLastApp} {Ago(_phoneLastTime)}" : null;
            sub.Text = _phoneFocus != null ? $"{_phoneFocus} focus" + (last != null ? " · " + last : "")
                : last ?? (_phoneSeen == DateTime.MinValue ? (Push.Count > 0 ? "Alerts on" : "Set up in iPhone bridge") : "Connected");
        });
        return card;
    }
}
