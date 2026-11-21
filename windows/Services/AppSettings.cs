using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DynamicIsland;

public sealed class MonitorTarget
{
    public string Name { get; set; } = "";
    /// <summary>"tcp" (host + port) or "http" (url).</summary>
    public string Type { get; set; } = "tcp";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Url { get; set; } = "";
}

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 0; // missing in older files → treated as old
    public string Accent { get; set; } = "#FFB37A";

    // ── cat ──
    public bool PetEnabled { get; set; } = true;
    /// <summary>ginger | black | white | grey</summary>
    public string PetColor { get; set; } = "ginger";
    /// <summary>The cat reminds you to stretch after this many minutes of continuous work. 0 = off.</summary>
    public int BreakReminderMinutes { get; set; } = 50;

    /// <summary>Widgets on the island's home, in order: media, system, battery, sound, alerts, timer, shelf, servers.</summary>
    public List<string> Widgets { get; set; } = new() { "media", "iphone", "system", "battery", "sound", "alerts", "timer", "shelf" };

    /// <summary>Hermes features (prompt, queue, command center) are parked for now.</summary>
    public bool HermesEnabled { get; set; } = false;

    // ── iPhone bridge (Tailscale) ──
    public bool BridgeEnabled { get; set; } = true;
    public int BridgePort { get; set; } = 47800;
    /// <summary>Pairing key your iPhone stores. Changing it unpairs every phone.</summary>
    public string BridgeKey { get; set; } = "";

    /// <summary>Also listen on this laptop's Tailscale address (100.x.x.x) so the iPhone app can connect directly.</summary>
    public bool BridgeListenTailscale { get; set; } = true;
    /// <summary>Optional extra address to listen on (leave empty). Never use 0.0.0.0.</summary>
    public string BridgeExtraAddress { get; set; } = "";

    // ── alerts to the iPhone (Bark app) ──
    public string BarkServer { get; set; } = "https://api.day.app";
    public string BarkKey { get; set; } = "";
    /// <summary>Same key and IV as Bark → Settings → Encryption (AES-CBC). Empty = not encrypted.</summary>
    public string BarkEncryptKey { get; set; } = "";
    public string BarkEncryptIv { get; set; } = "";
    public string BarkSound { get; set; } = "";

    /// <summary>While plugged in, keep Windows from sleeping so your iPhone can always reach it (the screen still turns off).</summary>
    public bool StayAwakeOnPower { get; set; } = false;

    // ── focus (both devices) ──
    /// <summary>Hold banners and pop-ups during focus (they wait in Alerts).</summary>
    public bool FocusSilence { get; set; } = true;
    /// <summary>Your iPhone's Focus (via the Update Laptop automation) also puts the laptop in focus.</summary>
    public bool FollowPhoneFocus { get; set; } = true;
    /// <summary>...and starts a Pomodoro timer.</summary>
    public bool PhoneFocusStartsTimer { get; set; } = false;
    public int PomodoroMinutes { get; set; } = 25;
    /// <summary>Starting focus on the laptop taps your iPhone (via Bark) to run a Shortcut that turns its Focus on.</summary>
    public bool FocusTapsPhone { get; set; } = true;
    public string FocusShortcutOn { get; set; } = "Cat Focus On";
    public string FocusShortcutOff { get; set; } = "Cat Focus Off";

    /// <summary>Screenshots you take (Win+Shift+S, Print Screen) are saved and put on the shelf.</summary>
    public bool ScreenshotShelf { get; set; } = true;

    public bool ShowDownloads { get; set; } = true;
    public bool DownloadsToShelf { get; set; } = false;
    public bool ShowBluetoothBattery { get; set; } = true;

    // ── find my laptop ──
    public bool LostMode { get; set; } = false;
    public string LostMessage { get; set; } = "";

    // ── updates ──
    /// <summary>auto = download and install new versions quietly; notify = ask first; off = never check.</summary>
    public string UpdateMode { get; set; } = "notify";
    /// <summary>Where releases come from (owner/repo on GitHub). Point it at your fork if you build your own.</summary>
    public string UpdateRepo { get; set; } = "hamzaibnzakir/cat-bridge";
    public string SkippedVersion { get; set; } = "";

    // ── taskbar replacement ──
    /// <summary>Hide the Windows taskbar; the island's dock and Apps page take over. Restored when the app exits.</summary>
    public bool HideTaskbar { get; set; } = false;
    /// <summary>Show running and pinned apps at the top of the island's home.</summary>
    public bool DockOnHome { get; set; } = true;
    /// <summary>Hovering an app in the dock shows a live preview of its window.</summary>
    public bool DockPreviews { get; set; } = true;

    // ── Mac-style notification banners ──
    public bool BannersForPhone { get; set; } = true;
    public bool BannersForWindows { get; set; } = false;
    /// <summary>right (like a Mac) or center (under the island).</summary>
    public string BannerPosition { get; set; } = "right";
    public int BannerSeconds { get; set; } = 6;
    /// <summary>What the Cat Face sign-in tile shows: off | counts | details.</summary>
    public string LockScreenFeed { get; set; } = "counts";

    // ── alerts to the iPhone (Web Push, for the old Safari app) ──
    public string VapidPublicKey { get; set; } = "";
    public string VapidPrivateKey { get; set; } = "";
    /// <summary>Contact for Apple's push service (required by the standard). Must not be a localhost address.</summary>
    public string PushSubject { get; set; } = "mailto:catbridge@users.noreply.github.com";
    /// <summary>Only send "while you're away" alerts after this many minutes without keyboard or mouse use.</summary>
    public int PushAwayMinutes { get; set; } = 2;
    public bool PushForwardNotifications { get; set; } = true;
    public bool PushTimer { get; set; } = true;
    public bool PushBattery { get; set; } = true;
    public bool PushServers { get; set; } = true;

    // ── face unlock ──
    /// <summary>Cosine similarity needed to count as you (0–1). Higher = stricter. Tested: same person 0.83–0.98, others ≤ 0.32.</summary>
    public double FaceMatchThreshold { get; set; } = 0.42;
    public int CameraIndex { get; set; } = 0;
    /// <summary>Set true if the "turn left/right" check always asks for the opposite direction on your camera.</summary>
    public bool FaceTurnFlip { get; set; } = false;
    public bool LyricsInPill { get; set; } = true;
    public bool ShowVolumeOsd { get; set; } = true;
    public bool ShowBrightnessOsd { get; set; } = true;
    public bool HideInFullscreen { get; set; } = false;
    public int TopOffset { get; set; } = 6;

    /// How long the cursor must rest on the island before it opens (passing over it to reach tabs won't open it).
    public int HoverDelayMs { get; set; } = 180;
    /// How long after the cursor leaves before it shrinks back to the notch.
    public int CollapseDelayMs { get; set; } = 300;
    /// A pinned island releases itself after this many seconds with the cursor away. 0 = stay pinned.
    public int PinTimeoutSeconds { get; set; } = 60;

    public bool Gestures { get; set; } = true;
    public bool PrivacyDots { get; set; } = true;

    public bool Notifications { get; set; } = true;
    public bool NotificationsSuppressNative { get; set; } = false;

    public bool ClipboardHistory { get; set; } = true;
    public bool ClipboardAutoPaste { get; set; } = true;

    public string BrainboxStatusUrl { get; set; } = "";
    public string BrainboxToken { get; set; } = "";
    public string HermesQueuePath { get; set; } = @"C:\Brainbox\automation\queue";

    public int MonitorIntervalSeconds { get; set; } = 30;
    public List<MonitorTarget> Monitors { get; set; } = new()
    {
        new() { Name = "Firecrawl tunnel", Type = "tcp", Host = "127.0.0.1", Port = 3002 },
        new() { Name = "VPS SSH", Type = "tcp", Host = "", Port = 22 },
    };

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
    public static string FilePath => Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppSettings Load()
    {
        AppSettings? s = null;
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts);
        }
        catch (Exception ex) { App.Log(ex); return new AppSettings(); } // don't overwrite a file the user is mid-edit on
        s ??= new AppSettings();
        if (s.SettingsVersion < 3)
        {
            // v1.3 cat redesign: move off the old default accent, keep anything custom
            if (string.Equals(s.Accent, "#39FF88", StringComparison.OrdinalIgnoreCase)) s.Accent = "#FFB37A";
            s.SettingsVersion = 3;
        }
        if (s.SettingsVersion < 4)
        {
            // v1.4 speed pass: only replace values that are still the old defaults
            if (s.HoverDelayMs == 300) s.HoverDelayMs = 180;
            if (s.CollapseDelayMs == 450) s.CollapseDelayMs = 300;
            s.SettingsVersion = 4;
        }
        if (string.IsNullOrWhiteSpace(s.VapidPublicKey) || string.IsNullOrWhiteSpace(s.VapidPrivateKey))
            (s.VapidPublicKey, s.VapidPrivateKey) = Bridge.WebPush.NewVapidKeys();
        if (s.SettingsVersion < 5)
        {
            // v1.7: add the iPhone widget to the island home
            if (s.Widgets != null && !s.Widgets.Contains("iphone")) s.Widgets.Insert(Math.Min(1, s.Widgets.Count), "iphone");
            s.SettingsVersion = 5;
        }
        if (s.SettingsVersion < 7)
        {
            // v2.2: Shopify is an ordinary notification now (no separate widget)
            s.Widgets?.Remove("sales");
            s.SettingsVersion = 7;
        }
        if (string.IsNullOrWhiteSpace(s.BridgeKey) || s.BridgeKey.Length < 32) s.BridgeKey = Bridge.BridgeServer.NewKey();
        s.Save(); // writes any newly added settings back so they show up in the file
        return s;
    }

    /// Copies every setting from another instance (so windows holding this object see reloaded values).
    public void CopyFrom(AppSettings o)
    {
        foreach (var p in typeof(AppSettings).GetProperties())
            if (p.CanRead && p.CanWrite) p.SetValue(this, p.GetValue(o));
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
