using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DynamicIsland.Notify;

public sealed class BannerSpec
{
    public string App = "", Title = "", Body = "";
    public ImageSource? Logo;
    public string? Glyph;              // Segoe Fluent icon when there's no logo
    public Color Tint = Color.FromRgb(0x3A, 0x3A, 0x3F);
    public string? Source;             // "iPhone" badge
    public Shopify.Order? Order;       // makes it a Shopify sale card
    public string? Footer;
    public Action? OnClick;
    public (string label, Action act)[] Actions = Array.Empty<(string, Action)>();
}

/// <summary>
/// macOS-style notification banners: they drop in at the top-right (or under the island), stack newest-first,
/// pause while you hover, and go away with a click on ×, a two-finger swipe right, or on their own.
/// The window is click-through everywhere except on the banners, and never steals focus.
/// </summary>
public sealed class BannerHost : Window
{
    const double CardWidth = 372;
    readonly AppSettings _s;
    readonly StackPanel _stack = new() { Margin = new Thickness(14) };
    readonly Func<Rect?> _islandRect;

    public BannerHost(AppSettings s, Func<Rect?> islandRect)
    {
        _s = s; _islandRect = islandRect;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = null;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = CardWidth + 28; SizeToContent = SizeToContent.Height;
        Content = _stack;
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80); // NOACTIVATE | TOOLWINDOW
        };
        SizeChanged += (_, _) => Place();
        SmoothScroll.SidewaysSwipe += OnSwipe;
    }

    void Place()
    {
        var wa = SystemParameters.WorkArea;
        if (_s.BannerPosition == "center" && _islandRect() is Rect r)
        { Left = r.Left + r.Width / 2 - Width / 2; Top = r.Bottom - 4; }
        else
        { Left = wa.Right - Width - 2; Top = wa.Top + 2; }
    }

    public void Show(BannerSpec b)
    {
        var card = Build(b);
        _stack.Children.Insert(0, card);
        while (_stack.Children.Count > 4) _stack.Children.RemoveAt(_stack.Children.Count - 1);
        if (!IsVisible) { Show(); }
        Place();
        AnimateIn(card);
        if (b.Order != null && _s.ShopifySound) Chime.Play();
    }

    // ───────── one banner ─────────

    sealed class Card : Border
    {
        public DispatcherTimer? Timer;
        public bool Closing;
        public double Swipe;
    }

    Card Build(BannerSpec b)
    {
        bool sale = b.Order != null;
        var card = new Card
        {
            Width = CardWidth, Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1E, 0x1E, 0x22)),
            BorderBrush = new SolidColorBrush(sale ? Color.FromArgb(0x70, 0x95, 0xBF, 0x47) : Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1), Padding = new Thickness(13, 12, 14, 12), Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Direction = 270, Opacity = 0.45, Color = Colors.Black },
            RenderTransform = new TranslateTransform(), Opacity = 0,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // icon
        var icon = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 11, 0), ClipToBounds = true };
        if (sale) { icon.Background = new SolidColorBrush(Color.FromRgb(0x95, 0xBF, 0x47)); icon.Child = Glyph("", 19, Brushes.White); }
        else if (b.Logo != null) { icon.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)); icon.Child = new Image { Source = b.Logo, Width = 28, Height = 28 }; }
        else if (b.Glyph != null) { icon.Background = new SolidColorBrush(b.Tint); icon.Child = Glyph(b.Glyph, 17, Brushes.White); }
        else
        {
            icon.Background = new SolidColorBrush(TintFor(b.App));
            icon.Child = new TextBlock { Text = (b.App.Trim().FirstOrDefault(char.IsLetterOrDigit) is char c and not '\0' ? char.ToUpperInvariant(c).ToString() : "•"), Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        grid.Children.Add(icon);

        var col = new StackPanel();
        Grid.SetColumn(col, 1);
        grid.Children.Add(col);

        // header: APP · from iPhone          now
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var appLine = new TextBlock { FontSize = 11.5, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis };
        appLine.Inlines.Add(new System.Windows.Documents.Run(sale ? "SHOPIFY" : b.App.ToUpperInvariant()) { FontWeight = FontWeights.SemiBold });
        if (b.Source != null) appLine.Inlines.Add(new System.Windows.Documents.Run("  ·  " + b.Source));
        head.Children.Add(appLine);
        var when = new TextBlock { Text = "now", FontSize = 11.5, Foreground = Dim, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(when, 1);
        head.Children.Add(when);
        col.Children.Add(head);

        if (sale)
        {
            var o = b.Order!;
            var amount = new TextBlock
            {
                Text = o.Amount is decimal a ? Shopify.Format(a, o.Currency) : "New order",
                FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xB5, 0xE0, 0x6A)), Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            col.Children.Add(amount);
            var who = string.Join(" · ", new[] { o.Number, o.Customer, o.Store }.Where(x => !string.IsNullOrWhiteSpace(x)));
            col.Children.Add(Line(who.Length > 0 ? who : b.Title, 13, Brushes.White, FontWeights.SemiBold, 1));
        }
        else
        {
            col.Children.Add(Line(b.Title, 13.5, Brushes.White, FontWeights.SemiBold, 1, top: 2));
            if (!string.IsNullOrWhiteSpace(b.Body)) col.Children.Add(Line(b.Body, 12.5, Soft, FontWeights.Normal, 3));
        }
        if (!string.IsNullOrEmpty(b.Footer))
            col.Children.Add(new Border
            {
                Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(9, 4, 9, 4), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock { Text = b.Footer, FontSize = 11.5, Foreground = Soft, TextTrimming = TextTrimming.CharacterEllipsis },
            });

        // actions (show on hover, like a Mac)
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        foreach (var (label, act) in b.Actions)
            actions.Children.Add(Chip(label, () => { act(); Dismiss(card); }));
        if (b.Actions.Length > 0) col.Children.Add(actions);

        // close button: top-left corner, on hover
        var close = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-21, -20, 0, 0),
            Child = Glyph("", 8, Brushes.White), Visibility = Visibility.Hidden, Cursor = Cursors.Arrow,
        };
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; Dismiss(card); };
        var layer = new Grid();
        layer.Children.Add(grid);
        layer.Children.Add(close);
        card.Child = layer;

        // timing: hover pauses, leaving restarts
        card.Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(_s.BannerSeconds, 2, 60) + (sale ? 2 : 0)) };
        card.Timer.Tick += (_, _) => Dismiss(card);
        card.Timer.Start();
        card.MouseEnter += (_, _) => { card.Timer?.Stop(); close.Visibility = Visibility.Visible; actions.Visibility = Visibility.Visible; card.Background = new SolidColorBrush(Color.FromArgb(0xF7, 0x26, 0x26, 0x2B)); };
        card.MouseLeave += (_, _) => { if (!card.Closing) card.Timer?.Start(); close.Visibility = Visibility.Hidden; actions.Visibility = Visibility.Collapsed; card.Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1E, 0x1E, 0x22)); };

        // click opens; dragging right dismisses
        Point? down = null;
        card.MouseLeftButtonDown += (_, e) => { down = e.GetPosition(this); card.CaptureMouse(); };
        card.MouseMove += (_, e) =>
        {
            if (down is not Point p || e.LeftButton != MouseButtonState.Pressed) return;
            var dx = Math.Max(0, e.GetPosition(this).X - p.X);
            ((TranslateTransform)card.RenderTransform).X = dx;
            card.Opacity = 1 - Math.Min(0.7, dx / 300);
        };
        card.MouseLeftButtonUp += (_, e) =>
        {
            card.ReleaseMouseCapture();
            if (down is not Point p) return;
            down = null;
            var dx = e.GetPosition(this).X - p.X;
            if (dx > 70) { Dismiss(card); return; }
            ((TranslateTransform)card.RenderTransform).X = 0; card.Opacity = 1;
            if (Math.Abs(dx) < 6) { try { b.OnClick?.Invoke(); } catch (Exception ex) { App.Log(ex); } Dismiss(card); }
        };
        return card;
    }

    bool OnSwipe(DependencyObject over, int delta)
    {
        for (var d = over; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is Card c)
            {
                c.Swipe += delta;
                if (c.Swipe > 90) Dismiss(c);
                else if (c.Swipe > 0) ((TranslateTransform)c.RenderTransform).X = c.Swipe * 0.6;
                return true;
            }
            if (d is Window) break;
        }
        return false;
    }

    void AnimateIn(Card c)
    {
        var t = (TranslateTransform)c.RenderTransform;
        bool fromTop = _s.BannerPosition == "center";
        var spring = new SpringEase { Damping = 7, Frequency = 12 };
        if (fromTop) t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-60, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = spring });
        else t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(CardWidth * 0.6, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = spring });
        c.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    void Dismiss(Card c)
    {
        if (c.Closing) return;
        c.Closing = true;
        c.Timer?.Stop();
        var t = (TranslateTransform)c.RenderTransform;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(t.X, CardWidth + 40, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        var fade = new DoubleAnimation(c.Opacity, 0, TimeSpan.FromMilliseconds(240));
        fade.Completed += (_, _) =>
        {
            // collapse the gap smoothly so the cards below slide up
            var h = new DoubleAnimation(c.ActualHeight + 10, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var host = new Border { Height = c.ActualHeight + 10 };
            int i = _stack.Children.IndexOf(c);
            if (i < 0) return;
            _stack.Children[i] = host; // replace with an empty spacer that shrinks
            h.Completed += (_, _) => { _stack.Children.Remove(host); if (_stack.Children.Count == 0) Hide(); };
            host.BeginAnimation(HeightProperty, h);
        };
        c.BeginAnimation(OpacityProperty, fade);
    }

    public void ClearAll() { foreach (var c in _stack.Children.OfType<Card>().ToList()) Dismiss(c); }

    // ───────── bits ─────────

    static readonly Brush Dim = Frozen(Color.FromRgb(0x9A, 0x9A, 0xA0));
    static readonly Brush Soft = Frozen(Color.FromRgb(0xC9, 0xC9, 0xCE));
    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static TextBlock Glyph(string g, double size, Brush fg) => new()
    {
        Text = g, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, Foreground = fg,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    static TextBlock Line(string text, double size, Brush fg, FontWeight w, int maxLines, double top = 1) => new()
    {
        Text = text, FontSize = size, Foreground = fg, FontWeight = w, TextWrapping = maxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = Math.Ceiling(size * 1.36 * maxLines) + 1, Margin = new Thickness(0, top, 0, 0),
    };

    static Border Chip(string label, Action act)
    {
        var b = new Border
        {
            Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
        };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; act(); };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        return b;
    }

    static Color TintFor(string app)
    {
        var a = app.ToLowerInvariant();
        if (a.Contains("whatsapp")) return Color.FromRgb(0x25, 0xD3, 0x66);
        if (a.Contains("message") || a.Contains("imessage")) return Color.FromRgb(0x30, 0xC8, 0x5A);
        if (a.Contains("mail") || a.Contains("gmail") || a.Contains("outlook")) return Color.FromRgb(0x2F, 0x7B, 0xF6);
        if (a.Contains("telegram")) return Color.FromRgb(0x2A, 0xA3, 0xE0);
        if (a.Contains("instagram")) return Color.FromRgb(0xD6, 0x29, 0x76);
        if (a.Contains("tiktok")) return Color.FromRgb(0x11, 0x11, 0x11);
        if (a.Contains("slack")) return Color.FromRgb(0x4A, 0x15, 0x4B);
        if (a.Contains("discord")) return Color.FromRgb(0x58, 0x65, 0xF2);
        if (a.Contains("calendar")) return Color.FromRgb(0xFF, 0x45, 0x3A);
        if (a.Contains("bank") || a.Contains("pay")) return Color.FromRgb(0x0A, 0x84, 0xFF);
        int h = 0; foreach (var ch in a) h = h * 31 + ch;
        var palette = new[] { Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0x64, 0xD2, 0xFF), Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0x5E, 0x5C, 0xE6), Color.FromRgb(0xAC, 0x8E, 0x68) };
        return palette[Math.Abs(h % palette.Length)];
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
}

/// <summary>A short "cha-ching" made in code (no sound files to ship).</summary>
static class Chime
{
    static byte[]? _wav;

    public static void Play()
    {
        try
        {
            _wav ??= Make();
            var p = new SoundPlayer(new MemoryStream(_wav));
            p.Play();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static byte[] Make()
    {
        const int rate = 44100;
        var samples = new List<short>();
        void Tone(double ms, (double f, double a)[] parts, double decay, double gapMs = 0)
        {
            int n = (int)(rate * ms / 1000);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate, env = Math.Exp(-decay * t) * Math.Min(1, i / (rate * 0.003));
                double v = 0; foreach (var (f, a) in parts) v += a * Math.Sin(2 * Math.PI * f * t);
                samples.Add((short)Math.Clamp(v * env * 9000, short.MinValue, short.MaxValue));
            }
            for (int i = 0; i < (int)(rate * gapMs / 1000); i++) samples.Add(0);
        }
        Tone(70, new[] { (2637.0, 0.6), (5274.0, 0.25) }, 30, 25);                 // "cha"
        Tone(520, new[] { (1976.0, 0.55), (2637.0, 0.45), (3951.0, 0.2) }, 7);      // "ching"
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + samples.Count * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples.Count * 2);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
