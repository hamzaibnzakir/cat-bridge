using System;
using System.Collections.Generic;
using System.Windows;

namespace DynamicIsland;

/// <summary>What Cat Center (the desktop app) reads from and does to the island.</summary>
public partial class IslandWindow
{
    public AppSettings Settings => _s;
    public ShelfStore Shelf => _shelf;
    public int? PhoneBattery => _phoneBattery;
    public bool? PhoneCharging => _phoneCharging;
    public string? PhoneFocus => _phoneFocus;
    public DateTime PhoneSeen => _phoneSeen;
    public string? PhoneLastApp => _phoneLastApp;
    public DateTime PhoneLastTime => _phoneLastTime;
    public int BatteryPercent => _power.Percent;
    public bool BatteryCharging => _power.Charging;
    public bool HasBattery => _power.HasBattery;
    public string? NowPlaying => _media.Current is { } m && !string.IsNullOrWhiteSpace(m.Title) ? m.Title + (string.IsNullOrWhiteSpace(m.Artist) ? "" : " · " + m.Artist) : null;

    /// Call after changing Settings in place: applies everything live and saves.
    public void SettingsChanged(bool rebuildHome = true)
    {
        _s.Save();
        ApplySettings();
        Reposition();
        if (rebuildHome) { BuildHome(); RebuildDock(); }
    }

    public void ShowIslandPage(string tab) => OpenIsland(tab);
    public void ShowNotice(string title, string? sub = null) => ShowToast(new ToastSpec(title, sub, Glyph: "", Seconds: 3));
    public void RestartBridge() { StartBridge(); }
    public void ClearNotifications() { _notes.ClearHistory(); NotificationsChanged?.Invoke(); if (_tab == "alerts") RefreshAlerts(); }
}

public partial class IslandWindow
{
    /// "Cat Bridge 2.1.0 is ready" pop-up with Install / Later.
    public void ShowUpdateToast(Updater.Release r)
    {
        ShowToast(new ToastSpec($"Cat Bridge {r.Version.ToString(3)} is ready",
            Updater.Installed ? "Click to install. It takes a few seconds." : "Click to download it",
            Glyph: "", Seconds: 10,
            OnClick: () => _ = Updater.InstallAsync(quiet: false)));
    }
}
