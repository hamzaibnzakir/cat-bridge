using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace DynamicIsland.Bridge;

/// <summary>Connects the island to your iPhone over Tailscale and shows the pairing QR code.</summary>
public sealed class BridgeWindow : Window
{
    readonly AppSettings _s;
    readonly IslandWindow _island;
    readonly Brush _accent;
    static Brush Fz(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static readonly Brush Dim = Fz(Color.FromRgb(0x8E, 0x8E, 0x93)), Red = Fz(Color.FromRgb(0xFF, 0x6B, 0x60)), Card = Fz(Color.FromRgb(0x17, 0x17, 0x1A));

    readonly StackPanel _body = new();
    readonly CatFace _cat = new() { Width = 70, Height = 58, Mood = FaceMood.Awake, HorizontalAlignment = HorizontalAlignment.Center };
    bool _busy;

    public BridgeWindow(AppSettings s, IslandWindow island)
    {
        _s = s; _island = island;
        try { _accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Accent)); } catch { _accent = Brushes.Orange; }
        Title = "iPhone bridge";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); Foreground = Brushes.White;

        var root = new StackPanel();
        root.Children.Add(_cat);
        root.Children.Add(_body);
        Content = new Border
        {
            Background = Fz(Color.FromRgb(0x0B, 0x0B, 0x0D)), CornerRadius = new CornerRadius(32),
            BorderBrush = Fz(Color.FromRgb(0x24, 0x24, 0x28)), BorderThickness = new Thickness(1),
            Padding = new Thickness(28, 18, 28, 24), Child = root,
        };
        MouseLeftButtonDown += (_, e) => { try { DragMove(); } catch { } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += async (_, _) => await Refresh();
    }

    async Task Refresh(string? error = null)
    {
        _body.Children.Clear();
        H("Your iPhone, anywhere");
        P("Checking Tailscale…");
        var st = await Tailscale.GetState();
        _body.Children.Clear();

        if (_island.BridgeError != null) error ??= _island.BridgeError;

        if (!st.Installed)
        {
            _cat.Mood = FaceMood.Surprised;
            H("Install Tailscale first");
            P("Install Tailscale on this laptop and on your iPhone, and sign in to both with the same account. Then come back here.");
            Buttons(("Close", false, Close), ("Get Tailscale", true, () => Open("https://tailscale.com/download")));
            Err(error);
            return;
        }
        if (!st.Running || st.DnsName == null)
        {
            _cat.Mood = FaceMood.Content;
            H("Tailscale isn't connected");
            P(st.Message ?? "Open Tailscale and connect.");
            Buttons(("Close", false, Close), ("Check again", true, async () => await Refresh()));
            Err(error);
            return;
        }

        bool serving = await Tailscale.IsServing(_s.BridgePort);
        if (!serving)
        {
            _cat.Mood = FaceMood.Awake;
            H("Connect your iPhone");
            Bullet("Works anywhere your iPhone and laptop are both on Tailscale, not just at home.");
            Bullet("Clipboard both ways, photos and files, links, music controls, focus timer, lock, and find my laptop.");
            Bullet("Private: only devices signed in to your Tailscale can reach it, over HTTPS, plus a pairing key.");
            P($"This laptop will be reachable at https://{st.DnsName} inside your tailnet only.");
            Buttons(("Not now", false, Close), ("Connect", true, async () => await Connect()));
            Err(error);
            return;
        }

        // connected: show the pairing QR
        _cat.Mood = FaceMood.Happy;
        H("Scan with your iPhone");
        var url = $"https://{st.DnsName}/#k={_s.BridgeKey}";
        var qr = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 12),
            Child = new Image { Source = Qr(url), Width = 210, Height = 210 },
        };
        RenderOptions.SetBitmapScalingMode(qr.Child, BitmapScalingMode.NearestNeighbor);
        _body.Children.Add(qr);
        Step(1, "Turn Tailscale on in your iPhone.");
        Step(2, "Point your Camera at this code and open the link.");
        Step(3, "In Safari, tap Share → Add to Home Screen. That's your Cat Bridge app.");
        Step(4, "Open Cat Bridge from the Home Screen and tap Turn on under Alerts.");
        P("Anyone who scans this code can pair, so don't share screenshots of it.");
        int n = _island.Push.Count;
        P(n == 0 ? "iPhone alerts: not turned on yet."
                 : $"iPhone alerts: on for {n} {(n == 1 ? "device" : "devices")}. Laptop alerts arrive while you've been away for {_s.PushAwayMinutes} min.");
        if (n > 0)
            Buttons(("Send a test", false, () => { _island.Push.Send("Meow from your laptop", "Alerts are working.", tag: "test"); }),
                    ("Ring my iPhone", false, () => _island.RingPhone()));
        Buttons(
            ("Turn off", false, async () => { await Tailscale.StopServe(); await Refresh(); }),
            ("New key", false, () =>
            {
                _s.BridgeKey = BridgeServer.NewKey(); _s.Save(); _island.StartBridge();
                _ = Refresh("New key made. Phones you paired before need to scan again.");
            }),
            ("Done", true, Close));
        Err(error);
    }

    async Task Connect()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_island.BridgeError != null) _island.StartBridge();
            var (ok, approve, msg) = await Tailscale.Serve(_s.BridgePort);
            if (approve != null) Open(approve);
            await Refresh(ok ? null : msg);
        }
        catch (Exception ex) { App.Log(ex); await Refresh(ex.Message); }
        finally { _busy = false; }
    }

    static ImageSource Qr(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        var bi = new BitmapImage();
        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(png); bi.EndInit(); bi.Freeze();
        return bi;
    }

    static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }

    // ───────── tiny layout helpers ─────────

    void H(string text) => _body.Children.Add(new TextBlock
    {
        Text = text, FontSize = 19, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), Margin = new Thickness(0, 10, 0, 10),
    });

    void P(string text) => _body.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2), LineHeight = 18 });

    void Err(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _body.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Red, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
    }

    void Bullet(string text)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = _accent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 7, 0, 0) });
        var t = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 1); g.Children.Add(t);
        _body.Children.Add(g);
    }

    void Step(int n, string text)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Card, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = n.ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        g.Children.Add(badge);
        var t = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        Grid.SetColumn(t, 1); g.Children.Add(t);
        _body.Children.Add(g);
    }

    void Buttons(params (string label, bool primary, Action act)[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var (label, primary, act) in items)
        {
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, primary ? _accent : Fz(Color.FromRgb(0x1F, 0x1F, 0x23)));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            bd.SetValue(Border.PaddingProperty, new Thickness(16, 8, 16, 8));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(TextElement.FontSizeProperty, 13.0);
            cp.SetValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
            cp.SetValue(TextElement.ForegroundProperty, primary ? Fz(Color.FromRgb(0x1A, 0x12, 0x0C)) : Brushes.White);
            bd.AppendChild(cp);
            var b = new Button { Content = label, Margin = new Thickness(4, 0, 4, 0), Cursor = Cursors.Hand, Template = new ControlTemplate(typeof(Button)) { VisualTree = bd } };
            b.Click += (_, _) => act();
            row.Children.Add(b);
        }
        _body.Children.Add(row);
    }
}
