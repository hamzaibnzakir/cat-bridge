using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DynamicIsland.FaceId;

public enum FaceMode { Enroll, Test }

/// <summary>Cat-themed face set-up and face check. Step 1 test harness for the sign-in screen version.</summary>
public sealed class FaceWindow : Window
{
    readonly AppSettings _s;
    readonly FaceMode _mode;
    readonly Brush _accent;
    static readonly Brush Red = Fz(Color.FromRgb(0xFF, 0x45, 0x3A));
    static readonly Brush Dim = Fz(Color.FromRgb(0x8E, 0x8E, 0x93));
    static readonly Brush Faint = Fz(Color.FromRgb(0x5A, 0x5A, 0x5E));
    static Brush Fz(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    readonly TextBlock _title = new(), _sub = new(), _prompt = new(), _hint = new();
    readonly Ring _ring = new() { Width = 252, Height = 252, Thickness = 5 };
    readonly Ellipse _preview = new() { Width = 226, Height = 226, Fill = Fz(Color.FromRgb(0x17, 0x17, 0x1A)) };
    readonly ImageBrush _previewBrush = new() { Stretch = Stretch.UniformToFill };
    readonly CatFace _cat = new() { Width = 80, Height = 66, Mood = FaceMood.Awake };
    readonly TextBlock _arrowL = Arrow(""), _arrowR = Arrow("");
    readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
    readonly Button _action, _cancel;

    FaceCamera? _camera;
    FaceVerifier? _verifier;
    readonly FaceEnroller _enroller = new();
    bool _busy, _paused, _finished;

    public FaceWindow(AppSettings s, FaceMode mode)
    {
        _s = s; _mode = mode;
        try { _accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Accent)); } catch { _accent = Brushes.Orange; }
        _ring.Stroke = _accent;

        Title = mode == FaceMode.Enroll ? "Set up face unlock" : "Face unlock";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Width = 380; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true; ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = Brushes.White;

        _preview.Fill = Fz(Color.FromRgb(0x17, 0x17, 0x1A));

        _title.FontSize = 19; _title.FontWeight = FontWeights.SemiBold; _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        _sub.FontSize = 12; _sub.Foreground = Dim; _sub.HorizontalAlignment = HorizontalAlignment.Center; _sub.Margin = new Thickness(0, 3, 0, 0);
        _prompt.FontSize = 16; _prompt.FontWeight = FontWeights.SemiBold; _prompt.TextAlignment = TextAlignment.Center; _prompt.TextWrapping = TextWrapping.Wrap; _prompt.Margin = new Thickness(0, 22, 0, 0);
        _hint.FontSize = 12.5; _hint.Foreground = Dim; _hint.TextAlignment = TextAlignment.Center; _hint.TextWrapping = TextWrapping.Wrap; _hint.Margin = new Thickness(0, 6, 0, 0); _hint.MinHeight = 34;

        var stage = new Grid { Width = 270, Height = 270, Margin = new Thickness(0, 30, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        stage.Children.Add(_ring);
        stage.Children.Add(_preview);
        _cat.VerticalAlignment = VerticalAlignment.Top; _cat.HorizontalAlignment = HorizontalAlignment.Center; _cat.Margin = new Thickness(0, -44, 0, 0);
        stage.Children.Add(_cat);
        _arrowL.HorizontalAlignment = HorizontalAlignment.Left; _arrowR.HorizontalAlignment = HorizontalAlignment.Right;
        _arrowL.Margin = new Thickness(-18, 0, 0, 0); _arrowR.Margin = new Thickness(0, 0, -18, 0);
        stage.Children.Add(_arrowL); stage.Children.Add(_arrowR);

        _cancel = Btn("Cancel", false);
        _cancel.Click += (_, _) => Close();
        _action = Btn("Try again", true);
        _action.Visibility = Visibility.Collapsed;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(_cancel); buttons.Children.Add(_action);

        var footer = new TextBlock
        {
            Text = "Your face is stored as encrypted numbers on this PC. Never a photo, never uploaded.",
            FontSize = 11, Foreground = Faint, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 16, 10, 0),
        };

        var stack = new StackPanel();
        foreach (var el in new UIElement[] { _title, _sub, stage, _prompt, _hint, _dots, buttons, footer }) stack.Children.Add(el);

        Content = new Border
        {
            Background = Fz(Color.FromRgb(0x0B, 0x0B, 0x0D)), CornerRadius = new CornerRadius(36),
            BorderBrush = Fz(Color.FromRgb(0x24, 0x24, 0x28)), BorderThickness = new Thickness(1),
            Padding = new Thickness(26, 24, 26, 22), Child = stack,
        };

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => Begin();
        Closed += (_, _) => _camera?.Dispose();
    }

