using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DynamicIsland.Taskbar;

namespace DynamicIsland;

/// <summary>
/// The island as your taskbar: a dock of pinned and running apps on the home page, and an Apps page with
/// search, open windows, background apps, recent apps and the Windows buttons (Start, Task view, …).
/// </summary>
public partial class IslandWindow
{
    List<AppWindow> _windows = new();
    readonly HashSet<IntPtr> _attention = new();
    uint _shellMsg;
    readonly DispatcherTimer _dockRefresh = new() { Interval = TimeSpan.FromMilliseconds(140) };
    readonly DispatcherTimer _dockPoll = new() { Interval = TimeSpan.FromSeconds(2.5) };
    PreviewWindow? _preview;
    Popup? _dockMenu;
    public event Action? WindowsChanged;
    public IReadOnlyList<AppWindow> OpenWindows => _windows;

    void StartDock()
    {
        _shellMsg = Taskbar.W.RegisterWindowMessage("SHELLHOOK");
        Taskbar.W.RegisterShellHookWindow(_hwnd);
        _dockRefresh.Tick += (_, _) => { _dockRefresh.Stop(); RefreshWindows(); };
        _dockPoll.Tick += (_, _) => RefreshWindows();
        AppCatalog.PinsChanged += () => Dispatcher.InvokeAsync(RebuildDock);
        AppCatalog.Loaded += () => Dispatcher.InvokeAsync(() => { if (_tab == "apps") RefreshAppsPage(); });
        _ = AppCatalog.LoadAsync();
        RefreshWindows();
        if (_s.HideTaskbar) TaskbarHider.Hide();
    }

    void StopDock()
    {
        try { Taskbar.W.DeregisterShellHookWindow(_hwnd); } catch { }
        _preview?.Close();
        TaskbarHider.Restore();
    }

    public void SetTaskbarHidden(bool hide)
    {
        _s.HideTaskbar = hide; _s.Save();
        if (hide) TaskbarHider.Hide(); else TaskbarHider.Restore();
        if (_tab == "apps") RefreshAppsPage();
    }

    bool DockWndProc(int msg, IntPtr w, IntPtr l)
    {
        if (msg == (int)TaskbarHider.TaskbarCreatedMsg) { TaskbarHider.OnTaskbarCreated(); return false; }
        if (_shellMsg == 0 || msg != (int)_shellMsg) return false;
        int code = w.ToInt32();
        switch (code)
        {
            case 0x8006: // HSHELL_FLASH: an app wants attention (its taskbar button would flash)
                if (!_attention.Contains(l)) { _attention.Add(l); Dispatcher.InvokeAsync(() => Attention(l)); }
                break;
            case 4: case 0x8004: // window activated
                _attention.Remove(l);
                Dispatcher.InvokeAsync(() => NoteActivated(l));
                break;
        }
        if (code is 1 or 2 or 4 or 6 or 13 or 0x8004 or 0x8006) { _dockRefresh.Stop(); _dockRefresh.Start(); }
        return false; // let everyone else see shell messages too
    }

    void NoteActivated(IntPtr h)
    {
        var w = _windows.FirstOrDefault(x => x.Hwnd == h);
        if (w == null && Taskbar.W.IsWindow(h)) { try { w = WindowList.Describe(h); } catch { } }
        if (w != null && w.Launch.Length > 0 && w.Pid != (uint)Environment.ProcessId) AppCatalog.Used(w);
    }

    void Attention(IntPtr h)
    {
        RefreshWindows();
        var w = _windows.FirstOrDefault(x => x.Hwnd == h);
        if (w == null) return;
        ShowToast(new ToastSpec($"{w.AppName} wants your attention", w.Title == w.AppName ? null : w.Title, w.Icon, Seconds: 5,
            OnClick: () => WindowList.Activate(h)));
    }

