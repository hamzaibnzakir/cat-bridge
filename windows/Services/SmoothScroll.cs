using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>
/// Smooth, touchpad-friendly scrolling for every ScrollViewer in the app:
/// • two-finger scrolling moves by exactly how far your fingers move (precision touchpads send small deltas),
///   mouse wheels glide instead of jumping 3 lines;
/// • two-finger sideways scrolling works (WPF ignores WM_MOUSEHWHEEL by default);
/// • the innermost list that can still scroll gets the gesture; at its end the page scrolls instead;
/// • touchscreens can pan.
/// </summary>
public static class SmoothScroll
{
    sealed class State { public double X, Y; public bool Running; }
    static readonly ConditionalWeakTable<ScrollViewer, State> States = new();
    static readonly List<ScrollViewer> Active = new();
    static bool _installed, _rendering;

    /// Lets other parts (e.g. banners) react to a sideways swipe first. Return true to consume it.
    public static event Func<DependencyObject, int, bool>? SidewaysSwipe;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel), true);
        EventManager.RegisterClassHandler(typeof(ScrollViewer), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        ComponentDispatcher.ThreadFilterMessage += OnThreadMessage;
    }

    static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        if (sv.TemplatedParent is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox or ComboBox) return; // leave text boxes alone
        sv.CanContentScroll = false; // pixel scrolling, not item-by-item jumps
        if (sv.PanningMode == PanningMode.None) sv.PanningMode = PanningMode.VerticalFirst;
    }

    static double Pixels(int delta) => delta / 120.0 * Math.Max(1, SystemParameters.WheelScrollLines) * 19;

    static bool CanScroll(ScrollViewer sv, bool horizontal, int delta)
    {
        if (horizontal)
            return sv.ScrollableWidth > 0.5 && (delta > 0 ? sv.HorizontalOffset < sv.ScrollableWidth - 0.5 : sv.HorizontalOffset > 0.5);
        return sv.ScrollableHeight > 0.5 && (delta > 0 ? sv.VerticalOffset > 0.5 : sv.VerticalOffset < sv.ScrollableHeight - 0.5);
    }

    static ScrollViewer? Find(DependencyObject? from, bool horizontal, int delta)
    {
        for (var d = from; d != null; d = Parent(d))
            if (d is ScrollViewer sv && CanScroll(sv, horizontal, delta)) return sv;
        return null;
    }

    static DependencyObject? Parent(DependencyObject d)
        => d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        if (sender is ScrollViewer own && own.TemplatedParent is System.Windows.Controls.Primitives.TextBoxBase) return;
        var src = e.OriginalSource as DependencyObject;
        bool horizontal = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var target = Find(src, horizontal, e.Delta);
        if (target == null && !horizontal)
        {
            // a vertical gesture over a sideways-only strip with nothing else to scroll: move the strip
            target = Find(src, true, -e.Delta);
            if (target != null && target.ScrollableHeight > 0.5) target = null;
            horizontal = target != null;
            if (horizontal) { Scroll(target!, true, Pixels(-e.Delta)); e.Handled = true; }
            return;
        }
        if (target == null) { e.Handled = sender is ScrollViewer; return; } // at the end: don't let WPF jump the parent
        if (!ReferenceEquals(target, sender)) return; // the inner viewer will get its own turn
        Scroll(target, horizontal, horizontal ? Pixels(-e.Delta) : -Pixels(e.Delta));
        e.Handled = true;
    }

    const int WM_MOUSEHWHEEL = 0x020E;

    static void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != WM_MOUSEHWHEEL || handled) return;
        int delta = (short)((long)msg.wParam >> 16 & 0xFFFF);
        var over = Mouse.DirectlyOver as DependencyObject;
        if (over == null) return;
        try { if (SidewaysSwipe?.Invoke(over, delta) == true) { handled = true; return; } } catch (Exception ex) { App.Log(ex); }
        var sv = Find(over, true, delta);
        if (sv == null) return;
        Scroll(sv, true, Pixels(delta));
        handled = true;
    }

    /// Glides by `by` pixels (positive = down / right).
    public static void Scroll(ScrollViewer sv, bool horizontal, double by)
    {
        var st = States.GetValue(sv, _ => new State());
        if (!st.Running) { st.X = sv.HorizontalOffset; st.Y = sv.VerticalOffset; }
        if (horizontal) st.X = Math.Clamp(st.X + by, 0, sv.ScrollableWidth);
        else st.Y = Math.Clamp(st.Y + by, 0, sv.ScrollableHeight);
        if (!st.Running) { st.Running = true; Active.Add(sv); }
        if (!_rendering) { _rendering = true; CompositionTarget.Rendering += Tick; }
    }

    static void Tick(object? sender, EventArgs e)
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var sv = Active[i];
            if (!States.TryGetValue(sv, out var st) || !sv.IsLoaded) { Active.RemoveAt(i); continue; }
            double dx = st.X - sv.HorizontalOffset, dy = st.Y - sv.VerticalOffset;
            if (Math.Abs(dx) < 0.6 && Math.Abs(dy) < 0.6)
            {
                sv.ScrollToHorizontalOffset(st.X); sv.ScrollToVerticalOffset(st.Y);
                st.Running = false; Active.RemoveAt(i); continue;
            }
            // ease toward the target: quick to respond, no overshoot
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset + dx * 0.32);
            sv.ScrollToVerticalOffset(sv.VerticalOffset + dy * 0.32);
        }
        if (Active.Count == 0) { CompositionTarget.Rendering -= Tick; _rendering = false; }
    }
}