    static TextBlock Arrow(string glyph) => new()
    {
        Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 26,
        VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Foreground = Brushes.White,
    };

    Button Btn(string text, bool primary)
    {
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, primary ? _accent : Fz(Color.FromRgb(0x1F, 0x1F, 0x23)));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        bd.SetValue(Border.PaddingProperty, new Thickness(18, 8, 18, 8));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(TextElement.FontSizeProperty, 13.0);
        cp.SetValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
        cp.SetValue(TextElement.ForegroundProperty, primary ? Fz(Color.FromRgb(0x1A, 0x12, 0x0C)) : Brushes.White);
        bd.AppendChild(cp);
        return new Button { Content = text, Margin = new Thickness(5, 0, 5, 0), Cursor = Cursors.Hand, Focusable = false, Template = new ControlTemplate(typeof(Button)) { VisualTree = bd } };
    }

    // ───────── flow ─────────

    void Begin()
    {
        var models = System.IO.Path.Combine(AppContext.BaseDirectory, "models");
        if (!File.Exists(System.IO.Path.Combine(models, FaceEngine.RecognizerFile)))
        {
            Show("Face models are missing", "Reinstall Dynamic Island so the models folder sits next to the app.", FaceMood.Surprised, Red);
            return;
        }

        FaceStore.Data? data = null;
        if (_mode == FaceMode.Test)
        {
            data = FaceStore.Load();
            if (data == null || data.Embeddings.Count == 0)
            {
                Show("I don't know your face yet", "Set up face unlock first from the tray menu.", FaceMood.Surprised, null);
                return;
            }
            _verifier = new FaceVerifier { Threshold = _s.FaceMatchThreshold, FlipTurn = _s.FaceTurnFlip };
            _title.Text = "Face unlock";
            _sub.Text = "Test run · nothing gets unlocked yet";
            UpdateDots();
        }
        else
        {
            _title.Text = "Set up face unlock";
            _sub.Text = "Takes about 10 seconds";
        }

        _prompt.Text = "Waking up the camera…";
        _camera = new FaceCamera(_s.CameraIndex, models, data?.Embeddings);
        _camera.Frame += f =>
        {
            if (_busy) return; // drop frames rather than queue them
            _busy = true;
            Dispatcher.BeginInvoke(() => { try { OnFrame(f); } finally { _busy = false; } });
        };
        _camera.Error += msg => Dispatcher.BeginInvoke(() => Show("Camera problem", msg, FaceMood.Surprised, Red));
        _camera.Start();
    }

    void OnFrame(CameraFrame f)
    {
        if (f.Pixels != null)
        {
            var bmp = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgr24, null, f.Pixels, f.Stride);
            bmp.Freeze();
            _previewBrush.ImageSource = bmp;
            if (_preview.Fill != _previewBrush) _preview.Fill = _previewBrush;
        }
        if (_finished || _paused) return;
        if (!f.Ready) { Say("Getting ready…", "Loading face recognition"); return; }
        if (_mode == FaceMode.Enroll) EnrollFrame(f); else TestFrame(f);
    }

    void EnrollFrame(CameraFrame f)
    {
        var st = _enroller.Feed(f.Obs, f.Embedding);
        _ring.Value = _enroller.Progress;
        _cat.Mood = FaceMood.Awake;
        _arrowL.Visibility = _arrowR.Visibility = Visibility.Collapsed;
        if (!f.Obs.HasFace) { Say("Look at me", "Center your face in the circle, in good light."); return; }
        if (_enroller.TooMany) { Say("Just you, please", "I can see more than one face."); return; }
        if (_enroller.TooFar) { Say("Come a little closer", "Your face should fill most of the circle."); return; }
        if (_enroller.NotSamePerson) { Say("Hmm, who's that?", "Only you in front of the camera during set-up."); return; }
        switch (st)
        {
            case EnrollStep.Straight: Say("Look straight at me", "Hold still for a second…"); break;
            case EnrollStep.SideA: Say("Now turn your head slightly to one side", "Just a little, like glancing at a second screen."); break;
            case EnrollStep.SideB: Say("And slightly to the other side", "Almost done…"); break;
            case EnrollStep.Done:
                _finished = true;
                _camera?.Dispose();
                try
                {
                    FaceStore.Save(_enroller.Embeddings);
                    Show("All set! I'll know you anywhere", "Try it now with Test face unlock.", FaceMood.Happy, _accent, action: ("Test it", () => { var w = new FaceWindow(_s, FaceMode.Test); w.Show(); Close(); }));
                }
                catch (Exception ex) { App.Log(ex); Show("Couldn't save your face", ex.Message, FaceMood.Surprised, Red); }
                break;
        }
    }

    void TestFrame(CameraFrame f)
    {
        var v = _verifier!;
        var st = v.Feed(f.Obs);
        bool left = (v.TurnDir > 0); // the person's left = screen left on the mirrored preview
        _arrowL.Visibility = st == VerifyStep.Turn && left ? Visibility.Visible : Visibility.Collapsed;
        _arrowR.Visibility = st == VerifyStep.Turn && !left ? Visibility.Visible : Visibility.Collapsed;
        _ring.Stroke = _accent;
        _ring.Value = st switch
        {
            VerifyStep.FindFace => 0.05, VerifyStep.Identify => 0.3, VerifyStep.Turn => 0.55,
            VerifyStep.ReturnCenter => 0.75, VerifyStep.Confirm => 0.9, VerifyStep.Passed => 1, _ => _ring.Value,
        };

        if (st is VerifyStep.FindFace or VerifyStep.Identify or VerifyStep.Turn or VerifyStep.ReturnCenter or VerifyStep.Confirm)
        {
            if (!f.Obs.HasFace) { Say("Look at me", "Face the camera in good light."); _cat.Mood = FaceMood.Awake; return; }
            if (v.TooMany) { Say("Just you, please", "I can see more than one face."); return; }
            if (v.TooFar) { Say("Come a little closer", "Your face should fill most of the circle."); return; }
        }
        switch (st)
        {
            case VerifyStep.FindFace:
            case VerifyStep.Identify: _cat.Mood = FaceMood.Content; Say("Checking it's you…", "Look straight at me."); break;
            case VerifyStep.Turn: _cat.Mood = FaceMood.Awake; Say(left ? "Turn your head a little to your left" : "Turn your head a little to your right", "This proves you're not a photo."); break;
            case VerifyStep.ReturnCenter: Say("And back to me", ""); break;
            case VerifyStep.Confirm: Say("Almost there…", ""); break;
            case VerifyStep.Passed:
                _finished = true; _camera?.Dispose();
                Show("Welcome back!", "That was you. Face unlock works on this PC.", FaceMood.Happy, _accent);
                break;
            case VerifyStep.AttemptFailed:
                _ring.Stroke = Red;
                _cat.Mood = FaceMood.Surprised;
                if (v.Attempt >= v.MaxAttempts)
                {
                    v.NextAttempt();
                    _finished = true; _camera?.Dispose();
                    UpdateDots();
                    Show("Let's use your password instead", "Two tries didn't work. On the real sign-in screen, this is where your password box appears.", FaceMood.Content, Red);
                    break;
                }
                Say(v.FailReason, "One more try…");
                UpdateDots(failedNow: true);
                _paused = true;
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
                t.Tick += (_, _) => { t.Stop(); v.NextAttempt(); _paused = false; _ring.Stroke = _accent; };
                t.Start();
                break;
        }
    }

    void UpdateDots(bool failedNow = false)
    {
        _dots.Children.Clear();
        if (_verifier == null) return;
        int used = _verifier.Attempt - (failedNow ? 0 : 1);
        for (int i = 0; i < _verifier.MaxAttempts; i++)
            _dots.Children.Add(new Ellipse { Width = 7, Height = 7, Margin = new Thickness(3, 0, 3, 0), Fill = i < used ? Red : Fz(Color.FromRgb(0x3A, 0x3A, 0x3F)) });
    }

    void Say(string prompt, string hint)
    {
        if (_prompt.Text != prompt)
        {
            _prompt.Text = prompt;
            _prompt.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(160)));
        }
        _hint.Text = hint;
    }

    void Show(string prompt, string hint, FaceMood mood, Brush? ring, (string label, Action act)? action = null)
    {
        _finished = true;
        _arrowL.Visibility = _arrowR.Visibility = Visibility.Collapsed;
        Say(prompt, hint);
        _cat.Mood = mood;
        if (ring != null) { _ring.Stroke = ring; _ring.Value = 1; }
        _cancel.Content = "Close";
        if (action is (string label, Action act))
        {
            _action.Content = label;
            _action.Visibility = Visibility.Visible;
            _action.Click += (_, _) => act();
        }
    }
}