    void RefreshWindows()
    {
        try
        {
            var fresh = WindowList.List();
            bool changed = fresh.Count != _windows.Count || fresh.Zip(_windows).Any(p => p.First.Hwnd != p.Second.Hwnd || p.First.Title != p.Second.Title || p.First.Minimized != p.Second.Minimized);
            _attention.RemoveWhere(h => !fresh.Any(f => f.Hwnd == h));
            _windows = fresh;
            if (!changed && !_dockDirty) { UpdateDockIndicators(); return; }
            _dockDirty = false;
            RebuildDock();
            if (_tab == "apps" && _mode == Mode.Expanded) RefreshAppsPage(keepSearch: true);
            WindowsChanged?.Invoke();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    bool _dockDirty = true;

    void DockModeChanged(bool expanded)
    {
        if (expanded) { RefreshWindows(); _dockPoll.Start(); }
        else
        {
            _dockPoll.Stop();
            ClosePreview();
            if (_dockMenu != null) _dockMenu.IsOpen = false;
            if (_searchActive) EndSearch(restoreFocus: false);
        }
    }

    // ───────── dock (top of home) ─────────

    sealed class DockApp
    {
        public string Id = "", Name = "", Launch = "", IconPath = "";
        public bool Pinned;
        public List<AppWindow> Windows = new();
    }

    readonly Dictionary<string, (Border item, Border indicator, Border glow)> _dockItems = new();

    List<DockApp> DockApps()
    {
        var apps = new List<DockApp>();
        foreach (var p in AppCatalog.Pinned)
            apps.Add(new DockApp { Id = p.Id, Name = p.Name, Launch = p.Launch, IconPath = p.IconPath, Pinned = true });
        foreach (var w in _windows)
        {
            var a = apps.FirstOrDefault(x => x.Id == w.AppId);
            if (a == null) apps.Add(a = new DockApp { Id = w.AppId, Name = w.AppName, Launch = w.Launch, IconPath = w.IconPath });
            a.Windows.Add(w);
        }
        return apps;
    }

    void RebuildDock()
    {
        _dockItems.Clear();
        if (!_s.DockOnHome) { DockHost.Child = null; DockHost.Margin = new Thickness(0); return; }
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 0) };
        strip.Children.Add(DockButton("", "Apps and search  (Ctrl+Alt+A)", () => SelectTab("apps")));
        var apps = DockApps();
        bool divider = false;
        foreach (var a in apps)
        {
            if (!a.Pinned && !divider && apps.Any(x => x.Pinned))
            {
                strip.Children.Add(new Border { Width = 1, Height = 30, Background = (Brush)Resources["CardEdge"], Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
                divider = true;
            }
            strip.Children.Add(DockItem(a));
        }
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = strip };
        DockHost.Child = new Border
        {
            Background = (Brush)Resources["Card"], BorderBrush = (Brush)Resources["CardEdge"], BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20), Height = 62, Padding = new Thickness(4, 0, 4, 0), Child = scroll,
        };
        DockHost.Margin = new Thickness(0, 0, 0, 8);
        UpdateDockIndicators();
    }

