using System;

namespace DynamicIsland;

public enum IslandMode { Compact, Toast, Expanded }

/// <summary>
/// Decides when the island is open. Pure logic (no WPF) so every scenario can be unit-tested.
///
/// Rules:
///  • Hover opens it only after the cursor rests on it for HoverDelayMs (so passing over it to reach
///    browser tabs doesn't pop it open).
///  • It closes once the cursor has been away for CollapseDelayMs. This is driven by polling the real
///    cursor position, so a missed MouseLeave can never leave it stuck open.
///  • Clicking opens it but never pins it. Only the pin button or the hotkey pins.
///  • A pin is released after PinTimeoutSeconds away (0 = never).
///  • Dragging and typing keep it open, with self-healing guards (mouse released / window lost focus).
/// </summary>
public sealed class IslandState
{
    public int HoverDelayMs = 300;
    public int CollapseDelayMs = 450;
    public int PinTimeoutSeconds = 60;

    public bool Pinned { get; private set; }
    public bool HoverOpen { get; private set; }
    public bool Dragging { get; private set; }
    public bool Typing { get; private set; }
    public bool ToastActive { get; set; }
    public bool ToastClickable { get; set; }

    DateTime _lastInside = DateTime.MinValue;
    DateTime? _insideSince;

    public IslandMode Mode =>
        Dragging || Typing || Pinned || HoverOpen ? IslandMode.Expanded
        : ToastActive ? IslandMode.Toast
        : IslandMode.Compact;

    /// True while anything still needs the cursor watcher (polling stops when idle).
    public bool NeedsWatch => Mode != IslandMode.Compact || _insideSince != null;

    /// Called ~every 100 ms while NeedsWatch, and on mouse enter.
    public void Tick(DateTime now, bool cursorInside, bool leftButtonDown, bool windowFocused)
    {
        if (cursorInside)
        {
            _lastInside = now;
            _insideSince ??= now;
            bool dwellDone = (now - _insideSince.Value).TotalMilliseconds >= HoverDelayMs;
            bool clickableToast = Mode == IslandMode.Toast && ToastClickable;
            if (dwellDone && !clickableToast) HoverOpen = true;
        }
        else
        {
            _insideSince = null;
        }

        var away = now - _lastInside;
        if (HoverOpen && !cursorInside && away.TotalMilliseconds >= CollapseDelayMs) HoverOpen = false;
        if (Pinned && PinTimeoutSeconds > 0 && !cursorInside && away.TotalSeconds >= PinTimeoutSeconds) Pinned = false;

        // Self-healing guards.
        if (Dragging && !leftButtonDown) Dragging = false;
        if (Typing && !windowFocused && !cursorInside && away.TotalMilliseconds >= 1500) Typing = false;
    }

    /// Click on the island. Opens it (like hover) but never pins it.
    /// Returns true if the click should run a clickable toast's action instead.
    public bool Click(DateTime now)
    {
        if (Mode == IslandMode.Toast && ToastClickable) return true;
        if (Mode != IslandMode.Expanded) { HoverOpen = true; _lastInside = now; }
        return false;
    }

    public void TogglePin(DateTime now) { Pinned = !Pinned; _lastInside = now; }
    public void Unpin() => Pinned = false;

    /// Close now (e.g. before a screenshot), whatever opened it.
    public void ForceCollapse() { Pinned = false; HoverOpen = false; Typing = false; }

    /// Open as if hovered (e.g. after tapping a track toast or dropping a file) — closes normally when you leave.
    public void OpenSoft(DateTime now) { HoverOpen = true; _lastInside = now; }

    public void SetDragging(bool on, DateTime now) { Dragging = on; if (!on) _lastInside = now; }
    public void SetTyping(bool on, DateTime now) { Typing = on; _lastInside = now; }
}
