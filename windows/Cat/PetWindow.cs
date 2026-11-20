using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>
/// The desktop cat. Lives on top of the taskbar, wanders, sits and naps.
/// Never steals focus, and clicks pass straight through it unless you rest the cursor on it first.
/// </summary>
public sealed class PetWindow : Window
{
    enum State { Sit, Walk, Sleep, Dangle, Fall, Land }

    // Window layout (DIPs): the cat's 120×100 box sits at (CatX0, CatY0); its ground line is local y = 92.
    const double WinW = 270, WinH = 210, CatX0 = 75, CatY0 = 104, GroundLocal = 92;
    double GroundInWindow => CatY0 + GroundLocal; // 196
    double CatCenterInWindow => CatX0 + 60;

    readonly AppSettings _s;
    readonly PetCanvas _canvas;
    readonly Border _bubble;
    readonly StackPanel _bubbleBody;
    readonly DispatcherTimer _frame = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly DispatcherTimer _bubbleTimer = new();
    IntPtr _hwnd;
    bool _clickThrough = true;

    State _state = State.Sit;
    DateTime _last = DateTime.Now, _stateUntil = DateTime.Now.AddSeconds(4), _lastSecond = DateTime.Now;
    double _x, _air, _vy, _dir = 1, _speed = 60, _targetX, _dwell, _heartTimer;
    bool _dragging, _pressed, _menuOpen, _dragHover, _sleepByIdle, _fsHidden;
    Point _pressAt;
    DateTime _napUntil = DateTime.MinValue, _moodUntil = DateTime.MinValue;
    FaceMood _moodOverride = FaceMood.Awake;
    double _activeSeconds, _idleSleepStartedSec;

    public event Action<string>? Command;          // focus25 | playpause | island | ringphone
    public event Action<string[]>? FilesDropped;

