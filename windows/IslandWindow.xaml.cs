using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland;

public sealed record ToastSpec(
    string Title, string? Sub = null, ImageSource? Image = null, string? Glyph = null,
    Brush? GlyphBrush = null, double? Progress = null, string? Right = null,
    double Seconds = 3.5, string? Key = null, Action? OnClick = null);

public partial class IslandWindow : Window
{
    enum Mode { Compact, Toast, Expanded }

    const double CompactH = 34, ToastW = 390, ToastH = 70, ExpW = 476, ExpH = 392, MinCompactW = 206, MaxCompactW = 460;

    AppSettings _s;
    readonly MediaService _media;
    readonly LyricsService _lyrics = new();
    readonly SystemStatsService _stats = new();
    readonly PowerNetService _power = new();
    readonly BrainboxService _brain = new();
    readonly AudioService _audio = new();
    readonly NotificationService _notes;
    readonly BrightnessService _bright = new();
    readonly PrivacyService _privacy = new();
    readonly MonitorService _monitors = new();
    readonly ShelfStore _shelf = new();
    readonly ClipboardHistory _clip = new();
    DateTime _lastMonitorCheck = DateTime.MinValue;
    bool _monitorBusy, _syncing, _keyboardOn;
    IntPtr _prevForeground;
    List<AppAudio> _apps = new();
    string _appKeys = "";
    // gestures
    Point? _downPt;
    bool _gestureUsed;
    readonly DispatcherTimer _longPress = new() { Interval = TimeSpan.FromMilliseconds(650) };

    Mode _mode = Mode.Compact;
    bool _hover, _hidden, _fsHidden;
    readonly IslandState _st = new();
    readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly DispatcherTimer _dragLeave = new() { Interval = TimeSpan.FromMilliseconds(300) };
    string _tab = "media";
    double _tw = 190, _th = CompactH;
    readonly Dictionary<UIElement, bool> _want = new();

    // media / lyrics
    string? _trackKey;
    Lyrics? _lyr;
    bool _lyrLoading;
    int _lyrIdx = -1;

    // timer
    DateTime? _timerEnd;
    TimeSpan _timerTotal;

    // toasts
    readonly Queue<ToastSpec> _toasts = new();
    ToastSpec? _toast;
    readonly DispatcherTimer _toastTimer = new();

    readonly DispatcherTimer _fast = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _slow = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _viz = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Rectangle[] _bars;
    readonly Rectangle[] _wifi;
    double[] _barH = new double[5];
    int _tick;
    IntPtr _hwnd;
    FileSystemWatcher? _queueWatcher;
    bool _brainBusy;
    DateTime _ignoreVolumeUntil;

    public IslandWindow(AppSettings s)
    {
        _s = s;
        InitializeComponent();
        _media = new MediaService(Dispatcher);
        _notes = new NotificationService(Dispatcher);
        _bars = new[] { Bar0, Bar1, Bar2, Bar3, Bar4 };
        _wifi = new[] { W0, W1, W2, W3 };
        ApplySettings();

        Island.SizeChanged += (_, _) =>
        {
            var r = new CornerRadius(Math.Min(Island.ActualHeight / 2, 32));
            Island.CornerRadius = r;
            Shadow.CornerRadius = r;
            PlaceEars();
        };

        _toastTimer.Tick += (_, _) => EndToast();
        _watch.Tick += (_, _) => WatchTick();
        _dragLeave.Tick += (_, _) => { _dragLeave.Stop(); _st.SetDragging(false, DateTime.Now); ShelfDrop.BorderBrush = ShelfIdleBorder; Reconcile(); };
        _fast.Tick += (_, _) => FastTick();
        _slow.Tick += async (_, _) => await SlowTick();
        _viz.Tick += (_, _) => VizTick();
        PromptBox.TextChanged += (_, _) => PromptHint.Visibility = PromptBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _longPress.Tick += (_, _) => { _longPress.Stop(); _downPt = null; _gestureUsed = _s.HermesEnabled; if (_s.HermesEnabled) OpenPrompt(); };
        Deactivated += (_, _) => { if (_keyboardOn) { SetKeyboard(false, restoreFocus: false); Reconcile(); } };

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += (_, _) => { StopBridge(); _stats.Dispose(); _audio.Dispose(); _bright.Dispose(); _queueWatcher?.Dispose(); };
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(Reposition);
    }

    // ───────────────────────── setup ─────────────────────────

