using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static DynamicIsland.Center.Ui;

namespace DynamicIsland.Center;

/// <summary>
/// Cat Center: the desktop app for everything — settings, iPhone, notifications, Shopify, apps, files, face unlock.
/// Sidebar on the left (shrinks to icons on narrow windows), one scrolling page on the right.
/// </summary>
public sealed partial class CenterWindow : Window
{
    static CenterWindow? _open;
    readonly IslandWindow _island;
    readonly AppSettings _s;
    readonly Grid _root = new();
    readonly StackPanel _nav = new();
    readonly Border _sidebar;
    readonly TextBlock _title = T("", 26, Text, FontWeights.SemiBold, false);
    readonly TextBlock _subtitle = T("", 13, Dim, null, true, new Thickness(0, 4, 0, 0));
    readonly StackPanel _page = new() { MaxWidth = 900, Margin = new Thickness(32, 0, 32, 40) };
    readonly ScrollViewer _scroll;
    readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly List<Action> _liveUpdaters = new();
    readonly Dictionary<string, (Border item, TextBlock label)> _navItems = new();
    readonly TextBlock _brand;
    string _current = "home";
    bool _compact;

    static readonly (string id, string glyph, string label, string subtitle)[] Pages =
    {
        ("home", "", "Home", "Everything at a glance"),
        ("apps", "", "Apps and taskbar", "Run the island as your taskbar"),
        ("notifications", "", "Notifications", "Mac-style banners, forwarding and the sign-in screen"),
        ("shopify", "", "Shopify", "Your orders as they come in"),
        ("iphone", "", "iPhone", "Cat Bridge over Tailscale, alerts with Bark"),
        ("island", "", "Island", "Widgets, colours and timing"),
        ("cat", "", "Desktop cat", "Your little helper on the taskbar line"),
        ("files", "", "Files and clipboard", "The shelf and what your iPhone sends"),
        ("face", "", "Face unlock", "Sign in with your face"),
        ("servers", "", "Servers", "Watch your VPS and tunnels"),
        ("general", "", "General", "Start-up, shortcuts and about"),
    };

    public static void Open(IslandWindow island, string page = "home")
    {
        if (_open == null) { _open = new CenterWindow(island); _open.Show(); }
        if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
        _open.Go(page);
        _open.Activate();
    }