    public PetWindow(AppSettings s)
    {
        _s = s;
        CatFace.CurrentPalette = CatArt.Palette(s.PetColor);
        Title = "Dynamic Island cat";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = WinW; Height = WinH;
        AllowDrop = true;

        _canvas = new PetCanvas { Width = 120, Height = 100, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(CatX0, CatY0, 0, 0) };
        _canvas.Palette = CatArt.Palette(s.PetColor);

        _bubbleBody = new StackPanel();
        _bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xF0)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(11, 8, 11, 8),
            MaxWidth = 250,
            Child = _bubbleBody,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.25, Direction = 270 },
        };
        var tail = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L14,0 L7,8 Z"),
            Fill = _bubble.Background,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -1, 0, 0),
        };
        var bubbleWrap = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, WinH - CatY0 - 8),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
        };
        bubbleWrap.Children.Add(_bubble);
        bubbleWrap.Children.Add(tail);
        _bubbleWrap = bubbleWrap;

        var root = new Grid();
        root.Children.Add(_canvas);
        root.Children.Add(bubbleWrap);
        Content = root;

        _frame.Tick += (_, _) => Frame();
        _bubbleTimer.Tick += (_, _) => { _bubbleTimer.Stop(); HideBubble(); };

        _canvas.MouseLeftButtonDown += Canvas_Down;
        _canvas.MouseMove += Canvas_Move;
        _canvas.MouseLeftButtonUp += Canvas_Up;
        DragEnter += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { _dragHover = true; Mood(FaceMood.Surprised, 2); e.Effects = DragDropEffects.Copy; } else e.Effects = DragDropEffects.None; e.Handled = true; };
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        DragLeave += (_, _) => _dragHover = false;
        Drop += (_, e) =>
        {
            _dragHover = false;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                FilesDropped?.Invoke(files);
                Mood(FaceMood.Happy, 2.5); Hop(260);
                Say(files.Length == 1 ? "Saved to the shelf" : $"Saved {files.Length} files to the shelf", 3);
            }
            e.Handled = true;
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
            ex = (ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | WS_EX_TRANSPARENT) & ~Native.WS_EX_APPWINDOW;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        };
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            _x = wa.Left + wa.Width * 0.72;
            _targetX = _x;
            Place();
            _frame.Start();
        };
    }

    readonly StackPanel _bubbleWrap;

    // ───────── public API ─────────

    public void ApplyPalette(string color)
    {
        _canvas.Palette = CatArt.Palette(color);
        CatFace.CurrentPalette = _canvas.Palette;
    }

    public void Say(string text, double seconds = 4)
    {
        if (_napUntil > DateTime.Now) return; // napping = do not disturb
        _menuOpen = false;
        _bubbleBody.Children.Clear();
        _bubbleBody.Children.Add(BubbleText(text));
        ShowBubble(seconds);
    }

    public void React(string kind)
    {
        switch (kind)
        {
            case "alert": Mood(FaceMood.Surprised, 1.6); Hop(220); break;
            case "happy": Mood(FaceMood.Happy, 2.5); Hop(300); break;
            case "tired": if (_state is State.Sit or State.Walk) Enter(State.Sleep, 25); break;
        }
    }

    // ───────── frame loop ─────────

    void Frame()
    {
        var now = DateTime.Now;
        double dt = Math.Clamp((now - _last).TotalSeconds, 0, 0.1);
        _last = now;
        _canvas.T += dt;

        if ((now - _lastSecond).TotalSeconds >= 1) { _lastSecond = now; EverySecond(); }

        var cur = CursorDip();
        var rect = CatRect();
        bool inside = rect.Contains(cur);

        if (!_dragging)
        {
            _dwell = inside && !_menuOpen ? _dwell + dt : 0;
            bool interactive = _menuOpen || _dragHover || _pressed || _dwell >= 0.25;
            SetClickThrough(!interactive);
            double targetOpacity = inside && !interactive ? 0.35 : 1;
            Opacity += (targetOpacity - Opacity) * Math.Min(1, dt * 12);

            // look toward the cursor when it's near
            double dx = cur.X - (Left + CatCenterInWindow);
            double dist = Math.Sqrt(dx * dx + Math.Pow(cur.Y - (Top + CatY0 + 40), 2));
            double wantLook = dist < 260 ? Math.Clamp(dx / 140, -1, 1) * (_canvas.FacingRight ? 1 : -1) : 0;
            _canvas.LookOverride += (wantLook - _canvas.LookOverride) * Math.Min(1, dt * 6);

            // purr hearts while being "petted" (cursor resting on it)
            if (_dwell >= 0.25 && !_menuOpen && _state != State.Sleep)
            {
                _heartTimer -= dt;
                if (_heartTimer <= 0) { _canvas.SpawnHeart(); _heartTimer = 0.55; }
            }
        }

        Step(now, dt, cur);
        _canvas.Mood = CurrentMood(now);
        _canvas.Advance(dt, _state == State.Walk, _state == State.Sleep);
        Place();
        _canvas.InvalidateVisual();

        _frame.Interval = TimeSpan.FromMilliseconds(_state switch
        {
            State.Walk or State.Dangle or State.Fall or State.Land => 16,
            State.Sleep when !inside => 90,
            _ => 33,
        });
    }

    void Step(DateTime now, double dt, Point cur)
    {
        var wa = SystemParameters.WorkArea;
        switch (_state)
        {
            case State.Walk:
                _x += _dir * _speed * dt;
                if ((_dir > 0 && _x >= _targetX) || (_dir < 0 && _x <= _targetX)) { _x = _targetX; Enter(State.Sit, Random.Shared.Next(2, 6)); }
                break;
            case State.Sit:
                if (now >= _stateUntil && !_menuOpen && _dwell < 0.25) Decide();
                break;
            case State.Sleep:
                if (_napUntil > now) break;
                if (_sleepByIdle) break; // woken in EverySecond when input resumes
                if (now >= _stateUntil) Enter(State.Sit, 3);
                break;
            case State.Dangle:
                Left = cur.X - CatCenterInWindow;
                Top = cur.Y - (CatY0 + 6);
                _x = Left + CatCenterInWindow;
                _air = Math.Max(0, GroundWindowTop() - Top);
                return;
            case State.Fall:
                _vy += 3000 * dt;
                _air -= _vy * dt;
                if (_air <= 0) { _air = 0; _vy = 0; Enter(State.Land, 0.2); }
                break;
            case State.Land:
                if (now >= _stateUntil) Enter(State.Sit, Random.Shared.Next(3, 6));
                break;
        }
        // hops (reactions) while sitting/walking
        if (_state is State.Sit or State.Walk or State.Sleep && (_air > 0 || _vy < 0))
        {
            _vy += 2400 * dt;
            _air = Math.Max(0, _air - _vy * dt);
            if (_air <= 0 && _vy > 0) _vy = 0;
        }
        _x = Math.Clamp(_x, wa.Left + 40, wa.Right - 40);
    }

    void Decide()
    {
        var now = DateTime.Now;
        if (_napUntil > now) { Enter(State.Sleep, 9999); return; }
        double roll = Random.Shared.NextDouble();
        if (roll < 0.58)
        {
            var wa = SystemParameters.WorkArea;
            double dist = Random.Shared.Next(80, 420) * (Random.Shared.Next(2) == 0 ? -1 : 1);
            _targetX = Math.Clamp(_x + dist, wa.Left + 60, wa.Right - 60);
            if (Math.Abs(_targetX - _x) < 30) { Enter(State.Sit, 3); return; }
            _dir = Math.Sign(_targetX - _x);
            _speed = Random.Shared.Next(85, 135);
            _canvas.FacingRight = _dir > 0;
            Enter(State.Walk, 60);
        }
        else if (roll < 0.78) Enter(State.Sleep, Random.Shared.Next(15, 45));
        else Enter(State.Sit, Random.Shared.Next(2, 6));
    }

    void Enter(State st, double seconds)
    {
        _state = st;
        _stateUntil = DateTime.Now.AddSeconds(seconds);
        _canvas.Pose = st switch
        {
            State.Walk => PetPose.Stand,
            State.Sleep => PetPose.Loaf,
            State.Dangle => PetPose.Dangle,
            State.Fall => PetPose.Dangle,
            State.Land => PetPose.Squash,
            _ => PetPose.Sit,
        };
        if (st != State.Sleep) _sleepByIdle = false;
    }

    void EverySecond()
    {
        KeepOnTop();

        // hide during full-screen games / videos / presentations
        Native.SHQueryUserNotificationState(out int qs);
        bool fs = qs is 2 or 3 or 4;
        if (fs != _fsHidden) { _fsHidden = fs; Visibility = fs ? Visibility.Hidden : Visibility.Visible; }

        double idle = UserIdleSeconds();

        // nap when you step away, wake up when you're back
        if (idle > 90 && _state == State.Sit) { Enter(State.Sleep, 99999); _sleepByIdle = true; _idleSleepStartedSec = idle; }
        if (_sleepByIdle && idle < 2)
        {
            double awayMin = _activeAwaySeconds / 60;
            Enter(State.Sit, 4);
            Mood(FaceMood.Happy, 2);
            if (awayMin >= 10) Say($"Welcome back! You were away {Math.Round(awayMin)} min.", 5);
        }
        _activeAwaySeconds = idle;

        // stretch reminder after a long unbroken stretch of work
        if (idle < 60) _activeSeconds += 1;
        else if (idle > 300) _activeSeconds = 0;
        if (_s.BreakReminderMinutes > 0 && _activeSeconds >= _s.BreakReminderMinutes * 60)
        {
            _activeSeconds = 0;
            Mood(FaceMood.Happy, 3); Hop(320);
            Say($"You've been at it for {_s.BreakReminderMinutes} minutes. Time to stretch and grab some water?", 12);
        }
    }
    double _activeAwaySeconds;

    FaceMood CurrentMood(DateTime now)
    {
        if (now < _moodUntil) return _moodOverride;
        return _state switch
        {
            State.Sleep => FaceMood.Sleep,
            State.Dangle or State.Fall => FaceMood.Surprised,
            _ when _dwell >= 0.25 => FaceMood.Content,
            _ => FaceMood.Awake,
        };
    }

    void Mood(FaceMood m, double seconds) { _moodOverride = m; _moodUntil = DateTime.Now.AddSeconds(seconds); }

    void Hop(double v)
    {
        if (_state is State.Dangle or State.Fall) return;
        if (_state == State.Sleep && !_sleepByIdle) Enter(State.Sit, 4);
        _vy = -v; _air = Math.Max(_air, 0.5);
    }

    // ───────── mouse ─────────

    void Canvas_Down(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _pressAt = CursorDip();
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    void Canvas_Move(object sender, MouseEventArgs e)
    {
        if (!_pressed || _dragging) return;
        var p = CursorDip();
        if (Math.Abs(p.X - _pressAt.X) + Math.Abs(p.Y - _pressAt.Y) < 6) return;
        _dragging = true;
        CloseMenu();
        Enter(State.Dangle, 9999);
    }

    void Canvas_Up(object sender, MouseButtonEventArgs e)
    {
        _canvas.ReleaseMouseCapture();
        bool wasDrag = _dragging;
        _pressed = _dragging = false;
        e.Handled = true;
        if (wasDrag)
        {
            _vy = 0;
            if (_air > 2) Enter(State.Fall, 9999); else Enter(State.Land, 0.28);
            return;
        }
        if (_menuOpen) CloseMenu(); else OpenMenu();
    }

    // ───────── bubbles ─────────

    void OpenMenu()
    {
        _menuOpen = true;
        if (_state == State.Sleep) Enter(State.Sit, 6);
        _bubbleBody.Children.Clear();
        _bubbleBody.Children.Add(BubbleText(_napUntil > DateTime.Now ? "Zzz… want me up?" : "Meow? What can I do?"));
        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), MaxWidth = 230 };
        row.Children.Add(BubbleButton("Focus 25 min", () => Command?.Invoke("focus25")));
        row.Children.Add(BubbleButton("Play / pause", () => Command?.Invoke("playpause")));
        row.Children.Add(BubbleButton("Open island", () => Command?.Invoke("island")));
        row.Children.Add(BubbleButton("Find my iPhone", () => Command?.Invoke("ringphone")));
        row.Children.Add(_napUntil > DateTime.Now
            ? BubbleButton("Wake up", () => { _napUntil = DateTime.MinValue; Enter(State.Sit, 3); Mood(FaceMood.Happy, 2); })
            : BubbleButton("Nap 30 min", () => { _napUntil = DateTime.Now.AddMinutes(30); Enter(State.Sleep, 99999); }));
        _bubbleBody.Children.Add(row);
        ShowBubble(8);
    }

    void CloseMenu() { if (_menuOpen) { _menuOpen = false; HideBubble(); } }

    void ShowBubble(double seconds)
    {
        _bubbleWrap.Visibility = Visibility.Visible;
        _bubbleWrap.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        _bubbleTimer.Stop(); _bubbleTimer.Interval = TimeSpan.FromSeconds(seconds); _bubbleTimer.Start();
    }

    void HideBubble()
    {
        _menuOpen = false;
        var a = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        a.Completed += (_, _) => { if (_bubbleWrap.Opacity < 0.05) _bubbleWrap.Visibility = Visibility.Collapsed; };
        _bubbleWrap.BeginAnimation(OpacityProperty, a);
    }

    static readonly Brush InkBrush = Fz(Color.FromRgb(0x4A, 0x34, 0x26));
    static readonly Brush ChipBrush = Fz(Color.FromRgb(0xF3, 0xE4, 0xD3));
    static Brush Fz(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static TextBlock BubbleText(string t) => new()
    {
        Text = t, FontSize = 12.5, Foreground = InkBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 228,
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontWeight = FontWeights.SemiBold,
    };

    Button BubbleButton(string label, Action act)
    {
        var b = new Button
        {
            Content = label, Cursor = Cursors.Hand, Focusable = false, Margin = new Thickness(0, 4, 5, 0),
            Template = ChipTemplate(),
        };
        b.Click += (_, e) => { e.Handled = true; HideBubble(); act(); };
        return b;
    }

    static ControlTemplate? _chipTemplate;
    static ControlTemplate ChipTemplate()
    {
        if (_chipTemplate != null) return _chipTemplate;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, ChipBrush);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.PaddingProperty, new Thickness(9, 4, 9, 4));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(TextElement.FontSizeProperty, 11.5);
        cp.SetValue(TextElement.ForegroundProperty, InkBrush);
        cp.SetValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
        border.AppendChild(cp);
        return _chipTemplate = new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    // ───────── helpers ─────────

    double GroundWindowTop() => SystemParameters.WorkArea.Bottom - GroundInWindow;

    void Place()
    {
        if (_state == State.Dangle) return;
        Left = _x - CatCenterInWindow;
        Top = GroundWindowTop() - _air;
    }

    Rect CatRect() => new(Left + CatX0 + 14, Top + CatY0 + 6, 92, 88);

    Point CursorDip()
    {
        GetCursorPos(out var p);
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY);
    }

    void SetClickThrough(bool on)
    {
        if (_hwnd == IntPtr.Zero || on == _clickThrough) return;
        _clickThrough = on;
        var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
        ex = on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
    }

    void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    const long WS_EX_TRANSPARENT = 0x20;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);

    static double UserIdleSeconds()
    {
        var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref li)) return 0;
        return unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
    }
}