    void ApplySettings()
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_s.Accent);
            Resources["Accent"] = new SolidColorBrush(c);
        }
        catch { }
        Ears.Margin = new Thickness(0, Math.Max(0, _s.TopOffset), 0, 0);
        Island.Margin = new Thickness(0, Math.Max(0, _s.TopOffset) + 15, 0, 0); // room for the ears
        CatFace.CurrentPalette = CatArt.Palette(_s.PetColor);
        _st.HoverDelayMs = Math.Clamp(_s.HoverDelayMs, 0, 2000);
        _st.CollapseDelayMs = Math.Clamp(_s.CollapseDelayMs, 100, 5000);
        _st.PinTimeoutSeconds = Math.Max(0, _s.PinTimeoutSeconds);
        SetupQueueWatcher();
    }

    public void ReloadSettings()
    {
        _s.CopyFrom(AppSettings.Load()); // keep the same object: the tray, cat and Cat Center hold it too
        ApplySettings();
        Reposition();
        BuildHome();
        if (_s.HermesEnabled) _ = RefreshBrainAsync();
        ShowToast(new ToastSpec("Settings reloaded", AppSettings.FilePath, Glyph: "", Seconds: 2.5));
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
        ex = (ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE) & ~Native.WS_EX_APPWINDOW;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));

        Native.RegisterHotKey(_hwnd, 1, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x20); // Ctrl+Alt+Space
        Native.RegisterHotKey(_hwnd, 2, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x48); // Ctrl+Alt+H
        if (_s.HermesEnabled) Native.RegisterHotKey(_hwnd, 3, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x51); // Ctrl+Alt+Q
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Native.RegisterHotKey(_hwnd, 4, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x41); // Ctrl+Alt+A: apps
        Native.RegisterHotKey(_hwnd, 5, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x43); // Ctrl+Alt+C: Cat Center
        StartDock();
        Native2.AddClipboardFormatListener(_hwnd);
        Closed += (_, _) =>
        {
            for (int i = 1; i <= 5; i++) Native.UnregisterHotKey(_hwnd, i);
            StopDock();
            Native2.RemoveClipboardFormatListener(_hwnd);
        };
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (DockWndProc(msg, w, l)) { handled = true; return IntPtr.Zero; }
        if (msg == Native.WM_HOTKEY)
        {
            if (w.ToInt32() == 1) TogglePinned();
            if (w.ToInt32() == 2) ToggleHidden();
            if (w.ToInt32() == 3) OpenPrompt();
            if (w.ToInt32() == 4) OpenAppSearch();
            if (w.ToInt32() == 5) Center.CenterWindow.Open(this);
            handled = true;
        }
        else if (msg == Native2.WM_CLIPBOARDUPDATE && _s.ClipboardHistory)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (_clip.Capture() && _tab == "shelf") RefreshShelf(); }));
        }
        return IntPtr.Zero;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Show the notch immediately; everything below loads in parallel afterwards.
        Reposition();
        BuildHome();
        SelectTab("home");
        SetMode(Mode.Compact);
        _slow.Start();
        StartEarTwitch();

        _media.Changed += OnMediaChanged;
        var mediaInit = _media.InitAsync();

        _bright.Changed += lvl => Dispatcher.InvokeAsync(() => OnBrightness(lvl));
        _ = System.Threading.Tasks.Task.Run(_bright.StartWatching); // WMI is slow to start; keep it off the UI thread
        _shelf.Load();
        if (_s.Notifications)
        {
            _notes.Added += OnNotification;
            _ = _notes.InitAsync();
        }
        _stats.Start();

        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        _audio.EnsureDevice();
        _audio.VolumeChanged += (v, muted) => Dispatcher.InvokeAsync(() => OnVolume(v, muted));
        _ignoreVolumeUntil = DateTime.Now.AddSeconds(2);

        StartBridge();
        WirePhoneAlerts();
        WireNotifyExtras();
        await mediaInit;
        await SlowTick();
        UpdateTimers();
        if (_s.HermesEnabled) _ = RefreshBrainAsync();
    }

    void Reposition()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        KeepOnTop();
    }

    void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // ───────────────────────── public controls (tray / hotkeys) ─────────────────────────

    public void TogglePinned()
    {
        if (_hidden) ToggleHidden();
        _st.TogglePin(DateTime.Now);
        Reconcile();
    }

    void PinBtn_Click(object sender, RoutedEventArgs e) { TogglePinned(); e.Handled = true; }

    void UpdatePinUi()
    {
        HdrPin.Foreground = _st.Pinned ? (Brush)Resources["Accent"] : (Brush)Resources["Faint"];
        HdrPin.Background = _st.Pinned ? new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E)) : Brushes.Transparent;
        HdrPin.ToolTip = _st.Pinned ? "Pinned open — click to unpin" : "Pin open (Ctrl+Alt+Space)";
    }

    /// Single place that decides which mode the island is in. Everything else just changes inputs and calls this.
    void Reconcile()
    {
        _st.ToastActive = _toast != null;
        _st.ToastClickable = _toast?.OnClick != null;
        var want = _st.Mode switch { IslandMode.Expanded => Mode.Expanded, IslandMode.Toast => Mode.Toast, _ => Mode.Compact };
        if (want != _mode) SetMode(want);
        UpdatePinUi();
        Toggle(_watch, _st.NeedsWatch);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    void WatchTick()
    {
        bool inside = CursorOverIsland();
        _hover = inside;
        bool leftDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
        bool wasTyping = _st.Typing;
        _st.Tick(DateTime.Now, inside, leftDown, IsActive);
        var catMood = inside ? FaceMood.Awake : FaceMood.Sleep; // the notch cat wakes up as you reach for it
        if (CompactCat.Mood != catMood) CompactCat.Mood = catMood;
        if (wasTyping && !_st.Typing && _keyboardOn) SetKeyboard(false, restoreFocus: false);
        Reconcile();
    }

    public void ToggleHidden()
    {
        _hidden = !_hidden;
        ApplyVisibility();
    }

    void ApplyVisibility()
    {
        bool show = !_hidden && !_fsHidden;
        Root.IsHitTestVisible = show;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(200)));
    }

    // ───────────────────────── mode / animation ─────────────────────────

    void SetMode(Mode m)
    {
        _mode = m;
        DockModeChanged(m == Mode.Expanded);
        if (m != Mode.Expanded && _tab != "home") SelectTab("home"); // next open starts on the widget home
        Fade(CompactView, m == Mode.Compact, 50);
        Fade(ToastView, m == Mode.Toast, 60);
        Fade(ExpandedView, m == Mode.Expanded, 70);
        switch (m)
        {
            case Mode.Compact: UpdateCompact(); break;
            case Mode.Toast: AnimateTo(ToastW, ToastH); break;
            case Mode.Expanded: AnimateTo(ExpW, ExpH); RefreshExpanded(); break;
        }
        UpdateTimers();
    }

    /// Adaptive polling (ported idea from PILLAR): fast timers only run while something on screen needs them.
    void UpdateTimers()
    {
        bool playing = _media.Current is { IsPlaying: true };
        bool needFast = _timerEnd != null || (_mode == Mode.Expanded && _tab == "media") || (playing && _lyr is { Synced: true });
        Toggle(_fast, needFast);
        Toggle(_viz, _mode == Mode.Compact && VizPanel.Visibility == Visibility.Visible);
        bool busy = _mode != Mode.Compact || playing || _timerEnd != null || _hover;
        var want = TimeSpan.FromSeconds(busy ? 1 : 3);
        if (_slow.Interval != want) _slow.Interval = want;
        _stats.Active = _mode == Mode.Expanded && (_tab == "system" || _tab == "home");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out POINTW p);
    struct POINTW { public int X, Y; }

    bool CursorOverIsland()
    {
        try
        {
            if (!GetCursorPos(out var c) || !Island.IsVisible) return false;
            var tl = Island.PointToScreen(new Point(0, 0));
            var br = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
            const double m = 4; // small forgiveness margin around the edge
            return c.X >= tl.X - m && c.X <= br.X + m && c.Y >= tl.Y - m && c.Y <= br.Y + m;
        }
        catch { return false; }
    }

    static void Toggle(DispatcherTimer t, bool on) { if (on && !t.IsEnabled) t.Start(); else if (!on && t.IsEnabled) t.Stop(); }


    void AnimateTo(double w, double h)
    {
        if (Math.Abs(w - _tw) < 0.5 && Math.Abs(h - _th) < 0.5) return;
        bool growing = w * h > _tw * _th;
        _tw = w; _th = h;
        var dur = TimeSpan.FromMilliseconds(growing ? 420 : 300);
        IEasingFunction ease = growing
            ? new SpringEase { Damping = 7, Frequency = 15 }    // quick pop with a small bounce
            : new SpringEase { Damping = 9.5, Frequency = 12 }; // snaps back into the notch
        Island.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
        Island.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
    }

    void Fade(UIElement el, bool on, int delayMs)
    {
        if (_want.TryGetValue(el, out var cur) && cur == on && (on ? el.Visibility == Visibility.Visible : true)) return;
        _want[el] = on;
        if (on)
        {
            el.Visibility = Visibility.Visible;
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { BeginTime = TimeSpan.FromMilliseconds(delayMs) });
        }
        else
        {
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(80));
            a.Completed += (_, _) => { if (!_want[el]) el.Visibility = Visibility.Collapsed; };
            el.BeginAnimation(OpacityProperty, a);
        }
    }

    // ───────────────────────── hover / click ─────────────────────────

    void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _hover = true;
        if (_mode == Mode.Toast && _toast != null) { _toastTimer.Stop(); _toastTimer.Start(); } // reading it? keep it up
        WatchTick(); // starts the dwell; the watcher takes it from here
    }

    void Island_MouseLeave(object sender, MouseEventArgs e) => _hover = false; // closing is decided by the cursor watcher

    void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        if (_st.Click(DateTime.Now)) { var act = _toast?.OnClick; act?.Invoke(); EndToast(); return; }
        Reconcile(); // clicking opens it, never pins it
    }

    // ───────────────────────── toasts ("live activities") ─────────────────────────

    void ShowToast(ToastSpec t)
    {
        if (t.Key != null && _toast?.Key == t.Key)
        {
            _toast = t; PopulateToast(t);
            _toastTimer.Stop(); _toastTimer.Interval = TimeSpan.FromSeconds(t.Seconds); _toastTimer.Start();
            return;
        }
        _toasts.Enqueue(t);
        if (_toast == null) NextToast();
    }

    void NextToast()
    {
        if (_toasts.Count == 0) { _toast = null; Reconcile(); return; }
        _toast = _toasts.Dequeue();
        PopulateToast(_toast);
        Reconcile();
        _toastTimer.Stop(); _toastTimer.Interval = TimeSpan.FromSeconds(_toast.Seconds); _toastTimer.Start();
    }

    void EndToast() { _toastTimer.Stop(); _toast = null; NextToast(); }

    void PopulateToast(ToastSpec t)
    {
        ToastTitle.Text = t.Title;
        ToastSub.Text = t.Sub ?? "";
        ToastSub.Visibility = string.IsNullOrEmpty(t.Sub) ? Visibility.Collapsed : Visibility.Visible;
        ToastRight.Text = t.Right ?? "";
        if (t.Image != null)
        {
            ToastArt.Background = new ImageBrush(t.Image) { Stretch = Stretch.UniformToFill };
            ToastGlyph.Text = "";
        }
        else
        {
            ToastArt.Background = (Brush)Resources["Card"];
            ToastGlyph.Text = t.Glyph ?? "";
            ToastGlyph.Foreground = t.GlyphBrush ?? (Brush)Resources["Accent"];
        }
        if (t.Progress is double p)
        {
            ToastBar.Visibility = Visibility.Visible;
            ToastBarScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(Math.Clamp(p, 0, 1), TimeSpan.FromMilliseconds(120)));
        }
        else ToastBar.Visibility = Visibility.Collapsed;
    }

    static readonly Brush Red = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
    static readonly Brush Yellow = Frozen(Color.FromRgb(0xFF, 0xD6, 0x0A));
    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // ───────────────────────── media + lyrics ─────────────────────────

    void OnMediaChanged()
    {
        var m = _media.Current;
        var key = m == null || string.IsNullOrWhiteSpace(m.Title) ? null : $"{m.Artist}|{m.Title}";
        if (key != _trackKey)
        {
            bool firstLoad = _trackKey == null && !_loadedOnce;
            _trackKey = key; _lyr = null; _lyrIdx = -1;
            if (m != null && key != null)
            {
                if (!firstLoad && m.IsPlaying && _mode != Mode.Expanded)
                    ShowToast(new ToastSpec(m.Title, m.Artist, m.Art, "", Right: m.App, Seconds: 3.2, Key: "track",
                        OnClick: () => { SelectTab("media"); _st.OpenSoft(DateTime.Now); }));
                _ = LoadLyricsAsync(m, key);
            }
            UpdateLyricsUi();
        }
        _loadedOnce = true;
        RefreshMedia();
        if (_mode == Mode.Expanded && _tab == "home") UpdateHome();
        UpdateCompact();
        UpdateTimers();
    }
    bool _loadedOnce;

    async Task LoadLyricsAsync(MediaInfo m, string key)
    {
        _lyrLoading = true; UpdateLyricsUi();
        var l = await _lyrics.GetAsync(m.Artist, m.Title, m.Duration.TotalSeconds);
        if (key != _trackKey) return;
        _lyrLoading = false; _lyr = l; _lyrIdx = -1;
        UpdateLyricsUi();
        UpdateCompact();
        UpdateTimers();
    }

    void RefreshMedia()
    {
        var m = _media.Current;
        bool has = m != null && !string.IsNullOrWhiteSpace(m.Title);
        NoMedia.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        MediaMain.Visibility = LyricsSynced.Visibility = has ? Visibility.Visible : Visibility.Hidden;
        if (!has) { LyricsPlainScroll.Visibility = Visibility.Collapsed; return; }
        MediaTitle.Text = m!.Title;
        MediaArtist.Text = string.IsNullOrEmpty(m.Album) || m.Album == m.Title ? m.Artist : $"{m.Artist} — {m.Album}";
        MediaApp.Text = m.App;
        PlayBtn.Content = m.IsPlaying ? "" : "";
        if (m.Art != null) { MediaArt.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill }; MediaArtGlyph.Text = ""; }
        else { MediaArt.Background = (Brush)Resources["Card"]; MediaArtGlyph.Text = ""; }
        UpdateLyricsUi();
        UpdateProgress();
    }

    void UpdateProgress()
    {
        var m = _media.Current;
        if (m == null) return;
        var pos = _media.EstimatedPosition;
        MediaPos.Text = Fmt(pos);
        MediaDur.Text = m.Duration > TimeSpan.Zero ? Fmt(m.Duration) : "";
        ProgressScale.ScaleX = m.Duration > TimeSpan.Zero ? Math.Clamp(pos / m.Duration, 0, 1) : 0;
    }

    void UpdateLyricsUi()
    {
        bool hasMedia = _media.Current != null && _trackKey != null;
        LyricsPlainScroll.Visibility = Visibility.Collapsed;
        if (!hasMedia) return;
        LyricsSynced.Visibility = Visibility.Visible;
        LyricPrev.Text = LyricNext.Text = "";
        if (_lyrLoading && _lyr == null) { LyricCur.Text = "Finding lyrics…"; LyricCur.Opacity = 0.5; return; }
        LyricCur.Opacity = 1;
        if (_lyr == null) { LyricCur.Text = "No lyrics found for this track"; LyricCur.Opacity = 0.45; return; }
        if (_lyr.Instrumental) { LyricCur.Text = "♪  Instrumental  ♪"; return; }
        if (!_lyr.Synced)
        {
            LyricsSynced.Visibility = Visibility.Collapsed;
            LyricsPlainScroll.Visibility = Visibility.Visible;
            LyricsPlain.Text = string.Join("\n", _lyr.Lines.Select(x => x.Text));
            return;
        }
        var L = _lyr.Lines;
        string T(int i) => i >= 0 && i < L.Count ? (string.IsNullOrWhiteSpace(L[i].Text) ? "♪" : L[i].Text) : "";
        LyricPrev.Text = T(_lyrIdx - 1);
        LyricCur.Text = _lyrIdx < 0 ? "♪" : T(_lyrIdx);
        LyricNext.Text = T(_lyrIdx + 1);
    }

    string? CurrentLyricLine()
    {
        if (_lyr is not { Synced: true } || _lyrIdx < 0 || _lyrIdx >= _lyr.Lines.Count) return null;
        var t = _lyr.Lines[_lyrIdx].Text;
        return string.IsNullOrWhiteSpace(t) ? "♪" : t;
    }

    void TickLyrics()
    {
        if (_lyr is not { Synced: true } l || _media.Current == null) return;
        var pos = _media.EstimatedPosition + TimeSpan.FromMilliseconds(250); // slight lead feels in-sync
        int lo = 0, hi = l.Lines.Count - 1, idx = -1;
        while (lo <= hi) { int mid = (lo + hi) / 2; if (l.Lines[mid].Time <= pos) { idx = mid; lo = mid + 1; } else hi = mid - 1; }
        if (idx == _lyrIdx) return;
        _lyrIdx = idx;
        UpdateLyricsUi();
        var d = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        LyricShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, d) { EasingFunction = ease });
        LyricCur.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, d));
        if (_s.LyricsInPill && _mode == Mode.Compact)
        {
            UpdateCompact();
            CompactCenterShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, d) { EasingFunction = ease });
            CompactCenter.BeginAnimation(OpacityProperty, new DoubleAnimation(0.1, 1, d));
        }
    }

    void Play_Click(object s, RoutedEventArgs e) { _media.TogglePlay(); e.Handled = true; }
    void Next_Click(object s, RoutedEventArgs e) { _media.Next(); e.Handled = true; }
    void Prev_Click(object s, RoutedEventArgs e) { _media.Previous(); e.Handled = true; }

    void ProgressTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var m = _media.Current;
        if (m != null && m.Duration > TimeSpan.Zero && ProgressTrack.ActualWidth > 0)
            _media.Seek(m.Duration * Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1));
        e.Handled = true;
    }

    // ───────────────────────── compact pill ─────────────────────────

    void UpdateCompact()
    {
        var m = _media.Current;
        bool playing = m is { IsPlaying: true } && _trackKey != null;
        bool lowBat = _power.HasBattery && !_power.OnAC && _power.Percent is >= 0 and <= 20;

        CompactClock.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        CompactArt.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        if (playing)
        {
            if (m!.Art != null) { CompactArt.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill }; CompactArtGlyph.Text = ""; }
            else { CompactArt.Background = (Brush)Resources["Card"]; CompactArtGlyph.Text = ""; }
        }

        string center = "";
        Brush centerBrush = Brushes.White;
        if (_timerEnd is DateTime end)
        {
            center = "⏱  " + Fmt(end - DateTime.Now);
            centerBrush = (Brush)Resources["Accent"];
        }
        else if (playing)
        {
            center = (_s.LyricsInPill ? CurrentLyricLine() : null) ?? m!.Title;
        }
        CompactCenter.Text = center;
        CompactCenter.Foreground = centerBrush;
        CompactCenter.Visibility = center.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        CompactCat.Visibility = center.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CompactCat.Mood = _hover ? FaceMood.Awake : FaceMood.Sleep;

        VizPanel.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = playing && !lowBat ? Visibility.Collapsed : Visibility.Visible;

        if (_mode != Mode.Compact) return;
        CompactView.Measure(new Size(double.PositiveInfinity, CompactH));
        var w = Math.Clamp(CompactView.DesiredSize.Width + 26, MinCompactW, MaxCompactW);
        if (center.Length == 0) w = MinCompactW;
        else if (Math.Abs(_th - CompactH) < 0.5 && w < _tw && _tw - w < 40) w = _tw; // don't 'breathe' on every lyric line
        AnimateTo(Math.Round(w), CompactH);
    }

    void VizTick()
    {
        if (VizPanel.Visibility != Visibility.Visible || _mode != Mode.Compact) return;
        double peak = Math.Sqrt(_audio.Peak); // perceptual curve
        double t = Environment.TickCount64 / 1000.0;
        for (int i = 0; i < _bars.Length; i++)
        {
            double wobble = 0.55 + 0.45 * Math.Abs(Math.Sin(t * (3.1 + i * 1.7) + i * 1.3));
            double target = 3.5 + peak * 14.5 * wobble;
            _barH[i] += (target - _barH[i]) * (target > _barH[i] ? 0.6 : 0.25);
            _bars[i].Height = _barH[i];
        }
    }

    // ───────────────────────── expanded panels ─────────────────────────

    void Tab_Click(object sender, RoutedEventArgs e)
    {
        SelectTab((string)((Button)sender).Tag);
        e.Handled = true;
    }

    void SelectTab(string tab)
    {
        _tab = tab;
        MediaPanel.Visibility = tab == "media" ? Visibility.Visible : Visibility.Collapsed;
        SystemPanel.Visibility = tab == "system" ? Visibility.Visible : Visibility.Collapsed;
        BrainPanel.Visibility = tab == "brain" ? Visibility.Visible : Visibility.Collapsed;
        TimerPanel.Visibility = tab == "timer" ? Visibility.Visible : Visibility.Collapsed;
        SoundPanel.Visibility = tab == "sound" ? Visibility.Visible : Visibility.Collapsed;
        AlertsPanel.Visibility = tab == "alerts" ? Visibility.Visible : Visibility.Collapsed;
        ShelfPanel.Visibility = tab == "shelf" ? Visibility.Visible : Visibility.Collapsed;
        HomeScroll.Visibility = tab == "home" ? Visibility.Visible : Visibility.Collapsed;
        AppsPanel.Visibility = tab == "apps" ? Visibility.Visible : Visibility.Collapsed;
        if (tab == "apps") RefreshAppsPage(); else ClosePreview();
        BackBtn.Visibility = tab == "home" ? Visibility.Collapsed : Visibility.Visible;
        HdrCat.Visibility = tab == "home" ? Visibility.Visible : Visibility.Collapsed;
        HdrTitle.Text = TitleFor(tab);
        AnimatePages();
        RefreshExpanded();
        if (tab == "brain") { _ = RefreshBrainAsync(); _ = CheckMonitorsAsync(force: true); }
        if (tab == "sound") RefreshSound(full: true);
        if (tab == "alerts") RefreshAlerts();
        if (tab == "shelf") RefreshShelf();
        UpdateTimers();
    }

    void RefreshExpanded()
    {
        HdrClock.Text = DateTime.Now.ToString("HH:mm");
        switch (_tab)
        {
            case "home": UpdateHome(); break;
            case "media": RefreshMedia(); break;
            case "system": RefreshSystem(); break;
            case "timer": RefreshTimer(); break;
            case "sound": RefreshSound(full: false); break;
        }
    }

    void RefreshSystem()
    {
        Meter(CpuScale, _stats.Cpu / 100.0);
        CpuText.Text = $"{_stats.Cpu:0}%";
        if (_stats.Gpu >= 0) { Meter(GpuScale, _stats.Gpu / 100.0); GpuText.Text = $"{_stats.Gpu:0}%"; }
        else { Meter(GpuScale, 0); GpuText.Text = "n/a"; }
        Meter(RamScale, _stats.RamPct / 100.0);
        RamText.Text = $"{_stats.RamUsedGb:0.0} / {_stats.RamTotalGb:0} GB";
        NetText.Text = $"↓ {Rate(_stats.DownBps)}   ↑ {Rate(_stats.UpBps)}";
        WifiText.Text = _power.Wifi ? $"{_power.Ssid}  ·  {_power.Bars}/4" : _power.Wired ? "Ethernet" : "Not connected";
        BatDetail.Text = !_power.HasBattery ? "AC power"
            : _power.Charging ? $"{_power.Percent}%  ·  charging"
            : _power.OnAC ? $"{_power.Percent}%  ·  plugged in"
            : _power.Remaining is TimeSpan r ? $"{_power.Percent}%  ·  {(int)r.TotalHours}h {r.Minutes:00}m left" : $"{_power.Percent}%";
    }

    static void Meter(ScaleTransform st, double v) =>
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(Math.Clamp(v, 0, 1), TimeSpan.FromMilliseconds(450))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    async Task RefreshBrainAsync()
    {
        if (_brainBusy) return;
        _brainBusy = true;
        try
        {
            var path = _s.HermesQueuePath;
            var q = await Task.Run(() => BrainboxService.ReadQueue(path));
            if (q == null)
            {
                QueueChips.Visibility = Visibility.Collapsed;
                QueueMsg.Visibility = Visibility.Visible;
                QueueMsg.Text = $"Queue folder not found: {path}";
            }
            else
            {
                QueueChips.Visibility = Visibility.Visible;
                QueueMsg.Visibility = Visibility.Collapsed;
                QPending.Text = q.Pending.ToString(); QRunning.Text = q.Running.ToString();
                QDone.Text = q.Done.ToString(); QFailed.Text = q.Failed.ToString();
            }

            var (state, ccRows) = await _brain.FetchAsync(_s.BrainboxStatusUrl, _s.BrainboxToken);
            var rows = MonitorRows().Concat(ccRows).ToList();
            CcRows.ItemsSource = rows;
            CcMsg.Text = state switch
            {
                "ok" => ccRows.Count == 0 ? "Command Center connected — endpoint returned no fields" : "",
                "not-configured" => "Command Center not connected — set brainboxStatusUrl in settings.json (tray menu).",
                _ => $"Unavailable ({state}) — {_s.BrainboxStatusUrl}",
            };
            CcMsg.Visibility = CcMsg.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _brainBusy = false; }
    }

    void SetupQueueWatcher()
    {
        _queueWatcher?.Dispose(); _queueWatcher = null;
        if (!_s.HermesEnabled) return;
        try
        {
            if (!Directory.Exists(_s.HermesQueuePath)) return;
            _queueWatcher = new FileSystemWatcher(_s.HermesQueuePath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler h = (_, e) => Dispatcher.InvokeAsync(() => OnQueueFile(e.FullPath));
            _queueWatcher.Created += h;
            _queueWatcher.Renamed += (_, e) => Dispatcher.InvokeAsync(() => OnQueueFile(e.FullPath));
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void OnQueueFile(string full)
    {
        var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(full) ?? "")?.ToLowerInvariant();
        var name = System.IO.Path.GetFileNameWithoutExtension(full);
        if (folder == "done") ShowToast(new ToastSpec("Hermes finished a task", name, Glyph: "", Seconds: 4.5));
        else if (folder == "failed") ShowToast(new ToastSpec("Hermes task failed", name, Glyph: "", GlyphBrush: Red, Seconds: 6));
        else if (folder == "in_progress") ShowToast(new ToastSpec("Hermes started", name, Glyph: "", GlyphBrush: Yellow, Seconds: 2.5, Key: "hermes-start"));
        _ = RefreshBrainAsync();
    }

    // ───────────────────────── timer ─────────────────────────

    void TimerPreset_Click(object sender, RoutedEventArgs e)
    {
        var min = int.Parse((string)((Button)sender).Tag);
        _timerTotal = TimeSpan.FromMinutes(min);
        _timerEnd = DateTime.Now + _timerTotal;
        RefreshTimer(); UpdateCompact(); UpdateTimers();
        e.Handled = true;
    }

    void TimerStop_Click(object sender, RoutedEventArgs e)
    {
        _timerEnd = null; RefreshTimer(); UpdateCompact();
        e.Handled = true;
    }

    void RefreshTimer()
    {
        if (_timerEnd is DateTime end)
        {
            var rem = end - DateTime.Now;
            TimerBig.Text = Fmt(rem < TimeSpan.Zero ? TimeSpan.Zero : rem);
            TimerScale.ScaleX = Math.Clamp(1 - rem / _timerTotal, 0, 1);
            TimerStop.Visibility = Visibility.Visible;
        }
        else
        {
            TimerBig.Text = "00:00";
            TimerScale.ScaleX = 0;
            TimerStop.Visibility = Visibility.Collapsed;
        }
    }

    void TickTimer()
    {
        if (_timerEnd is not DateTime end) return;
        if (DateTime.Now >= end)
        {
            _timerEnd = null;
            SystemSounds.Exclamation.Play();
            TimerFinished?.Invoke();
            ShowToast(new ToastSpec("Time's up", $"{_timerTotal.TotalMinutes:0}-minute timer finished", Glyph: "", Seconds: 8));
            RefreshTimer(); UpdateCompact();
        }
    }

    // ───────────────────────── volume OSD ─────────────────────────

    void OnVolume(float v, bool muted)
    {
        if (_mode == Mode.Expanded && _tab == "sound") { SyncMaster(); return; }
        if (!_s.ShowVolumeOsd || DateTime.Now < _ignoreVolumeUntil || _mode == Mode.Expanded) return;
        ShowToast(new ToastSpec(muted ? "Muted" : "Volume", null, null, muted ? "" : "",
            Brushes.White, muted ? 0 : v, muted ? "" : $"{Math.Round(v * 100)}", 1.6, "vol"));
    }

    // ───────────────────────── ticks ─────────────────────────

    void FastTick()
    {
        TickLyrics();
        TickTimer();
        if (_mode == Mode.Expanded && _tab == "media") UpdateProgress();
    }

    async Task SlowTick()
    {
        _tick++;
        CompactClock.Text = DateTime.Now.ToString("HH:mm");

        var events = await Task.Run(_power.Poll);
        UpdateStatusIcons();
        foreach (var ev in events) PowerToast(ev);

        if (_tick % 2 == 0) { KeepOnTop(); _media.Reevaluate(); }
        PollPrivacy();
        if (_s.Notifications && (!_notes.EventsOk || _tick % 15 == 0)) _ = _notes.PollAsync(_s.NotificationsSuppressNative);
        _ = CheckMonitorsAsync(force: false);
        if (_tick % 5 == 0) _audio.EnsureDevice();
        if (_s.HermesEnabled && _tick % 15 == 0 && (_tab == "brain" || !string.IsNullOrWhiteSpace(_s.BrainboxStatusUrl))) _ = RefreshBrainAsync();

        if (_s.HideInFullscreen)
        {
            Native.SHQueryUserNotificationState(out int st);
            bool fs = st is 2 or 3 or 4; // busy / D3D fullscreen / presentation
            if (fs != _fsHidden) { _fsHidden = fs; ApplyVisibility(); }
        }
        else if (_fsHidden) { _fsHidden = false; ApplyVisibility(); }

        if (_mode == Mode.Expanded) RefreshExpanded();
        if (_mode == Mode.Compact && _timerEnd != null) UpdateCompact();
        UpdateTimers();
    }

    void UpdateStatusIcons()
    {
        var p = _power;
        BatteryPanel.Visibility = p.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (p.HasBattery && p.Percent >= 0)
        {
            BatText.Text = p.Percent.ToString();
            BatFill.Width = Math.Max(1.5, 17 * p.Percent / 100.0);
            BatFill.Background = p.Charging || p.OnAC ? (Brush)Resources["Accent"] : p.Percent <= 20 ? Red : Brushes.White;
            BatBolt.Visibility = p.Charging ? Visibility.Visible : Visibility.Collapsed;
        }
        int bars = p.Wifi ? Math.Max(1, p.Bars) : 0;
        for (int i = 0; i < _wifi.Length; i++)
        {
            _wifi[i].Opacity = p.Wifi ? (i < bars ? 1 : 0.25) : 0.18;
            _wifi[i].Fill = p.Online || !p.Wifi ? Brushes.White : Yellow;
        }
        WifiPanel.Visibility = p.Wired && !p.Wifi ? Visibility.Collapsed : Visibility.Visible;
    }

    void PowerToast(string ev)
    {
        var p = _power;
        PowerEvent?.Invoke(ev, p.Percent);
        switch (ev)
        {
            case "plug": ShowToast(new ToastSpec("Charging", null, null, "", null, p.Percent / 100.0, $"{p.Percent}%", 3, "power")); break;
            case "unplug": ShowToast(new ToastSpec("On battery", p.Remaining is TimeSpan r ? $"{(int)r.TotalHours}h {r.Minutes:00}m remaining" : null, null, "", Brushes.White, p.Percent / 100.0, $"{p.Percent}%", 3, "power")); break;
            case "low20": ShowToast(new ToastSpec("Battery low", "Plug in soon", null, "", Yellow, p.Percent / 100.0, $"{p.Percent}%", 6)); break;
            case "low10": ShowToast(new ToastSpec("Battery critically low", "Plug in now", null, "", Red, p.Percent / 100.0, $"{p.Percent}%", 8)); break;
            case "full": ShowToast(new ToastSpec("Fully charged", null, null, "", null, 1, "100%", 3)); break;
            case "wifi-on": ShowToast(new ToastSpec("Wi-Fi connected", p.Ssid, null, "", null, null, null, 2.5, "net")); break;
            case "wifi-off": ShowToast(new ToastSpec("Offline", "No internet connection", null, "", Red, null, null, 3, "net")); break;
        }
    }

    // ───────────────────────── sound & display (ported from PILLAR) ─────────────────────────

    readonly Dictionary<string, (Slider Slider, Button Mute)> _appCtl = new();
    string _deviceSig = "";
    DateTime _brightTouched = DateTime.MinValue, _lastBrightSync = DateTime.MinValue;

    void RefreshSound(bool full)
    {
        SyncMaster();
        if (full || DateTime.Now - _lastBrightSync > TimeSpan.FromSeconds(2)) _ = SyncBrightnessAsync();
        if (full || _tick % 2 == 0) { RefreshDevices(); RefreshAppMixer(); }
    }

    void SyncMaster()
    {
        if (!MasterSlider.IsMouseCaptureWithin)
        {
            _syncing = true;
            MasterSlider.Value = Math.Round(_audio.MasterVolume * 100);
            _syncing = false;
        }
        MasterText.Text = $"{MasterSlider.Value:0}";
        MasterMuteBtn.Content = _audio.MasterMuted ? "" : "";
    }

    void MasterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !IsLoaded) return;
        _audio.MasterVolume = (float)(e.NewValue / 100);
        MasterText.Text = $"{e.NewValue:0}";
    }

    void MasterMute_Click(object sender, RoutedEventArgs e)
    {
        _audio.MasterMuted = !_audio.MasterMuted;
        SyncMaster();
        e.Handled = true;
    }

    void BrightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !IsLoaded) return;
        _brightTouched = DateTime.Now;
        _bright.SetAsync((int)Math.Round(e.NewValue));
        BrightText.Text = $"{e.NewValue:0}";
    }

    async Task SyncBrightnessAsync()
    {
        _lastBrightSync = DateTime.Now;
        var b = await Task.Run(_bright.Get);
        if (DateTime.Now - _brightTouched < TimeSpan.FromSeconds(1.5)) return;
        if (b is int v)
        {
            BrightSlider.IsEnabled = true;
            if (!BrightSlider.IsMouseCaptureWithin)
            {
                _syncing = true; BrightSlider.Value = Math.Max(5, v); _syncing = false;
                BrightText.Text = $"{v}";
            }
        }
        else { BrightSlider.IsEnabled = false; BrightText.Text = "n/a"; }
    }

    void OnBrightness(int lvl)
    {
        if (_mode == Mode.Expanded && _tab == "sound") { _ = SyncBrightnessAsync(); return; }
        if (!_s.ShowBrightnessOsd || _mode == Mode.Expanded || DateTime.Now - _brightTouched < TimeSpan.FromSeconds(1.5)) return;
        ShowToast(new ToastSpec("Brightness", null, null, "", Brushes.White, lvl / 100.0, $"{lvl}", 1.6, "bright"));
    }

    void RefreshDevices()
    {
        var devs = _audio.Devices();
        var sig = string.Join("|", devs.Select(d => d.Id + d.IsDefault));
        if (sig == _deviceSig) return;
        _deviceSig = sig;
        DeviceChips.Children.Clear();
        foreach (var d in devs)
        {
            var lower = d.Name.ToLowerInvariant();
            bool phones = lower.Contains("headphone") || lower.Contains("headset") || lower.Contains("buds") || lower.Contains("airpods") || lower.Contains("hands-free");
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { FontFamily = (FontFamily)Resources["Icons"], Text = phones ? "" : "", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock { Text = d.Name.Length > 26 ? d.Name[..26] + "…" : d.Name, VerticalAlignment = VerticalAlignment.Center });
            var btn = new Button
            {
                Style = (Style)Resources["Chip"],
                Content = content,
                Background = d.IsDefault ? new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E)) : (Brush)Resources["Card"],
                Foreground = d.IsDefault ? (Brush)Resources["Accent"] : new SolidColorBrush(Color.FromRgb(0xC7, 0xC7, 0xCC)),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = d.Name,
            };
            var id = d.Id; var name = d.Name;
            btn.Click += (_, e) =>
            {
                e.Handled = true;
                if (_audio.SetDefaultDevice(id))
                {
                    _deviceSig = ""; _appKeys = "";
                    RefreshSound(full: true);
                }
                else ShowToast(new ToastSpec("Couldn't switch output", name, Glyph: "", GlyphBrush: Red, Seconds: 3));
            };
            DeviceChips.Children.Add(btn);
        }
    }

    void RefreshAppMixer()
    {
        _apps = _audio.RefreshApps().ToList();
        NoApps.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var keys = string.Join("|", _apps.Select(a => a.Key));
        if (keys != _appKeys)
        {
            _appKeys = keys;
            AppRows.Children.Clear();
            _appCtl.Clear();
            foreach (var a in _apps) AppRows.Children.Add(BuildAppRow(a));
            return;
        }
        foreach (var a in _apps)
        {
            if (!_appCtl.TryGetValue(a.Key, out var c)) continue;
            if (!c.Slider.IsMouseCaptureWithin) { _syncing = true; c.Slider.Value = Math.Round(a.Volume * 100); _syncing = false; }
            c.Mute.Content = a.Muted ? "" : "";
        }
    }

    UIElement BuildAppRow(AppAudio a)
    {
        var g = new Grid { Height = 30 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

        UIElement icon = a.Icon != null
            ? new Image { Source = a.Icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left }
            : new TextBlock { FontFamily = (FontFamily)Resources["Icons"], Text = a.Key == "system" ? "" : "", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Resources["Dim"] };
        g.Children.Add(icon);

        var name = new TextBlock { Text = a.Name, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xC7, 0xCC)), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(name, 1); g.Children.Add(name);

        var key = a.Key;
        var sl = new Slider { Style = (Style)Resources["PillSlider"] };
        _syncing = true; sl.Value = Math.Round(a.Volume * 100); _syncing = false;
        sl.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            var cur = _apps.FirstOrDefault(x => x.Key == key);
            if (cur != null) _audio.SetAppVolume(cur, (float)(e.NewValue / 100));
        };
        Grid.SetColumn(sl, 2); g.Children.Add(sl);

        var mute = new Button { Style = (Style)Resources["TinyBtn"], Content = a.Muted ? "" : "", HorizontalAlignment = HorizontalAlignment.Right };
        mute.Click += (_, e) =>
        {
            e.Handled = true;
            var cur = _apps.FirstOrDefault(x => x.Key == key);
            if (cur == null) return;
            _audio.SetAppMute(cur, !cur.Muted);
            mute.Content = cur.Muted ? "" : "";
        };
        Grid.SetColumn(mute, 3); g.Children.Add(mute);

        _appCtl[key] = (sl, mute);
        return g;
    }

    // ───────────────────────── notifications (ported from PILLAR) ─────────────────────────

    void OnNotification(NotificationItem n)
    {
        if (IsPhoneLinkDuplicate(n)) return; // already shown via Cat Bridge
        ForwardIfAway(n);
        NotificationShown?.Invoke(n);
        if (_mode == Mode.Expanded && _tab == "alerts") RefreshAlerts();
        if (Present(n, false, n.App)) return; // shown as a Mac-style banner
        var right = n.Title == n.App ? "" : (n.App.Length > 14 ? n.App[..14] : n.App);
        ShowToast(new ToastSpec(n.Title, string.IsNullOrEmpty(n.Body) ? null : n.Body, n.Logo, "", null, null, right, 5,
            OnClick: () => NotificationService.OpenApp(n)));
    }

    void RefreshAlerts()
    {
        bool ok = _s.Notifications && _notes.Status == "allowed";
        AlertsMsgPanel.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        AlertsClear.Visibility = ok && _notes.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlertRows.Children.Clear();
        if (!ok)
        {
            AlertsCount.Text = "NOTIFICATIONS";
            AlertsSettingsBtn.Visibility = _notes.Status == "denied" ? Visibility.Visible : Visibility.Collapsed;
            AlertsMsg.Text = !_s.Notifications ? "Notifications are turned off in settings.json."
                : _notes.Status == "denied" ? "Windows hasn't given Dynamic Island access to your notifications. Turn on notification access for apps, then restart the island."
                : "This Windows build doesn't let desktop apps read notifications.";
            return;
        }
        AlertsCount.Text = $"NOTIFICATIONS · {_notes.History.Count}";
        if (_notes.History.Count == 0)
        {
            AlertRows.Children.Add(new TextBlock { Text = "You're all caught up", FontSize = 12.5, Foreground = (Brush)Resources["Dim"], HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) });
            return;
        }
        foreach (var n in _notes.History.Take(30)) AlertRows.Children.Add(BuildAlertRow(n));
    }

    UIElement BuildAlertRow(NotificationItem n)
    {
        var card = new Border { Background = (Brush)Resources["Card"], CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 7, 4, 7), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (n.Logo != null) g.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = new ImageBrush(n.Logo), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left });
        else g.Children.Add(new TextBlock { FontFamily = (FontFamily)Resources["Icons"], Text = "", FontSize = 15, Foreground = (Brush)Resources["Accent"], VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 4, 0, 0) });

        var st = new StackPanel();
        st.Children.Add(new TextBlock { Text = $"{n.App}  ·  {Ago(n.Time)}", FontSize = 10.5, Foreground = (Brush)Resources["Dim"], TextTrimming = TextTrimming.CharacterEllipsis });
        st.Children.Add(new TextBlock { Text = n.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(n.Body))
            st.Children.Add(new TextBlock { Text = n.Body, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xAE, 0xAE, 0xB2)), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(st, 1); g.Children.Add(st);

        var x = new Button { Style = (Style)Resources["TinyBtn"], Content = "", FontSize = 9, VerticalAlignment = VerticalAlignment.Top };
        x.Click += (_, e) => { e.Handled = true; _notes.Dismiss(n); RefreshAlerts(); };
        Grid.SetColumn(x, 2); g.Children.Add(x);

        card.Child = g;
        card.MouseLeftButtonUp += (_, e) => { if (e.Handled) return; e.Handled = true; NotificationService.OpenApp(n); };
        return card;
    }

    void AlertsClear_Click(object sender, RoutedEventArgs e) { _notes.ClearHistory(); RefreshAlerts(); e.Handled = true; }
    void AlertsSettings_Click(object sender, RoutedEventArgs e) { NotificationService.OpenSettings(); e.Handled = true; }

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes}m" : d.TotalDays < 1 ? $"{(int)d.TotalHours}h" : t.ToString("MMM d");
    }

    // ───────────────────────── privacy dots ─────────────────────────

    bool _camOn, _micOn;

    void PollPrivacy()
    {
        if (!_s.PrivacyDots) { PrivacyDots.Visibility = HdrCam.Visibility = HdrMic.Visibility = Visibility.Collapsed; return; }
        var (camStarted, micStarted) = _privacy.Poll();
        bool cam = _privacy.Camera.Count > 0, mic = _privacy.Mic.Count > 0;
        CamDot.Visibility = HdrCam.Visibility = cam ? Visibility.Visible : Visibility.Collapsed;
        MicDot.Visibility = HdrMic.Visibility = mic ? Visibility.Visible : Visibility.Collapsed;
        PrivacyDots.Visibility = cam || mic ? Visibility.Visible : Visibility.Collapsed;
        HdrCam.ToolTip = cam ? "Camera: " + string.Join(", ", _privacy.Camera) : null;
        HdrMic.ToolTip = mic ? "Microphone: " + string.Join(", ", _privacy.Mic) : null;
        if (cam != _camOn || mic != _micOn) { _camOn = cam; _micOn = mic; UpdateCompact(); }
        if (camStarted) ShowToast(new ToastSpec("Camera in use", string.Join(", ", _privacy.Camera), Glyph: "", GlyphBrush: CamGreen, Seconds: 3.5));
        if (micStarted) ShowToast(new ToastSpec("Microphone in use", string.Join(", ", _privacy.Mic), Glyph: "", GlyphBrush: MicOrange, Seconds: 3.5));
    }

    static readonly Brush CamGreen = Frozen(Color.FromRgb(0x30, 0xD1, 0x58));
    static readonly Brush MicOrange = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A));

    // ───────────────────────── VPS / tunnel monitors ─────────────────────────

    async Task CheckMonitorsAsync(bool force)
    {
        if (_monitorBusy || _s.Monitors.Count == 0) return;
        if (!force && (DateTime.Now - _lastMonitorCheck).TotalSeconds < Math.Max(10, _s.MonitorIntervalSeconds)) return;
        _monitorBusy = true;
        _lastMonitorCheck = DateTime.Now;
        try
        {
            var changes = await _monitors.CheckAsync(_s.Monitors);
            foreach (var (name, up) in changes)
            {
                PushServerChange(name, up);
                ShowToast(up
                    ? new ToastSpec($"{name} is back", "Reachable again", Glyph: "", Seconds: 3)
                    : new ToastSpec($"{name} is down", "Not reachable", Glyph: "", GlyphBrush: Red, Seconds: 7));
            }
            if (_mode == Mode.Expanded && _tab == "brain") await RefreshBrainAsync();
        }
        catch (Exception ex) { App.Log(ex); }
        finally { _monitorBusy = false; }
    }

    IEnumerable<StatusRow> MonitorRows() =>
        _monitors.Results.Select(r => new StatusRow(r.Name, r.Up ? $"{r.Ms} ms" : "down", r.Up ? (Brush)Resources["Accent"] : Red));

    // ───────────────────────── Hermes quick prompt ─────────────────────────

    public void OpenPrompt()
    {
        if (_hidden) ToggleHidden();
        if (_tab != "brain") SelectTab("brain");
        SetKeyboard(true);
        Reconcile();
        PromptBox.Focus();
        Keyboard.Focus(PromptBox);
        PromptBox.CaretIndex = PromptBox.Text.Length;
    }

    /// The island normally never takes focus (WS_EX_NOACTIVATE). Typing needs it, so we lift that flag
    /// while the prompt is open, then hand focus back to whatever app you were in.
    void SetKeyboard(bool on, bool restoreFocus = true)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (on == _keyboardOn) { if (on) Activate(); return; }
        _keyboardOn = on;
        _st.SetTyping(on, DateTime.Now);
        var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
        if (on)
        {
            _prevForeground = Native2.GetForegroundWindow();
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex & ~Native.WS_EX_NOACTIVATE));
            Activate();
            Native2.SetForegroundWindow(_hwnd);
        }
        else
        {
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_NOACTIVATE));
            Keyboard.ClearFocus();
            if (restoreFocus && _prevForeground != IntPtr.Zero && _prevForeground != _hwnd) Native2.SetForegroundWindow(_prevForeground);
            _prevForeground = IntPtr.Zero;
        }
    }

    void PromptBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_keyboardOn) return;
        SetKeyboard(true);
        PromptBox.Focus();
    }

    void PromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SubmitPrompt(); e.Handled = true; }
        else if (e.Key == Key.Escape) { ClosePrompt(); e.Handled = true; }
    }

    void PromptSend_Click(object sender, RoutedEventArgs e) { SubmitPrompt(); e.Handled = true; }

    void ClosePrompt()
    {
        SetKeyboard(false);
        Reconcile();
    }

    void SubmitPrompt()
    {
        var text = PromptBox.Text.Trim();
        if (text.Length == 0) return;
        var root = _s.HermesQueuePath;
        try
        {
            if (!Directory.Exists(root))
            {
                ShowToast(new ToastSpec("Hermes queue not found", root, Glyph: "", GlyphBrush: Red, Seconds: 5));
                return;
            }
            var pending = System.IO.Path.Combine(root, "pending");
            Directory.CreateDirectory(pending);
            var id = $"island-{DateTime.Now:yyyyMMdd-HHmmss}-{Random.Shared.Next(1000, 9999)}";
            var task = new { id, created = DateTimeOffset.Now.ToString("o"), source = "dynamic-island", status = "pending", instruction = text };
            var tmp = System.IO.Path.Combine(root, id + ".tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(task, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, System.IO.Path.Combine(pending, id + ".json")); // atomic hand-off: Hermes never sees a half-written file
            PromptBox.Text = "";
            ClosePrompt();
            ShowToast(new ToastSpec("Queued for Hermes", text, Glyph: "", Seconds: 3));
            _ = RefreshBrainAsync();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast(new ToastSpec("Couldn't queue task", ex.Message, Glyph: "", GlyphBrush: Red, Seconds: 5));
        }
    }

    // ───────────────────────── shelf + clipboard ─────────────────────────

    Point? _tileDown;

    void RefreshShelf()
    {
        _shelf.Prune();
        ShelfTiles.Children.Clear();
        ShelfEmpty.Visibility = _shelf.Paths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var p in _shelf.Paths) ShelfTiles.Children.Add(BuildTile(p));

        ClipRows.Children.Clear();
        if (!_s.ClipboardHistory)
            ClipRows.Children.Add(new TextBlock { Text = "Clipboard history is off in settings.json", FontSize = 12, Foreground = (Brush)Resources["Dim"] });
        else if (_clip.Items.Count == 0)
            ClipRows.Children.Add(new TextBlock { Text = "Copy some text and it shows up here (kept in memory only)", FontSize = 12, Foreground = (Brush)Resources["Dim"] });
        foreach (var t in _clip.Items)
        {
            var oneLine = t.Replace("\r", "").Replace("\n", "  ⏎  ").Trim();
            var row = new Border
            {
                Background = (Brush)Resources["Card"], CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, Tag = "drag", ToolTip = t.Length > 400 ? t[..400] + "…" : t,
                Child = new TextBlock { Text = oneLine.Length > 160 ? oneLine[..160] : oneLine, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            var text = t;
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; PasteClip(text, row); };
            ClipRows.Children.Add(row);
        }
    }

    void PasteClip(string text, Border row)
    {
        try { Clipboard.SetText(text); } catch { return; }
        row.Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E));
        if (_s.ClipboardAutoPaste && Native2.GetForegroundWindow() != _hwnd)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            t.Tick += (_, _) => { t.Stop(); Native2.SendCtrlV(); };
            t.Start();
        }
    }

    UIElement BuildTile(string path)
    {
        bool isDir = Directory.Exists(path);
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        var tile = new Border { Width = 76, Height = 80, Margin = new Thickness(2, 6, 2, 6), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Tag = "drag", ToolTip = path, Cursor = Cursors.Hand };
        var g = new Grid();
        var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var icon = isDir ? null : ShelfStore.IconFor(path);
        if (icon != null) st.Children.Add(new Image { Source = icon, Width = 32, Height = 32 });
        else st.Children.Add(new TextBlock { FontFamily = (FontFamily)Resources["Icons"], Text = isDir ? "" : "", FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Resources["Accent"] });
        st.Children.Add(new TextBlock { Text = name, FontSize = 10.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 68, Margin = new Thickness(0, 5, 0, 0) });
        g.Children.Add(st);
        var x = new Button { Style = (Style)Resources["TinyBtn"], Content = "", FontSize = 8, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Opacity = 0 };
        x.Click += (_, e) => { e.Handled = true; _shelf.Remove(path); RefreshShelf(); };
        g.Children.Add(x);
        tile.Child = g;

        tile.MouseEnter += (_, _) => { tile.Background = (Brush)Resources["Card"]; x.Opacity = 1; };
        tile.MouseLeave += (_, _) => { tile.Background = Brushes.Transparent; x.Opacity = 0; };
        tile.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
                e.Handled = true; return;
            }
            _tileDown = e.GetPosition(tile);
        };
        tile.MouseMove += (_, e) =>
        {
            if (_tileDown is not Point p0 || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(tile);
            if (Math.Abs(p.X - p0.X) < 6 && Math.Abs(p.Y - p0.Y) < 6) return;
            _tileDown = null;
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            _st.SetDragging(true, DateTime.Now);
            try { DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            finally { _st.SetDragging(false, DateTime.Now); }
            RefreshShelf();
            Reconcile();
        };
        tile.MouseLeftButtonUp += (_, e) => { _tileDown = null; e.Handled = true; };
        return tile;
    }

    void ShelfClear_Click(object sender, RoutedEventArgs e) { _shelf.Clear(); RefreshShelf(); e.Handled = true; }

    void Island_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        _dragLeave.Stop();
        if (!_st.Dragging)
        {
            _st.SetDragging(true, DateTime.Now);
            if (_tab != "shelf") SelectTab("shelf");
            Reconcile();
        }
        ShelfDrop.BorderBrush = (Brush)Resources["Accent"];
    }

    void Island_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Island_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; wait briefly so DragEnter can cancel it.
        _dragLeave.Stop(); _dragLeave.Start();
    }

    void Island_Drop(object sender, DragEventArgs e)
    {
        _dragLeave.Stop();
        _st.SetDragging(false, DateTime.Now);
        _st.OpenSoft(DateTime.Now);
        ShelfDrop.BorderBrush = ShelfIdleBorder;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            _shelf.Add(paths);
            RefreshShelf();
        }
        e.Handled = true;
        Reconcile();
    }

    static readonly Brush ShelfIdleBorder = Frozen(Color.FromRgb(0x2C, 0x2C, 0x2E));

    // ───────────────────────── gestures (ported from PILLAR) ─────────────────────────

    void Island_PreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (!_s.Gestures || IsInteractive(e.OriginalSource as DependencyObject, includeScroll: false)) return;
        _downPt = e.GetPosition(Island);
        _gestureUsed = false;
        _longPress.Stop(); _longPress.Start();
    }

    void Island_PreviewMove(object sender, MouseEventArgs e)
    {
        if (_downPt is not Point p0) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _downPt = null; _longPress.Stop(); return; }
        var p = e.GetPosition(Island);
        double dx = p.X - p0.X, dy = p.Y - p0.Y;
        if (Math.Abs(dx) > 8 || Math.Abs(dy) > 8) _longPress.Stop();
        if (Math.Abs(dx) < 48 || Math.Abs(dx) < Math.Abs(dy) * 1.5) return;
        _downPt = null;
        _gestureUsed = true;
        if (_media.Current == null) return;
        if (dx > 0) _media.Next(); else _media.Previous();
        if (_mode != Mode.Expanded)
            ShowToast(new ToastSpec(dx > 0 ? "Next track" : "Previous track", null, null, dx > 0 ? "" : "", Brushes.White, Seconds: 0.9, Key: "swipe"));
    }

    void Island_PreviewUp(object sender, MouseButtonEventArgs e)
    {
        _longPress.Stop();
        _downPt = null;
        if (_gestureUsed) { _gestureUsed = false; e.Handled = true; }
    }

    void Island_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_s.Gestures || IsInteractive(e.OriginalSource as DependencyObject, includeScroll: true)) return;
        _audio.MasterVolume += e.Delta / 120f * 0.02f;
        if (_mode == Mode.Expanded && _tab == "sound") SyncMaster();
        e.Handled = true;
    }

    void Island_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_s.Gestures && e.ChangedButton == MouseButton.Middle) { _media.TogglePlay(); e.Handled = true; }
    }

    bool IsInteractive(DependencyObject? d, bool includeScroll)
    {
        while (d != null && !ReferenceEquals(d, Island))
        {
            if (d is ButtonBase or Slider or TextBoxBase or Thumb or ScrollBar) return true;
            if (includeScroll && d is ScrollViewer) return true;
            if (d is FrameworkElement { Tag: "drag" }) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    // ───────────────────────── helpers ─────────────────────────

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    static string Rate(double bps) => bps >= 1 << 20 ? $"{bps / (1 << 20):0.0} MB/s" : bps >= 1024 ? $"{bps / 1024:0} KB/s" : $"{bps:0} B/s";
}
