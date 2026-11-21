using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>Widget home, page navigation, cat ears and the bridge to the desktop cat.</summary>
public partial class IslandWindow
{
    // ───────── events the desktop cat listens to ─────────
    public event Action? TimerFinished;
    public event Action<NotificationItem>? NotificationShown;
    public event Action<string, int>? PowerEvent;

    // ───────── things the desktop cat can ask for ─────────
    public void StartFocus(int minutes)
    {
        BeginFocus(minutes);
        ShowToast(new ToastSpec($"{minutes}-minute focus started", "The cat will let you know when it's done", Glyph: "", Seconds: 2.5));
    }

    public void TogglePlayback() => _media.TogglePlay();

    public void OpenIsland(string page = "home")
    {
        if (_hidden) ToggleHidden();
        SelectTab(page);
        if (!_st.Pinned) _st.TogglePin(DateTime.Now); // stays open until you've been away for a bit
        Reconcile();
    }

    public void AddToShelf(string[] paths)
    {
        _shelf.Add(paths);
        if (_tab == "shelf") RefreshShelf();
        UpdateHome();
    }

    // ───────── navigation ─────────

    void Back_Click(object sender, RoutedEventArgs e) { SelectTab("home"); e.Handled = true; }

    static string TitleFor(string tab) => tab switch
    {
        "media" => "Now playing",
        "system" => "System",
        "sound" => "Sound and display",
        "alerts" => "Notifications",
        "timer" => "Focus timer",
        "shelf" => "Shelf",
        "brain" => "Hermes",
        "apps" => "Apps",
        _ => Greeting(),
    };

    static string Greeting()
    {
        int h = DateTime.Now.Hour;
        return h < 5 ? "Night owl mode" : h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : h < 22 ? "Good evening" : "Late night";
    }