/// <summary>Body pose, all in the cat's local 120×100 box (facing right, ground at y = 92).</summary>
public struct PetPose
{
    public double BodyX, BodyY, BodyRx, BodyRy, HeadX, HeadY, HeadR;
    public double FrontX1, FrontX2, FrontTop, FrontLen, BackX1, BackX2, BackTop, BackLen, LegW;
    public double TailBX, TailBY, TailCX, TailCY, TailTX, TailTY, TailFront, Look;

    public static readonly PetPose Stand = new()
    {
        BodyX = 54, BodyY = 66, BodyRx = 30, BodyRy = 16, HeadX = 87, HeadY = 45, HeadR = 18,
        FrontX1 = 70, FrontX2 = 78, FrontTop = 70, FrontLen = 21, BackX1 = 31, BackX2 = 39, BackTop = 70, BackLen = 21, LegW = 8,
        TailBX = 27, TailBY = 60, TailCX = 6, TailCY = 56, TailTX = 12, TailTY = 32, TailFront = 0, Look = 0.55,
    };
    public static readonly PetPose Sit = new()
    {
        BodyX = 58, BodyY = 70, BodyRx = 21, BodyRy = 21, HeadX = 60, HeadY = 39, HeadR = 19,
        FrontX1 = 52, FrontX2 = 63, FrontTop = 72, FrontLen = 19, BackX1 = 39, BackX2 = 76, BackTop = 82, BackLen = 9, LegW = 8,
        TailBX = 40, TailBY = 84, TailCX = 32, TailCY = 99, TailTX = 84, TailTY = 95, TailFront = 0.4, Look = 0,
    };
    public static readonly PetPose Loaf = new()
    {
        BodyX = 57, BodyY = 79, BodyRx = 33, BodyRy = 13, HeadX = 83, HeadY = 71, HeadR = 17,
        FrontX1 = 74, FrontX2 = 80, FrontTop = 88, FrontLen = 1, BackX1 = 34, BackX2 = 40, BackTop = 88, BackLen = 1, LegW = 8,
        TailBX = 27, TailBY = 82, TailCX = 42, TailCY = 98, TailTX = 74, TailTY = 94, TailFront = 1, Look = 0.25,
    };
    public static readonly PetPose Dangle = new()
    {
        BodyX = 60, BodyY = 60, BodyRx = 14, BodyRy = 22, HeadX = 60, HeadY = 26, HeadR = 19,
        FrontX1 = 53, FrontX2 = 67, FrontTop = 70, FrontLen = 17, BackX1 = 51, BackX2 = 69, BackTop = 74, BackLen = 20, LegW = 7,
        TailBX = 60, TailBY = 80, TailCX = 66, TailCY = 92, TailTX = 58, TailTY = 99, TailFront = 0, Look = 0,
    };
    public static readonly PetPose Squash = new()
    {
        BodyX = 58, BodyY = 76, BodyRx = 27, BodyRy = 16, HeadX = 60, HeadY = 50, HeadR = 19,
        FrontX1 = 50, FrontX2 = 66, FrontTop = 80, FrontLen = 11, BackX1 = 38, BackX2 = 78, BackTop = 84, BackLen = 7, LegW = 8,
        TailBX = 40, TailBY = 86, TailCX = 56, TailCY = 99, TailTX = 89, TailTY = 89, TailFront = 1, Look = 0,
    };