    CenterWindow(IslandWindow island)
    {
        _island = island; _s = island.Settings;
        Title = "Cat Center";
        Width = 1100; Height = 760; MinWidth = 640; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Bg; Foreground = Text; FontFamily = Font;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/DynamicIsland;component/Installer/app.ico", UriKind.Absolute)); } catch { }

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // sidebar
        var side = new DockPanel { LastChildFill = true };
        var brandRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 18, 12, 18) };
        brandRow.Children.Add(new CatFace { Width = 36, Height = 30, Mood = FaceMood.Happy });
        _brand = T("Cat Center", 16, Text, FontWeights.SemiBold, false, new Thickness(10, 0, 0, 0));
        _brand.VerticalAlignment = VerticalAlignment.Center;
        brandRow.Children.Add(_brand);
        DockPanel.SetDock(brandRow, Dock.Top);
        side.Children.Add(brandRow);
        var navScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _nav };
        side.Children.Add(navScroll);
        foreach (var p in Pages) _nav.Children.Add(NavItem(p.id, p.glyph, p.label));
        _sidebar = new Border { Background = Side, BorderBrush = Edge, BorderThickness = new Thickness(0, 0, 1, 0), Width = 248, Child = side };
        _root.Children.Add(_sidebar);

        // content
        var content = new StackPanel();
        var header = new StackPanel { MaxWidth = 900, Margin = new Thickness(32, 30, 32, 18) };
        header.Children.Add(_title);
        header.Children.Add(_subtitle);
        content.Children.Add(header);
        content.Children.Add(_page);
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content };
        _scroll.Resources[SystemParameters.VerticalScrollBarWidthKey] = 8.0;
        Grid.SetColumn(_scroll, 1);
        _root.Children.Add(_scroll);
        Content = _root;

        SizeChanged += (_, _) => Responsive();
        SourceInitialized += (_, _) => DarkChrome();
        _live.Tick += (_, _) => { foreach (var u in _liveUpdaters.ToList()) { try { u(); } catch (Exception ex) { App.Log(ex); } } };
        _live.Start();
        Closed += (_, _) => { _live.Stop(); _open = null; if (_updateHandler != null) Updater.Changed -= _updateHandler; _island.NotificationsChanged -= OnNotes; _island.WindowsChanged -= OnWindows; };
        _island.NotificationsChanged += OnNotes;
        _island.WindowsChanged += OnWindows;
    }

    void OnNotes() => Dispatcher.InvokeAsync(() => { if (_current is "home" or "notifications" or "shopify") Go(_current, keepScroll: true); });
    void OnWindows() => Dispatcher.InvokeAsync(() => { if (_current == "apps") Go(_current, keepScroll: true); });

    Border NavItem(string id, string glyph, string label)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(Icon(glyph, 15));
        var t = T(label, 13.5, Text, FontWeights.Normal, false);
        t.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var b = new Border { Child = g, Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(10, 1, 10, 1), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = label };
        b.MouseEnter += (_, _) => { if (_current != id) b.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)); };
        b.MouseLeave += (_, _) => { if (_current != id) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, _) => Go(id);
        _navItems[id] = (b, t);
        return b;
    }

    void Responsive()
    {
        bool compact = ActualWidth < 900;
        if (compact == _compact) return;
        _compact = compact;
        _sidebar.Width = compact ? 76 : 248;
        _brand.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (item, label) in _navItems.Values) { label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; item.Margin = compact ? new Thickness(10, 2, 10, 2) : new Thickness(10, 1, 10, 1); }
        var m = compact ? new Thickness(20, 0, 20, 36) : new Thickness(32, 0, 32, 40);
        _page.Margin = m;
        ((FrameworkElement)((StackPanel)_scroll.Content).Children[0]).Margin = compact ? new Thickness(20, 24, 20, 14) : new Thickness(32, 30, 32, 18);
    }

    public void Go(string id, bool keepScroll = false)
    {
        var info = Pages.FirstOrDefault(p => p.id == id);
        if (info.id == null) info = Pages[0];
        _current = info.id;
        foreach (var (k, (item, label)) in _navItems)
        {
            bool sel = k == _current;
            item.Background = sel ? CardHi : Brushes.Transparent;
            label.FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal;
            ((TextBlock)((Grid)item.Child).Children[0]).Foreground = sel ? Accent : Text;
        }
        _title.Text = info.label;
        _subtitle.Text = info.subtitle;
        double offset = _scroll.VerticalOffset;
        _liveUpdaters.Clear();
        _page.Children.Clear();
        try { Build(_current); }
        catch (Exception ex) { App.Log(ex); _page.Children.Add(T("Something went wrong building this page: " + ex.Message, 13, Red)); }
        if (keepScroll) { _scroll.UpdateLayout(); _scroll.ScrollToVerticalOffset(offset); }
        else
        {
            _scroll.ScrollToTop();
            _page.Opacity = 0;
            _page.RenderTransform = new TranslateTransform(0, 8);
            _page.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            ((TranslateTransform)_page.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        foreach (var u in _liveUpdaters.ToList()) { try { u(); } catch { } }
    }

    void Build(string id)
    {
        switch (id)
        {
            case "home": HomePage(); break;
            case "apps": AppsPage(); break;
            case "notifications": NotificationsPage(); break;
            case "shopify": ShopifyPage(); break;
            case "iphone": IPhonePage(); break;
            case "island": IslandPage(); break;
            case "cat": CatPage(); break;
            case "files": FilesPage(); break;
            case "face": FacePage(); break;
            case "servers": ServersPage(); break;
            case "general": GeneralPage(); break;
        }
    }

    void Add(UIElement e) => _page.Children.Add(e);
    void Live(Action a) => _liveUpdaters.Add(a);
    void Save(bool rebuildHome = false) => _island.SettingsChanged(rebuildHome);

    // ───────── window chrome ─────────

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    void DarkChrome()
    {
        var h = new WindowInteropHelper(this).Handle;
        int on = 1; DwmSetWindowAttribute(h, 20, ref on, 4);              // dark title bar
        int caption = 0x00100E0E; DwmSetWindowAttribute(h, 35, ref caption, 4); // caption colour = page background
        int border = 0x002B2626; DwmSetWindowAttribute(h, 34, ref border, 4);
        int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4);
    }
}
