using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DynamicIsland.Center;

/// <summary>
/// Cat Center's building blocks. Every row puts text in a star-sized column that wraps and the control in an
/// auto column next to it, so nothing overlaps at any window size; tile grids reflow their column count.
/// </summary>
public static class Ui
{
    public static readonly Color AccentColor = Color.FromRgb(0xFF, 0xB3, 0x7A);
    public static readonly Brush Bg = B(0x0E, 0x0E, 0x10), Side = B(0x14, 0x14, 0x17), Card = B(0x18, 0x18, 0x1B), CardHi = B(0x20, 0x20, 0x24),
        Edge = B(0x26, 0x26, 0x2B), Field = B(0x10, 0x10, 0x12), Text = B(0xF2, 0xF2, 0xF4), Dim = B(0x9A, 0x9A, 0xA2), Faint = B(0x62, 0x62, 0x69),
        Accent = new SolidColorBrush(AccentColor), OnAccent = B(0x1A, 0x10, 0x08), Green = B(0x30, 0xD1, 0x58), Red = B(0xFF, 0x45, 0x3A),
        Yellow = B(0xFF, 0xD6, 0x0A), ShopifyGreen = B(0x95, 0xBF, 0x47), Blue = B(0x0A, 0x84, 0xFF);
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");

    static Brush B(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    // ───────── text ─────────

    public static TextBlock T(string text, double size = 13, Brush? fg = null, FontWeight? w = null, bool wrap = true, Thickness? m = null) => new()
    {
        Text = text, FontSize = size, Foreground = fg ?? Text, FontWeight = w ?? FontWeights.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = m ?? new Thickness(0),
    };

    public static TextBlock Icon(string glyph, double size = 16, Brush? fg = null) => new()
    {
        Text = glyph, FontFamily = Icons, FontSize = size, Foreground = fg ?? Text,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
    };

    public static TextBlock Caps(string text) => T(text.ToUpperInvariant(), 11, Dim, FontWeights.SemiBold, false, new Thickness(4, 22, 0, 8));

    // ───────── containers ─────────

    public static Border Section(params UIElement[] rows)
    {
        var sp = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) sp.Children.Add(new Border { Height = 1, Background = Edge, Margin = new Thickness(16, 0, 16, 0) });
            sp.Children.Add(rows[i]);
        }
        return new Border { Background = Card, CornerRadius = new CornerRadius(16), BorderBrush = Edge, BorderThickness = new Thickness(1), Child = sp };
    }

    public static Border Box(UIElement child, Thickness? pad = null) => new()
    {
        Background = Card, CornerRadius = new CornerRadius(16), BorderBrush = Edge, BorderThickness = new Thickness(1),
        Padding = pad ?? new Thickness(18, 16, 18, 16), Child = child,
    };

    /// Label (+ optional description) on the left, wrapping; control on the right.
    public static Grid Row(string label, string? desc, UIElement? control, string? glyph = null)
    {
        var g = new Grid { Margin = new Thickness(16, 13, 16, 13), MinHeight = 34 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (glyph != null)
        {
            var ic = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = CardHi, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Child = Icon(glyph, 15) };
            g.Children.Add(ic);
        }
        var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        st.Children.Add(T(label, 13.5, Text, FontWeights.SemiBold));
        if (!string.IsNullOrEmpty(desc)) st.Children.Add(T(desc, 12, Dim, null, true, new Thickness(0, 2, 0, 0)));
        Grid.SetColumn(st, 1);
        g.Children.Add(st);
        if (control is FrameworkElement fe)
        {
            fe.Margin = new Thickness(16, 0, 0, 0);
            fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(fe, 2);
            g.Children.Add(fe);
        }
        return g;
    }

    /// Same as Row, but the control drops below the text (for wide controls like text boxes).
    public static StackPanel Stacked(string label, string? desc, UIElement control)
    {
        var st = new StackPanel { Margin = new Thickness(16, 13, 16, 14) };
        st.Children.Add(T(label, 13.5, Text, FontWeights.SemiBold));
        if (!string.IsNullOrEmpty(desc)) st.Children.Add(T(desc, 12, Dim, null, true, new Thickness(0, 2, 0, 0)));
        if (control is FrameworkElement fe) fe.Margin = new Thickness(0, 10, 0, 0);
        st.Children.Add(control);
        return st;
    }

    public static UIElement Pad(UIElement e, double l = 16, double t = 12, double r = 16, double b = 12)
    {
        if (e is FrameworkElement fe) fe.Margin = new Thickness(l, t, r, b);
        return e;
    }

    // ───────── controls ─────────

    public static Border Button(string label, Action onClick, bool primary = false, string? glyph = null, bool danger = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var fg = primary ? OnAccent : danger ? Red : Text;
        if (glyph != null) sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 13, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        sp.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
        var normal = primary ? Accent : CardHi;
        var b = new Border
        {
            Background = normal, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8), Cursor = Cursors.Hand, Child = sp,
            BorderBrush = primary ? null : Edge, BorderThickness = new Thickness(primary ? 0 : 1), Focusable = true,
        };
        var hover = primary ? new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x96)) : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x30));
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => { b.Background = normal; b.RenderTransform = null; };
        b.MouseLeftButtonDown += (_, e) => { b.RenderTransformOrigin = new Point(0.5, 0.5); b.RenderTransform = new ScaleTransform(0.97, 0.97); e.Handled = true; };
        b.MouseLeftButtonUp += (_, e) =>
        {
            b.RenderTransform = null; e.Handled = true;
            try { onClick(); } catch (Exception ex) { App.Log(ex); }
        };
        b.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) onClick(); };
        return b;
    }

    public static WrapPanel Buttons(params Border[] buttons)
    {
        var w = new WrapPanel();
        foreach (var b in buttons) { b.Margin = new Thickness(0, 0, 8, 8); w.Children.Add(b); }
        return w;
    }

    /// iOS-style switch.
    public static Border Toggle(bool on, Action<bool> changed)
    {
        var knob = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3), RenderTransform = new TranslateTransform(on ? 18 : 0, 0) };
        var track = new Border { Width = 42, Height = 24, CornerRadius = new CornerRadius(12), Background = on ? Accent : B(0x3A, 0x3A, 0x40), Child = knob, Cursor = Cursors.Hand };
        track.Tag = on;
        track.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            bool v = !(bool)track.Tag; track.Tag = v;
            ((TranslateTransform)knob.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(v ? 18 : 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new SpringEase { Damping = 9, Frequency = 14 } });
            track.Background = v ? Accent : B(0x3A, 0x3A, 0x40);
            try { changed(v); } catch (Exception ex) { App.Log(ex); }
        };
        return track;
    }

    /// Segmented control: a row of options, one selected.
    public static Border Segmented(IEnumerable<(string value, string label)> options, string selected, Action<string> changed)
    {
        var panel = new WrapPanel();
        var host = new Border { Background = Field, CornerRadius = new CornerRadius(11), Padding = new Thickness(3), BorderBrush = Edge, BorderThickness = new Thickness(1), Child = panel, HorizontalAlignment = HorizontalAlignment.Left };
        var items = new List<(string v, Border b, TextBlock t)>();
        void Select(string v)
        {
            foreach (var (iv, b, t) in items)
            {
                bool sel = iv == v;
                b.Background = sel ? CardHi : Brushes.Transparent;
                b.BorderBrush = sel ? Edge : Brushes.Transparent;
                t.Foreground = sel ? Text : Dim;
            }
        }
        foreach (var (v, label) in options)
        {
            var t = new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            var b = new Border { Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(8), Child = t, Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
            var value = v;
            b.MouseLeftButtonUp += (_, e) => { e.Handled = true; Select(value); try { changed(value); } catch (Exception ex) { App.Log(ex); } };
            items.Add((v, b, t));
            panel.Children.Add(b);
        }
        Select(selected);
        return host;
    }

    public static TextBox Input(string text, Action<string>? changed = null, double width = double.NaN, string? placeholder = null, bool mono = false)
    {
        var tb = new TextBox
        {
            Text = text, Width = width, MinWidth = 80, FontSize = 13, Foreground = Text, Background = Field, CaretBrush = Accent,
            BorderBrush = Edge, BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), SelectionBrush = Accent,
            FontFamily = mono ? new FontFamily("Cascadia Mono, Consolas") : Font, VerticalContentAlignment = VerticalAlignment.Center,
        };
        tb.Template = RoundedTextBox();
        if (placeholder != null) Placeholder(tb, placeholder);
        if (changed != null) tb.TextChanged += (_, _) => changed(tb.Text);
        tb.GotKeyboardFocus += (_, _) => tb.BorderBrush = Accent;
        tb.LostKeyboardFocus += (_, _) => tb.BorderBrush = Edge;
        return tb;
    }

    static ControlTemplate? _tbTemplate;
    static ControlTemplate RoundedTextBox()
    {
        if (_tbTemplate != null) return _tbTemplate;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetBinding(Control.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        border.AppendChild(host);
        _tbTemplate = new ControlTemplate(typeof(TextBox)) { VisualTree = border };
        return _tbTemplate;
    }

    static void Placeholder(TextBox tb, string text)
    {
        var hint = new VisualBrush(new TextBlock { Text = text, Foreground = Faint, FontSize = 13, Margin = new Thickness(11, 0, 0, 0) })
        { AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Center, Stretch = Stretch.None };
        void Upd() => tb.Background = string.IsNullOrEmpty(tb.Text) && !tb.IsKeyboardFocused ? hint : Field;
        tb.TextChanged += (_, _) => Upd();
        tb.GotKeyboardFocus += (_, _) => Upd();
        tb.LostKeyboardFocus += (_, _) => Upd();
        Upd();
    }

    /// Password-style text box with an eye button.
    public static Grid Secret(string text, Action<string> changed, string? placeholder = null)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var plain = Input(text, changed, placeholder: placeholder, mono: true);
        var pw = new PasswordBox { Password = text, FontSize = 13, Foreground = Text, Background = Field, BorderBrush = Edge, BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), CaretBrush = Accent };
        pw.PasswordChanged += (_, _) => { if (plain.Text != pw.Password) plain.Text = pw.Password; };
        plain.TextChanged += (_, _) => { if (pw.Password != plain.Text) pw.Password = plain.Text; };
        plain.Visibility = Visibility.Collapsed;
        g.Children.Add(plain); g.Children.Add(pw);
        var eye = Button("", () =>
        {
            bool show = plain.Visibility != Visibility.Visible;
            plain.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            pw.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }, glyph: "\uE7B3");
        eye.Padding = new Thickness(10, 8, 4, 8);
        eye.ToolTip = "Show or hide";
        eye.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(eye, 1);
        g.Children.Add(eye);
        return g;
    }

    public static Border Pill(string text, Brush fg, Brush? bg = null) => new()
    {
        Padding = new Thickness(9, 3, 9, 3), CornerRadius = new CornerRadius(20), Background = bg ?? new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
        Child = T(text, 11.5, fg, FontWeights.SemiBold, false), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
    };

    public static Ellipse Dot(Brush b, double size = 8) => new() { Width = size, Height = size, Fill = b, VerticalAlignment = VerticalAlignment.Center };

    /// Big number tile used on Home and Shopify.
    public static Border Tile(string glyph, Brush glyphColor, string label, out TextBlock value, out TextBlock sub, Action? onClick = null)
    {
        var st = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(0x26, ((SolidColorBrush)glyphColor).Color.R, ((SolidColorBrush)glyphColor).Color.G, ((SolidColorBrush)glyphColor).Color.B)), Child = Icon(glyph, 13, glyphColor) });
        head.Children.Add(T(label, 12.5, Dim, FontWeights.SemiBold, false, new Thickness(9, 0, 0, 0)).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        st.Children.Add(head);
        value = T("—", 24, Text, FontWeights.SemiBold, false, new Thickness(0, 12, 0, 0));
        value.FontFamily = Display;
        sub = T("", 12, Dim, null, false, new Thickness(0, 2, 0, 0));
        st.Children.Add(value); st.Children.Add(sub);
        var b = Box(st, new Thickness(16, 14, 16, 14));
        b.Margin = new Thickness(0, 0, 12, 12);
        b.MinHeight = 118;
        if (onClick != null)
        {
            b.Cursor = Cursors.Hand;
            b.MouseEnter += (_, _) => b.Background = CardHi;
            b.MouseLeave += (_, _) => b.Background = Card;
            b.MouseLeftButtonUp += (_, _) => onClick();
        }
        return b;
    }

    public static T Also<T>(this T x, Action<T> a) { a(x); return x; }
}