    public static PetPose Lerp(PetPose a, PetPose b, double t)
    {
        double L(double x, double y) => x + (y - x) * t;
        return new PetPose
        {
            BodyX = L(a.BodyX, b.BodyX), BodyY = L(a.BodyY, b.BodyY), BodyRx = L(a.BodyRx, b.BodyRx), BodyRy = L(a.BodyRy, b.BodyRy),
            HeadX = L(a.HeadX, b.HeadX), HeadY = L(a.HeadY, b.HeadY), HeadR = L(a.HeadR, b.HeadR),
            FrontX1 = L(a.FrontX1, b.FrontX1), FrontX2 = L(a.FrontX2, b.FrontX2), FrontTop = L(a.FrontTop, b.FrontTop), FrontLen = L(a.FrontLen, b.FrontLen),
            BackX1 = L(a.BackX1, b.BackX1), BackX2 = L(a.BackX2, b.BackX2), BackTop = L(a.BackTop, b.BackTop), BackLen = L(a.BackLen, b.BackLen), LegW = L(a.LegW, b.LegW),
            TailBX = L(a.TailBX, b.TailBX), TailBY = L(a.TailBY, b.TailBY), TailCX = L(a.TailCX, b.TailCX), TailCY = L(a.TailCY, b.TailCY),
            TailTX = L(a.TailTX, b.TailTX), TailTY = L(a.TailTY, b.TailTY), TailFront = L(a.TailFront, b.TailFront), Look = L(a.Look, b.Look),
        };
    }
}

