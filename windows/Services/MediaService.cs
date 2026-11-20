using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace DynamicIsland;

public sealed class MediaInfo
{
    public string Title = "", Artist = "", Album = "", App = "";
    public ImageSource? Art;
    public bool IsPlaying;
    public TimeSpan Position, Duration;
    public DateTimeOffset Updated;
}

/// <summary>Reads "now playing" from Windows' media session API (Spotify, browsers, Apple Music, VLC…).</summary>
public sealed class MediaService
{
    readonly Dispatcher _ui;
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _s;
    public MediaInfo? Current { get; private set; }
    public event Action? Changed;

    public MediaService(Dispatcher ui) => _ui = ui;

    public async Task InitAsync()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(Reevaluate);
            _mgr.SessionsChanged += (_, _) => _ui.InvokeAsync(Reevaluate);
            Reevaluate();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    /// Prefer whichever session is actually playing; otherwise Windows' "current" one.
    public void Reevaluate()
    {
        if (_mgr == null) return;
        GlobalSystemMediaTransportControlsSession? pick = null;
        try
        {
            var sessions = _mgr.GetSessions();
            pick = sessions.FirstOrDefault(x =>
                x.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                ?? _mgr.GetCurrentSession();
        }
        catch { }
        if (pick == null && _s == null) return;
        if (pick != null && _s != null && pick.SourceAppUserModelId == _s.SourceAppUserModelId) return;
        Attach(pick);
    }

    void Attach(GlobalSystemMediaTransportControlsSession? s)
    {
        if (_s != null)
        {
            _s.MediaPropertiesChanged -= OnProps;
            _s.PlaybackInfoChanged -= OnPlayback;
            _s.TimelinePropertiesChanged -= OnPlayback;
        }
        _s = s;
        if (s == null) { Current = null; Changed?.Invoke(); return; }
        s.MediaPropertiesChanged += OnProps;
        s.PlaybackInfoChanged += OnPlayback;
        s.TimelinePropertiesChanged += OnPlayback;
        _ = LoadPropsAsync(s);
    }

    void OnProps(GlobalSystemMediaTransportControlsSession s, object _) => _ui.InvokeAsync(() => LoadPropsAsync(s));
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, object _) => _ui.InvokeAsync(() =>
    {
        if (!ReferenceEquals(s, _s) || Current == null) return;
        ReadPlayback(s, Current);
        Changed?.Invoke();
    });

    async Task LoadPropsAsync(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(s, _s) || p == null) return;
            var info = new MediaInfo
            {
                Title = p.Title ?? "",
                Artist = string.IsNullOrWhiteSpace(p.Artist) ? p.AlbumArtist ?? "" : p.Artist,
                Album = p.AlbumTitle ?? "",
                App = PrettyApp(s.SourceAppUserModelId),
            };
            // Keep the old artwork if the track didn't change (avoids flicker).
            if (Current != null && Current.Title == info.Title && Current.Artist == info.Artist) info.Art = Current.Art;
            if (p.Thumbnail != null)
            {
                try
                {
                    using var ras = await p.Thumbnail.OpenReadAsync();
                    using var st = ras.AsStreamForRead();
                    var ms = new MemoryStream();
                    await st.CopyToAsync(ms);
                    ms.Position = 0;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 200;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    info.Art = bi;
                }
                catch { }
            }
            if (!ReferenceEquals(s, _s)) return;
            ReadPlayback(s, info);
            Current = info;
            Changed?.Invoke();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static void ReadPlayback(GlobalSystemMediaTransportControlsSession s, MediaInfo m)
    {
        try
        {
            m.IsPlaying = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var tl = s.GetTimelineProperties();
            m.Position = tl.Position;
            m.Duration = tl.EndTime - tl.StartTime;
            var u = tl.LastUpdatedTime;
            m.Updated = (u.Year > 2000 && DateTimeOffset.Now - u < TimeSpan.FromHours(12)) ? u : DateTimeOffset.Now;
        }
        catch { }
    }

    public TimeSpan EstimatedPosition
    {
        get
        {
            var m = Current;
            if (m == null) return TimeSpan.Zero;
            var p = m.IsPlaying ? m.Position + (DateTimeOffset.Now - m.Updated) : m.Position;
            if (p < TimeSpan.Zero) p = TimeSpan.Zero;
            if (m.Duration > TimeSpan.Zero && p > m.Duration) p = m.Duration;
            return p;
        }
    }

    public async void TogglePlay() { try { if (_s != null) await _s.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (_s != null) await _s.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_s != null) await _s.TrySkipPreviousAsync(); } catch { } }
    public async void Seek(TimeSpan t) { try { if (_s != null) await _s.TryChangePlaybackPositionAsync(t.Ticks); } catch { } }

    static string PrettyApp(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var n = id.Contains('!') ? id[(id.LastIndexOf('!') + 1)..] : id;
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n.ToLowerInvariant() switch
        {
            "msedge" => "Edge",
            "chrome" => "Chrome",
            "firefox" => "Firefox",
            "spotify" => "Spotify",
            "app" when id.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) => "Apple Music",
            "microsoft.zunemusic" or "microsoft.zunevideo" => "Media Player",
            _ => char.ToUpperInvariant(n[0]) + n[1..],
        };
    }
}