    Border DockButton(string glyph, string tip, Action click)
    {
        var b = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(13), Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x29)), Cursor = Cursors.Hand, ToolTip = tip,
            Child = new TextBlock { Text = glyph, FontFamily = (FontFamily)Resources["Icons"], FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    Border DockItem(DockApp a)
    {
        var scale = new ScaleTransform(1, 1);
        var icon = new Image { Source = ShellIcons.Get(a.IconPath), Width = 30, Height = 30, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var glow = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x9F, 0x0A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), BorderThickness = new Thickness(1.5), Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0),
        };
        var indicator = new Border { Height = 4, Width = 4, CornerRadius = new CornerRadius(2), Background = (Brush)Resources["Dim"], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3), Visibility = Visibility.Collapsed };
        var g = new Grid { Width = 46, Height = 52 };
        g.Children.Add(glow);
        g.Children.Add(new Border { Child = icon, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0) });
        g.Children.Add(indicator);
        var item = new Border { Child = g, Background = Brushes.Transparent, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center, Tag = a };

        item.MouseEnter += (_, _) =>
        {
            var spring = new SpringEase { Damping = 8, Frequency = 13 };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.18, TimeSpan.FromMilliseconds(320)) { EasingFunction = spring });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.18, TimeSpan.FromMilliseconds(320)) { EasingFunction = spring });
            item.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            var app = (DockApp)item.Tag;
            if (_s.DockPreviews && app.Windows.Count > 0) ShowPreview(item, app.Windows[0]);
            else item.ToolTip = app.Name;
        };
        item.MouseLeave += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            item.Background = Brushes.Transparent;
            ClosePreview();
        };
        item.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ClosePreview();
            var app = (DockApp)item.Tag;
            if (app.Windows.Count == 0) { Bounce(icon); WindowList.Launch(app.Launch); return; }
            if (app.Windows.Count == 1) { WindowList.Toggle(app.Windows[0].Hwnd); return; }
            // several windows: cycle through them, starting with the most recent
            var fg = Taskbar.W.GetForegroundWindow();
            int i = app.Windows.FindIndex(w => w.Hwnd == fg);
            WindowList.Activate(app.Windows[(i + 1) % app.Windows.Count].Hwnd);
        };
        item.MouseRightButtonUp += (_, e) => { e.Handled = true; ClosePreview(); ShowDockMenu(item, (DockApp)item.Tag); };
        item.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { e.Handled = true; Bounce(icon); WindowList.Launch(((DockApp)item.Tag).Launch); } };
        _dockItems[a.Id] = (item, indicator, glow);
        return item;
    }

    static void Bounce(UIElement e)
    {
        var t = new TranslateTransform();
        if (e.RenderTransform is ScaleTransform s) e.RenderTransform = new TransformGroup { Children = { s, t } };
        else e.RenderTransform = t;
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(-9, KeyTime.FromPercent(0.25), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new BounceEase { Bounces = 2, Bounciness = 2.5 }));
        t.BeginAnimation(TranslateTransform.YProperty, a);
    }

    void UpdateDockIndicators()
    {
        var fg = Taskbar.W.GetForegroundWindow();
        foreach (var a in DockApps())
        {
            if (!_dockItems.TryGetValue(a.Id, out var v)) { _dockDirty = true; continue; }
            v.item.Tag = a;
            bool running = a.Windows.Count > 0, front = a.Windows.Any(w => w.Hwnd == fg);
            v.indicator.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            v.indicator.Width = front ? 14 : a.Windows.Count > 1 ? 9 : 4;
            v.indicator.Background = front ? (Brush)Resources["Accent"] : (Brush)Resources["Dim"];
            bool wants = a.Windows.Any(w => _attention.Contains(w.Hwnd));
            if (wants && v.glow.Visibility != Visibility.Visible)
            {
                v.glow.Visibility = Visibility.Visible;
                v.glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }
            else if (!wants && v.glow.Visibility == Visibility.Visible) { v.glow.BeginAnimation(OpacityProperty, null); v.glow.Visibility = Visibility.Collapsed; }
        }
    }

    // ───────── preview + menu ─────────

    void ShowPreview(FrameworkElement anchor, AppWindow w)
    {
        try
        {
            _preview ??= new PreviewWindow();
            var src = PresentationSource.FromVisual(anchor);
            if (src?.CompositionTarget == null) return;
            var p = src.CompositionTarget.TransformFromDevice.Transform(anchor.PointToScreen(new Point(anchor.ActualWidth / 2, anchor.ActualHeight)));
            var islandBottom = IslandScreenRect()?.Bottom ?? p.Y;
            _preview.ShowFor(w, new Point(p.X, Math.Max(p.Y, islandBottom)));
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void ClosePreview() { try { _preview?.HidePreview(); } catch { } }

    void ShowDockMenu(FrameworkElement anchor, DockApp a)
    {
        var list = new StackPanel { Margin = new Thickness(6) };
        void Item(string glyph, string text, Action act, Brush? fg = null)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)Resources["Icons"], FontSize = 12.5, Foreground = fg ?? Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            var t = new TextBlock { Text = text, FontSize = 12.5, Foreground = fg ?? Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 1); row.Children.Add(t);
            var b = new Border { Child = row, Padding = new Thickness(10, 6, 12, 6), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (_dockMenu != null) _dockMenu.IsOpen = false; try { act(); } catch (Exception ex) { App.Log(ex); } };
            list.Children.Add(b);
        }
        void Sep() => list.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(8, 4, 8, 4) });

        list.Children.Add(new TextBlock { Text = a.Name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Resources["Dim"], Margin = new Thickness(10, 4, 10, 4), TextTrimming = TextTrimming.CharacterEllipsis });
        foreach (var w in a.Windows.Take(8)) Item(w.Minimized ? "" : "", w.Title, () => WindowList.Activate(w.Hwnd));
        if (a.Windows.Count > 0) Sep();
        if (a.Launch.Length > 0) Item("", a.Windows.Count > 0 ? "New window" : "Open", () => WindowList.Launch(a.Launch));
        if (AppCatalog.IsPinned(a.Id)) Item("", "Unpin from island", () => AppCatalog.Unpin(a.Id));
        else if (a.Launch.Length > 0) Item("", "Pin to island", () => AppCatalog.Pin(new AppRef { Id = a.Id, Name = a.Name, Launch = a.Launch, IconPath = a.IconPath }));
        if (AppCatalog.IsPinned(a.Id))
        {
            Item("", "Move left", () => AppCatalog.MovePin(a.Id, -1));
            Item("", "Move right", () => AppCatalog.MovePin(a.Id, 1));
        }
        if (a.Windows.Count > 0)
        {
            Sep();
            Item("", a.Windows.Count > 1 ? "Minimise all" : "Minimise", () => { foreach (var w in a.Windows) WindowList.Minimize(w.Hwnd); });
            Item("", a.Windows.Count > 1 ? $"Close all {a.Windows.Count} windows" : "Close window", () => { foreach (var w in a.Windows) WindowList.Close(w.Hwnd); }, new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x61)));
        }

        _dockMenu ??= new Popup { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, PopupAnimation = PopupAnimation.Fade };
        _dockMenu.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x20)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), MinWidth = 210, MaxWidth = 300, Child = list, Margin = new Thickness(10),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.5 },
        };
        _dockMenu.PlacementTarget = anchor;
        _dockMenu.IsOpen = true;
    }

    // ───────── Apps page ─────────

    TextBox? _search;
    WrapPanel? _results;
    bool _searchActive;
    string _lastQuery = "";

    public void OpenAppSearch()
    {
        if (_hidden) ToggleHidden();
        SelectTab("apps");
        if (_mode != Mode.Expanded) { _st.TogglePin(DateTime.Now); Reconcile(); }
        BeginSearch();
    }

    void BeginSearch()
    {
        if (_search == null) return;
        _searchActive = true;
        SetKeyboard(true);
        _search.Focus(); Keyboard.Focus(_search);
    }

    void EndSearch(bool restoreFocus = true)
    {
        _searchActive = false;
        SetKeyboard(false, restoreFocus);
    }

    void RefreshAppsPage(bool keepSearch = false)
    {
        var q = keepSearch ? _search?.Text ?? "" : _lastQuery;
        var root = new StackPanel();

        // Windows buttons
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        tools.Children.Add(Tool("", "Start", WindowList.Start));
        tools.Children.Add(Tool("", "Task view", WindowList.TaskView));
        tools.Children.Add(Tool("", "Desktop", WindowList.ShowDesktop));
        tools.Children.Add(Tool("", "Alerts", WindowList.NotificationCenter));
        tools.Children.Add(Tool("", "Quick settings", WindowList.QuickSettings));
        if (TaskbarHider.Hidden) tools.Children.Add(Tool("", "Tray icons", () => TaskbarHider.Peek()));
        tools.Children.Add(Tool("\uE713", "Cat Center", () => Center.CenterWindow.Open(this)));
        tools.Children.Add(Tool(TaskbarHider.Hidden ? "" : "", TaskbarHider.Hidden ? "Show taskbar" : "Hide taskbar", () => SetTaskbarHidden(!TaskbarHider.Hidden)));
        root.Children.Add(tools);

        // search box
        var keepText = _search?.Text ?? q;
        _search = new TextBox
        {
            Text = keepText, FontSize = 13.5, Foreground = Brushes.White, CaretBrush = (Brush)Resources["Accent"], Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0),
        };
        var hint = new TextBlock { Text = "Search apps…", Foreground = (Brush)Resources["Faint"], FontSize = 13.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Visibility = keepText.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
        var sg = new Grid();
        sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sg.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)Resources["Icons"], FontSize = 13, Foreground = (Brush)Resources["Dim"], VerticalAlignment = VerticalAlignment.Center });
        var inner = new Grid(); inner.Children.Add(hint); inner.Children.Add(_search);
        Grid.SetColumn(inner, 1); sg.Children.Add(inner);
        var box = new Border { Background = (Brush)Resources["Card"], BorderBrush = (Brush)Resources["CardEdge"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 9, 12, 9), Child = sg, Cursor = Cursors.IBeam };
        box.PreviewMouseDown += (_, _) => { if (!_searchActive) BeginSearch(); };
        _search.TextChanged += (_, _) => { hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _lastQuery = _search.Text; FillResults(); };
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { if (_search.Text.Length > 0) _search.Text = ""; else EndSearch(); e.Handled = true; }
            else if (e.Key == Key.Enter)
            {
                var first = AppCatalog.Search(_search.Text, 1).FirstOrDefault();
                if (first != null) { LaunchFromSearch(first); e.Handled = true; }
            }
        };
        root.Children.Add(box);

        _results = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        root.Children.Add(_results);
        FillResults();

        // open windows
        if (_windows.Count > 0)
        {
            root.Children.Add(SectionLabel($"OPEN WINDOWS · {_windows.Count}"));
            foreach (var w in _windows) root.Children.Add(WindowRow(w));
        }

        // background apps
        var bgHost = new StackPanel();
        root.Children.Add(bgHost);
        _ = FillBackgroundAsync(bgHost);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = root };
        AppsPanel.Children.Clear();
        AppsPanel.Children.Add(scroll);
        if (_searchActive) { _search.Focus(); _search.CaretIndex = _search.Text.Length; }
    }

    void FillResults()
    {
        if (_results == null || _search == null) return;
        _results.Children.Clear();
        var q = _search.Text.Trim();
        if (q.Length > 0)
        {
            var hits = AppCatalog.Search(q, 8);
            if (hits.Count == 0) _results.Children.Add(new TextBlock { Text = AppCatalog.All.Count == 0 ? "Loading your apps…" : "No apps match", Foreground = (Brush)Resources["Dim"], FontSize = 12.5, Margin = new Thickness(4, 2, 0, 4) });
            foreach (var e in hits) _results.Children.Add(AppTile(e.Name, e.Launch, () => LaunchFromSearch(e), AppCatalog.From(e)));
            return;
        }
        if (AppCatalog.Pinned.Count > 0)
        {
            _results.Children.Add(Full(SectionLabel("PINNED")));
            foreach (var p in AppCatalog.Pinned) { var pp = p; _results.Children.Add(AppTile(p.Name, p.IconPath, () => WindowList.Launch(pp.Launch), pp)); }
        }
        var recent = AppCatalog.Recent.Where(r => !AppCatalog.IsPinned(r.Id)).Take(8).ToList();
        if (recent.Count > 0)
        {
            _results.Children.Add(Full(SectionLabel("RECENT")));
            foreach (var r in recent) { var rr = r; _results.Children.Add(AppTile(r.Name, r.IconPath, () => WindowList.Launch(rr.Launch), rr)); }
        }
    }

    UIElement Full(FrameworkElement e) { e.Width = 430; return e; }

    void LaunchFromSearch(AppCatalog.Entry e)
    {
        WindowList.Launch(e.Launch);
        if (_search != null) _search.Text = "";
        EndSearch(restoreFocus: false);
        ShowToast(new ToastSpec("Opening " + e.Name, null, ShellIcons.Get(e.Launch), Seconds: 2));
    }

    Border AppTile(string name, string iconPath, Action open, AppRef? pin)
    {
        var st = new StackPanel();
        var img = new Image { Source = ShellIcons.Get(iconPath), Width = 32, Height = 32, Margin = new Thickness(0, 8, 0, 4) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        st.Children.Add(img);
        st.Children.Add(new TextBlock { Text = name, FontSize = 11, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxHeight = 30, Margin = new Thickness(4, 0, 4, 6) });
        var b = new Border { Width = 84, Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = st, ToolTip = name };
        b.MouseEnter += (_, _) => b.Background = (Brush)Resources["CardHover"];
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; open(); };
        if (pin != null)
            b.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowDockMenu(b, new DockApp { Id = pin.Id, Name = pin.Name, Launch = pin.Launch, IconPath = pin.IconPath, Windows = _windows.Where(w => w.AppId == pin.Id).ToList() });
            };
        return b;
    }

    FrameworkElement SectionLabel(string t) => new TextBlock { Text = t, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Resources["Dim"], Margin = new Thickness(2, 12, 0, 6) };

    Border Tool(string glyph, string label, Action act)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)Resources["Icons"], FontSize = 12.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        var b = new Border { Child = sp, Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(11), Background = (Brush)Resources["Card"], BorderBrush = (Brush)Resources["CardEdge"], BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => b.Background = (Brush)Resources["CardHover"];
        b.MouseLeave += (_, _) => b.Background = (Brush)Resources["Card"];
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; try { act(); } catch (Exception ex) { App.Log(ex); } };
        return b;
    }

    Border WindowRow(AppWindow w)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var img = new Image { Source = w.Icon, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        g.Children.Add(img);
        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(new TextBlock { Text = w.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        txt.Children.Add(new TextBlock { Text = w.AppName + (w.Minimized ? " · minimised" : "") + (_attention.Contains(w.Hwnd) ? " · wants attention" : ""), FontSize = 11, Foreground = _attention.Contains(w.Hwnd) ? new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)) : (Brush)Resources["Dim"], TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(txt, 1); g.Children.Add(txt);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Opacity = 0.0, Margin = new Thickness(6, 0, 0, 0) };
        btns.Children.Add(Mini("", "Minimise", () => WindowList.Minimize(w.Hwnd)));
        btns.Children.Add(Mini("", "Close", () => { WindowList.Close(w.Hwnd); _dockRefresh.Start(); }));
        Grid.SetColumn(btns, 2); g.Children.Add(btns);
        var row = new Border { Child = g, Padding = new Thickness(10, 7, 6, 7), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(12), Background = (Brush)Resources["Card"], Cursor = Cursors.Hand };
        row.MouseEnter += (_, _) => { row.Background = (Brush)Resources["CardHover"]; btns.Opacity = 1; if (_s.DockPreviews) ShowPreview(row, w); };
        row.MouseLeave += (_, _) => { row.Background = (Brush)Resources["Card"]; btns.Opacity = 0; ClosePreview(); };
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; ClosePreview(); WindowList.Activate(w.Hwnd); };
        return row;
    }

    Border Mini(string glyph, string tip, Action act)
    {
        var b = new Border { Width = 28, Height = 26, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip,
            Child = new TextBlock { Text = glyph, FontFamily = (FontFamily)Resources["Icons"], FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; act(); };
        return b;
    }

    async Task FillBackgroundAsync(StackPanel host)
    {
        var windows = _windows.ToList();
        List<BackgroundApps.Item> items;
        try { items = await Task.Run(() => BackgroundApps.List(windows)); }
        catch (Exception ex) { App.Log(ex); return; }
        if (items.Count == 0) return;
        host.Children.Add(SectionLabel($"RUNNING IN THE BACKGROUND · {items.Count}"));
        foreach (var it in items)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new Image { Source = ShellIcons.Get(it.Exe), Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBlock { Text = it.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1); g.Children.Add(name);
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var item = it;
            var row = new Border { Child = g, Padding = new Thickness(10, 6, 6, 6), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(12), Background = (Brush)Resources["Card"] };
            btns.Children.Add(SmallChip("Open", () => BackgroundApps.Open(item)));
            btns.Children.Add(SmallChip("Quit", async () =>
            {
                name.Text = item.Name + " · quitting…";
                if (await BackgroundApps.Quit(item)) { host.Children.Remove(row); return; }
                name.Text = item.Name + " · didn't quit";
                btns.Children.Clear();
                btns.Children.Add(SmallChip("Force quit", () => { BackgroundApps.ForceQuit(item); host.Children.Remove(row); }, danger: true));
            }));
            Grid.SetColumn(btns, 2); g.Children.Add(btns);
            host.Children.Add(row);
        }
    }

    Border SmallChip(string label, Action act, bool danger = false)
    {
        var b = new Border
        {
            Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand,
            Background = danger ? new SolidColorBrush(Color.FromRgb(0x3A, 0x1C, 0x1C)) : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30)),
            Child = new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = danger ? new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0x61)) : Brushes.White },
        };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; act(); };
        return b;
    }
}