/// <summary>Draws the cat each frame, easing smoothly between poses.</summary>
public sealed class PetCanvas : FrameworkElement
{
    public CatPalette Palette = CatArt.Palette("ginger");
    public PetPose Pose = PetPose.Sit;
    public FaceMood Mood = FaceMood.Awake;
    public bool FacingRight = true;
    public double T, LookOverride;
    public double BaseLook => _cur.Look;

    PetPose _cur = PetPose.Sit;
    double _walkPhase, _eye = 1;
    DateTime _nextBlink = DateTime.Now.AddSeconds(3);
    readonly List<(double x, double y, double age, bool heart)> _particles = new();
    double _zTimer;

    public void SpawnHeart() => _particles.Add((60 + Random.Shared.Next(-14, 14), _cur.HeadY - _cur.HeadR, 0, true));

    public void Advance(double dt, bool walking, bool sleeping)
    {
        _cur = PetPose.Lerp(_cur, Pose, 1 - Math.Exp(-dt * 15));
        if (walking) _walkPhase += dt * 15; else _walkPhase = 0;

        var now = DateTime.Now;
        double since = (now - _nextBlink).TotalMilliseconds;
        if (since >= 0 && since < 150) _eye = Math.Abs(since - 75) / 75.0;
        else if (since >= 150) { _eye = 1; _nextBlink = now.AddMilliseconds(Random.Shared.Next(2500, 6500)); }

        if (sleeping) { _zTimer -= dt; if (_zTimer <= 0) { _particles.Add((_cur.HeadX + 10, _cur.HeadY - _cur.HeadR, 0, false)); _zTimer = 1.3; } }
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.age += dt;
            if (p.age > 1.6) _particles.RemoveAt(i);
            else _particles[i] = (p.x + (p.heart ? Math.Sin(p.age * 6) * 0.3 : 0.25), p.y - dt * 18, p.age, p.heart);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = Palette;
        var q = _cur;
        double breathe = 1 + 0.025 * Math.Sin(T * 2.4);
        double bob = _walkPhase > 0 ? -Math.Abs(Math.Sin(_walkPhase)) * 1.6 : 0;
        double swing = _walkPhase > 0 ? Math.Sin(_walkPhase) * 24 : 0;
        double tailSway = Math.Sin(T * 1.7) * 5;

        var outline = Pen(p.Outline, 1.6);
        var furB = CatArt.B(p.Fur);
        var darkB = CatArt.B(p.FurDark);

        // ground shadow
        dc.DrawEllipse(CatArt.B(Color.FromArgb(46, 0, 0, 0)), null, new Point(q.BodyX, 93), q.BodyRx * 1.1, 4);

        bool flip = !FacingRight && q.Look > 0.15;
        if (flip) dc.PushTransform(new ScaleTransform(-1, 1, 60, 0));

        void Tail()
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(q.TailBX, q.TailBY + bob), false, false);
                ctx.QuadraticBezierTo(new Point(q.TailCX, q.TailCY + bob), new Point(q.TailTX + tailSway, q.TailTY + bob), true, true);
            }
            dc.DrawGeometry(null, Pen(p.Outline, 9.2), g);
            dc.DrawGeometry(null, Pen(p.Fur, 6.4), g);
        }

        void Leg(double x, double top, double len, double angle, bool far)
        {
            if (len < 2) return;
            dc.PushTransform(new RotateTransform(angle, x, top + bob));
            var r = new Rect(x - q.LegW / 2, top + bob, q.LegW, len);
            dc.DrawRoundedRectangle(far ? darkB : furB, outline, r, q.LegW / 2, q.LegW / 2);
            dc.DrawEllipse(CatArt.B(p.Belly), null, new Point(x, top + bob + len - 2.5), q.LegW * 0.38, 2.2);
            dc.Pop();
        }

        bool frontFacing = q.Look < 0.2; // sitting / dangling: both front paws are in front of the body
        if (q.TailFront < 0.5) Tail();
        Leg(q.BackX1, q.BackTop, q.BackLen, -swing, true);
        if (!frontFacing) Leg(q.FrontX1, q.FrontTop, q.FrontLen, swing, true);

        // body
        var bc = new Point(q.BodyX, q.BodyY + bob);
        dc.DrawEllipse(furB, outline, bc, q.BodyRx, q.BodyRy * breathe);
        dc.DrawEllipse(CatArt.B(p.Belly), null, new Point(bc.X + q.BodyRx * 0.12, bc.Y + q.BodyRy * 0.42), q.BodyRx * 0.55, q.BodyRy * 0.42);
        var stripe = Pen(p.FurDark, 2.2);
        for (int i = -1; i <= 1; i++)
        {
            double sx = bc.X + i * q.BodyRx * 0.32 - q.BodyRx * 0.1;
            dc.DrawLine(stripe, new Point(sx, bc.Y - q.BodyRy * 0.92), new Point(sx + 2, bc.Y - q.BodyRy * 0.55));
        }

        Leg(q.BackX2, q.BackTop, q.BackLen, swing, false);
        if (frontFacing) Leg(q.FrontX1, q.FrontTop, q.FrontLen, swing, false);
        Leg(q.FrontX2, q.FrontTop, q.FrontLen, -swing, false);
        if (q.TailFront >= 0.5) Tail();

        double look = Math.Clamp(q.Look + LookOverride * 0.5, -1, 1);
        CatArt.DrawFace(dc, new Point(q.HeadX, q.HeadY + bob), q.HeadR, p, Mood, _eye, look, 0, whiskers: true);

        if (flip) dc.Pop();

        foreach (var pt in _particles)
        {
            double a = Math.Max(0, 1 - pt.age / 1.6);
            if (pt.heart) CatArt.DrawHeart(dc, new Point(pt.x, pt.y), 4.5, Color.FromRgb(0xFF, 0x7A, 0x99), a);
            else
            {
                var ft = new FormattedText("z", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 10 + pt.age * 4,
                    CatArt.B(Color.FromRgb(0x8A, 0x9B, 0xD8), a), 1.25);
                dc.DrawText(ft, new Point(pt.x, pt.y));
            }
        }
    }

    static readonly Dictionary<(Color, double), Pen> Pens = new();
    static Pen Pen(Color c, double w)
    {
        if (Pens.TryGetValue((c, w), out var pen)) return pen;
        pen = new Pen(CatArt.B(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return Pens[(c, w)] = pen;
    }
}