    void AnimatePages()
    {
        if (_mode != Mode.Expanded) return;
        var d = TimeSpan.FromMilliseconds(150);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PagesShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_tab == "home" ? -10 : 10, 0, d) { EasingFunction = ease });
        Pages.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, d));
    }

    // ───────── cat ears ─────────

    void PlaceEars()
    {
        double h = Island.ActualHeight;
        double radius = Math.Min(h / 2, 32);
        double inset = Math.Max(8, radius * 0.5);
        EarL.Margin = new Thickness(inset, 0, 0, 0);
        EarR.Margin = new Thickness(0, 0, inset, 0);
        double scale = h > 60 ? 1.18 : 1;
        EarLScale.ScaleX = EarLScale.ScaleY = EarRScale.ScaleX = EarRScale.ScaleY = scale;
    }

    readonly DispatcherTimer _earTimer = new();

    void StartEarTwitch()
    {
        _earTimer.Tick += (_, _) =>
        {
            _earTimer.Interval = TimeSpan.FromSeconds(Random.Shared.Next(5, 13));
            bool left = Random.Shared.Next(2) == 0;
            var rot = left ? EarLRot : EarRRot;
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(left ? -16 : 16, KeyTime.FromPercent(0.35)));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new SpringEase { Damping = 7, Frequency = 14 }));
            rot.BeginAnimation(RotateTransform.AngleProperty, a);
        };
        _earTimer.Interval = TimeSpan.FromSeconds(4);
        _earTimer.Start();
    }

    // ───────── widget home ─────────

    readonly List<Action> _homeUpdaters = new();
    static readonly string[] KnownWidgets = { "media", "iphone", "system", "battery", "sound", "alerts", "timer", "shelf", "servers" };

    void BuildHome()
    {
        HomeView.Children.Clear();
        HomeView.RowDefinitions.Clear();
        _homeUpdaters.Clear();
        var ids = (_s.Widgets ?? new()).Select(w => w.ToLowerInvariant()).Where(KnownWidgets.Contains).Distinct().ToList();
        if (ids.Count == 0) ids = new() { "media", "system", "battery", "sound", "alerts", "timer", "shelf" };

        int row = 0, col = 0;
        void EnsureRow() { while (HomeView.RowDefinitions.Count <= row) HomeView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
        foreach (var id in ids)
        {
            bool wide = id == "media";
            if (wide && col == 1) { row++; col = 0; }
            EnsureRow();
            var card = MakeWidget(id);
            Grid.SetRow(card, row);
            Grid.SetColumn(card, col);
            if (wide) { Grid.SetColumnSpan(card, 2); card.Margin = new Thickness(0, 0, 0, 8); }
            else card.Margin = col == 0 ? new Thickness(0, 0, 4, 8) : new Thickness(4, 0, 0, 8);
            HomeView.Children.Add(card);
            if (wide) { row++; col = 0; } else if (++col == 2) { col = 0; row++; }
        }
        UpdateHome();
    }

    void UpdateHome() { foreach (var u in _homeUpdaters) { try { u(); } catch (Exception ex) { App.Log(ex); } } }

    Border Card(string target, double height)
    {
        var scale = new ScaleTransform(1, 1);
        var card = new Border
        {
            Background = (Brush)Resources["Card"],
            BorderBrush = (Brush)Resources["CardEdge"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(12, 10, 12, 10),
            Height = height,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        card.MouseEnter += (_, _) =>
        {
            card.Background = (Brush)Resources["CardHover"];
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.025, TimeSpan.FromMilliseconds(110)) { EasingFunction = new CubicEase() });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.025, TimeSpan.FromMilliseconds(110)) { EasingFunction = new CubicEase() });
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = (Brush)Resources["Card"];
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        };
        card.MouseLeftButtonUp += (_, e) => { if (e.Handled) return; e.Handled = true; SelectTab(target); };
        return card;
    }

    /// Standard small widget: icon/ring on the left, title + one-line status on the right.
    Border Small(string target, UIElement lead, string title, out TextBlock sub, out TextBlock titleBlock)
    {
        var card = Card(target, 66);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(lead);
        var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        titleBlock = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        sub = new TextBlock { FontSize = 11.5, Foreground = (Brush)Resources["Dim"], TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        st.Children.Add(titleBlock);
        st.Children.Add(sub);
        Grid.SetColumn(st, 1);
        g.Children.Add(st);
        card.Child = g;
        return card;
    }

    Grid Bubble(string glyph, out TextBlock icon)
    {
        var g = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(20), Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x29)) });
        icon = new TextBlock { FontFamily = (FontFamily)Resources["Icons"], Text = glyph, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(icon);
        return g;
    }

    Grid RingLead(out Ring ring, out TextBlock center)
    {
        var g = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center };
        ring = new Ring { Thickness = 4, Stroke = (Brush)Resources["Accent"] };
        center = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(ring);
        g.Children.Add(center);
        return g;
    }

    Border MakeWidget(string id)
    {
        switch (id)
        {
            case "media": return MediaWidget();
            case "iphone": return PhoneWidget();
            case "system":
            {
                var lead = RingLead(out var ring, out var c);
                var card = Small("system", lead, "System", out var sub, out _);
                _homeUpdaters.Add(() =>
                {
                    ring.Value = _stats.Cpu / 100.0; c.Text = $"{_stats.Cpu:0}";
                    sub.Text = _stats.Gpu >= 0 ? $"CPU {_stats.Cpu:0}% · RAM {_stats.RamPct:0}% · GPU {_stats.Gpu:0}%" : $"CPU {_stats.Cpu:0}% · RAM {_stats.RamPct:0}%";
                });
                return card;
            }
            case "battery":
            {
                var lead = RingLead(out var ring, out var c);
                var card = Small("system", lead, "Battery", out var sub, out _);
                _homeUpdaters.Add(() =>
                {
                    var p = _power;
                    if (!p.HasBattery) { ring.Value = 1; c.Text = "AC"; sub.Text = "Plugged in"; return; }
                    ring.Value = Math.Max(0, p.Percent) / 100.0;
                    ring.Stroke = p.Charging || p.OnAC ? (Brush)Resources["Accent"] : p.Percent <= 20 ? Red : Brushes.White;
                    c.Text = p.Charging ? "⚡" : $"{p.Percent}";
                    sub.Text = p.Charging ? $"{p.Percent}% · charging" : p.OnAC ? $"{p.Percent}% · plugged in"
                        : p.Remaining is TimeSpan r ? $"{(int)r.TotalHours}h {r.Minutes:00}m left" : $"{p.Percent}%";
                });
                return card;
            }
            case "sound":
            {
                var lead = Bubble("", out var icon);
                var card = Small("sound", lead, "Sound", out var sub, out _);
                _homeUpdaters.Add(() =>
                {
                    bool m = _audio.MasterMuted;
                    icon.Text = m ? "" : "";
                    var dev = _audio.DeviceName ?? "No output";
                    if (dev.Length > 18) dev = dev[..18] + "…";
                    sub.Text = m ? $"Muted · {dev}" : $"{Math.Round(_audio.MasterVolume * 100)}% · {dev}";
                });
                return card;
            }
            case "alerts":
            {
                var lead = Bubble("", out var icon);
                var card = Small("alerts", lead, "Alerts", out var sub, out _);
                _homeUpdaters.Add(() =>
                {
                    int n = _notes.History.Count;
                    icon.Foreground = n > 0 ? (Brush)Resources["Accent"] : Brushes.White;
                    sub.Text = !_s.Notifications ? "Turned off" : _notes.Status == "denied" ? "Needs access" : n == 0 ? "All caught up" : n == 1 ? $"1 from {_notes.History[0].App}" : $"{n} notifications";
                });
                return card;
            }
            case "timer":
            {
                var lead = RingLead(out var ring, out var c);
                c.FontFamily = (FontFamily)Resources["Icons"]; c.Text = ""; c.FontSize = 13;
                var card = Small("timer", lead, "Focus", out var sub, out _);
                _homeUpdaters.Add(() =>
                {
                    if (_timerEnd is DateTime end)
                    {
                        var rem = end - DateTime.Now;
                        ring.Value = Math.Clamp(1 - rem / _timerTotal, 0, 1);
                        sub.Text = $"{Fmt(rem < TimeSpan.Zero ? TimeSpan.Zero : rem)} left";
                    }
                    else { ring.Value = 0; sub.Text = "Start a session"; }
                });
                return card;
            }
            case "shelf":
            {
                var lead = Bubble("", out _);
                var card = Small("shelf", lead, "Shelf", out var sub, out _);
                _homeUpdaters.Add(() =>
                {
                    int f = _shelf.Paths.Count, c = _clip.Items.Count;
                    sub.Text = f == 0 && c == 0 ? "Drop files here" : $"{f} file{(f == 1 ? "" : "s")} · {c} clip{(c == 1 ? "" : "s")}";
                });
                return card;
            }
            case "servers":
            {
                var lead = Bubble("", out var icon);
                var card = Small("system", lead, "Servers", out var sub, out _);
                _homeUpdaters.Add(() =>
                {
                    var r = _monitors.Results;
                    int down = r.Count(x => !x.Up);
                    icon.Foreground = r.Count == 0 ? Brushes.White : down > 0 ? Red : (Brush)Resources["Accent"];
                    sub.Text = r.Count == 0 ? "None configured" : down > 0 ? $"{down} down" : $"All up · {r.Min(x => x.Ms)} ms";
                });
                return card;
            }
        }
        return Card("home", 66);
    }

    Border MediaWidget()
    {
        var card = Card("media", 84);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var art = new Border { Width = 60, Height = 60, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x29)) };
        var sleepy = new CatFace { Width = 40, Height = 34, Mood = FaceMood.Sleep };
        art.Child = sleepy;
        g.Children.Add(art);

        var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        var title = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var sub = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        var track = new Grid { Height = 3, Margin = new Thickness(0, 8, 0, 0) };
        track.Children.Add(new Border { Background = (Brush)Resources["Track"], CornerRadius = new CornerRadius(1.5) });
        var fillScale = new ScaleTransform(0, 1);
        track.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(1.5), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = fillScale });
        st.Children.Add(title); st.Children.Add(sub); st.Children.Add(track);
        Grid.SetColumn(st, 1); g.Children.Add(st);

        var play = new Button { Style = (Style)Resources["IconBtn"], Content = "", FontSize = 17, Width = 42, Height = 42 };
        play.Click += (_, e) => { e.Handled = true; _media.TogglePlay(); };
        Grid.SetColumn(play, 2); g.Children.Add(play);
        card.Child = g;

        ImageSource? shownArt = null;
        _homeUpdaters.Add(() =>
        {
            var m = _media.Current;
            bool has = m != null && !string.IsNullOrWhiteSpace(m.Title);
            play.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            track.Visibility = has && m!.Duration > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
            if (!has)
            {
                title.Text = "Nothing playing";
                sub.Text = "The cat is napping until the music starts";
                sub.Foreground = (Brush)Resources["Dim"];
                if (shownArt != null) { shownArt = null; art.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x29)); art.Child = sleepy; }
                sleepy.Mood = FaceMood.Sleep;
                return;
            }
            title.Text = m!.Title;
            var line = _s.LyricsInPill ? CurrentLyricLine() : null;
            sub.Text = line ?? (string.IsNullOrEmpty(m.Artist) ? m.App : m.Artist);
            sub.Foreground = line != null ? (Brush)Resources["Accent"] : (Brush)Resources["Dim"];
            play.Content = m.IsPlaying ? "" : "";
            if (!ReferenceEquals(shownArt, m.Art))
            {
                shownArt = m.Art;
                if (m.Art != null) { art.Background = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill }; art.Child = null; }
                else { art.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x29)); art.Child = sleepy; sleepy.Mood = FaceMood.Content; }
            }
            fillScale.ScaleX = m.Duration > TimeSpan.Zero ? Math.Clamp(_media.EstimatedPosition / m.Duration, 0, 1) : 0;
        });
        return card;
    }
}