/// <summary>A grid that picks how many columns fit (min tile width), so tiles never squash or overlap.</summary>
public sealed class FlowGrid : Panel
{
    public double MinItemWidth { get; set; } = 220;
    public int MaxColumns { get; set; } = 4;
    int _cols = 1;

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? MinItemWidth * 2 : available.Width;
        _cols = Math.Clamp((int)(w / MinItemWidth), 1, Math.Max(1, Math.Min(MaxColumns, InternalChildren.Count)));
        double cw = w / _cols, rowH = 0, total = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            c.Measure(new Size(cw, double.PositiveInfinity));
            rowH = Math.Max(rowH, c.DesiredSize.Height);
            if (i % _cols == _cols - 1 || i == InternalChildren.Count - 1) { total += rowH; rowH = 0; }
        }
        return new Size(w, total);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double cw = final.Width / _cols, y = 0;
        for (int start = 0; start < InternalChildren.Count; start += _cols)
        {
            double rowH = 0;
            for (int j = start; j < Math.Min(start + _cols, InternalChildren.Count); j++) rowH = Math.Max(rowH, InternalChildren[j].DesiredSize.Height);
            for (int j = start; j < Math.Min(start + _cols, InternalChildren.Count); j++)
                InternalChildren[j].Arrange(new Rect((j - start) * cw, y, cw, rowH));
            y += rowH;
        }
        return final;
    }
}
