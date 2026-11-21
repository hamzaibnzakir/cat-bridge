using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Bridge;
using DynamicIsland.Notify;

namespace DynamicIsland;

/// <summary>Find my laptop: where it is, a loud alarm, and lost mode (a message on screen and on the sign-in tile).</summary>
public partial class IslandWindow
{
    // ───────── location ─────────

    static readonly HttpClient IpHttp = new() { Timeout = TimeSpan.FromSeconds(6) };

    async Task<object> IBridgeHost.Locate()
    {
        double? lat = null, lon = null, acc = null; string? source = null, why = null;
        try
        {
            var access = await Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
            if (access == Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed)
            {
                var g = new Windows.Devices.Geolocation.Geolocator { DesiredAccuracy = Windows.Devices.Geolocation.PositionAccuracy.High };
                var pos = await g.GetGeopositionAsync(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(12));
                lat = pos.Coordinate.Point.Position.Latitude; lon = pos.Coordinate.Point.Position.Longitude;
                acc = pos.Coordinate.Accuracy; source = pos.Coordinate.PositionSource.ToString();
            }
            else why = "Location is off for desktop apps on the laptop (Settings → Privacy & security → Location).";
        }
        catch (Exception ex) { why = "Windows couldn't get a location: " + ex.Message; }
        string? publicIp = null;
        try { publicIp = (await IpHttp.GetStringAsync("https://api.ipify.org")).Trim(); } catch { }
        return new
        {
            latitude = lat, longitude = lon, accuracy = acc, source, problem = why, publicIp,
            wifi = _power.Wifi ? _power.Ssid : null, battery = _power.HasBattery ? _power.Percent : (int?)null, charging = _power.Charging,
            locked = _sessionLocked, lost = _s.LostMode, time = DateTime.Now,
        };
    }

    // ───────── alarm ─────────

    Window? _alarmWin;
    SoundPlayer? _alarmPlayer;
    float? _alarmOldVolume; bool? _alarmOldMute;

    void IBridgeHost.Alarm(bool on) => UiAsync(() => { if (on) StartAlarm(); else StopAlarm(); });

    public void StartAlarm()
    {
        if (_alarmPlayer != null) return;
        try
        {
            _alarmOldVolume = _audio.MasterVolume; _alarmOldMute = _audio.MasterMuted;
            _audio.MasterMuted = false; _audio.MasterVolume = 1f;
            _alarmPlayer = new SoundPlayer(new MemoryStream(AlarmWav()));
            _alarmPlayer.PlayLooping();
        }
        catch (Exception ex) { App.Log(ex); }
        _alarmWin = FindMeWindow("Your iPhone is looking for this laptop", "Click anywhere to stop the sound.", StopAlarm);
        _alarmWin.Show();
        BridgeEvent?.Invoke("ping", "");
    }

    public void StopAlarm()
    {
        try { _alarmPlayer?.Stop(); _alarmPlayer?.Dispose(); } catch { }
        _alarmPlayer = null;
        if (_alarmOldVolume is float v) { _audio.MasterVolume = v; if (_alarmOldMute is bool m) _audio.MasterMuted = m; }
        _alarmOldVolume = null;
        try { _alarmWin?.Close(); } catch { }
        _alarmWin = null;
    }

    /// Two-tone chirp, 1.2 s, looped (made in code, nothing to ship).
    static byte[] AlarmWav()
    {
        const int rate = 22050;
        var samples = new List<short>();
        void Tone(double f, double ms)
        {
            int n = (int)(rate * ms / 1000);
            for (int i = 0; i < n; i++)
            {
                double env = Math.Min(1, Math.Min(i, n - i) / (rate * 0.01));
                samples.Add((short)(Math.Sin(2 * Math.PI * f * i / rate) * env * 26000));
            }
        }
        for (int r = 0; r < 3; r++) { Tone(1760, 120); Tone(2349, 120); }
        Tone(0, 480);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + samples.Count * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples.Count * 2);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }

    // ───────── lost mode ─────────

    Window? _lostWin;

    void IBridgeHost.LostMode(bool on, string? message) => UiAsync(() => SetLostMode(on, message));

    public void SetLostMode(bool on, string? message)
    {
        _s.LostMode = on;
        if (message != null) _s.LostMessage = message.Trim();
        _s.Save();
        ApplyLostMode(lockNow: on);
    }

    void ApplyLostMode(bool lockNow = false)
    {
        var msg = string.IsNullOrWhiteSpace(_s.LostMessage) ? "This laptop is lost. Please contact its owner." : _s.LostMessage;
        LockFeedWriter.LostMessage = _s.LostMode ? "Lost laptop: " + msg : null;
        LockFeedWriter.Write(_s);
        try { _lostWin?.Close(); } catch { }
        _lostWin = null;
        if (_s.LostMode)
        {
            _lostWin = FindMeWindow("This laptop is lost", msg, null);
            _lostWin.Show();
            if (lockNow) LockWorkStationNow();
        }
    }

    static void LockWorkStationNow() { try { LockWorkStation(); } catch { } }

    /// Big, calm, topmost card in the middle of the screen.
    Window FindMeWindow(string title, string text, Action? onClick)
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
            Topmost = true, ShowInTaskbar = false, WindowState = WindowState.Maximized, ResizeMode = ResizeMode.NoResize,
        };
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), CornerRadius = new CornerRadius(28), Padding = new Thickness(40, 34, 40, 34),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x7A)), BorderThickness = new Thickness(2), MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var sp = new StackPanel();
        sp.Children.Add(new CatFace { Width = 110, Height = 92, Mood = FaceMood.Surprised, HorizontalAlignment = HorizontalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = title, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 8) });
        sp.Children.Add(new TextBlock { Text = text, FontSize = 18, Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD6)), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        card.Child = sp;
        w.Content = card;
        if (onClick != null) { w.MouseLeftButtonUp += (_, _) => onClick(); w.KeyDown += (_, _) => onClick(); }
        return w;
    }
}
