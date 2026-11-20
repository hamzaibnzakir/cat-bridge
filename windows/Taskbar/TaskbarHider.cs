using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DynamicIsland.Taskbar;

/// <summary>
/// Hides the Windows taskbar (on every screen) so the island can take over, and always puts it back:
/// when you turn it off, when Dynamic Island exits or crashes, and if Explorer restarts while hidden it hides the new one.
/// It also switches the taskbar to auto-hide so maximised windows get the full screen, then restores your original setting.
/// </summary>
public static class TaskbarHider
{
    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public RECT rc; public IntPtr lParam; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
    const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

    public static bool Hidden { get; private set; }
    static int? _originalState;
    static DispatcherTimer? _guard;
    static DateTime _peekUntil = DateTime.MinValue;
    public static readonly uint TaskbarCreatedMsg = W.RegisterWindowMessage("TaskbarCreated");

    static List<IntPtr> Bars()
    {
        var list = new List<IntPtr>();
        var main = W.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) list.Add(main);
        W.EnumWindows((h, _) => { if (W.Class(h) == "Shell_SecondaryTrayWnd") list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    static int GetState() { var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() }; return (int)SHAppBarMessage(ABM_GETSTATE, ref d); }
    static void SetState(int s)
    {
        var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = W.FindWindow("Shell_TrayWnd", null), lParam = (IntPtr)s };
        SHAppBarMessage(ABM_SETSTATE, ref d);
    }

    public static void Hide()
    {
        try
        {
            if (!Hidden)
            {
                _originalState ??= GetState();
                if ((_originalState & ABS_AUTOHIDE) == 0) SetState(ABS_AUTOHIDE | (_originalState.Value & ABS_ALWAYSONTOP));
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
            }
            Hidden = true;
            foreach (var h in Bars()) W.ShowWindow(h, W.SW_HIDE);
            if (_guard == null)
            {
                // Start or Explorer sometimes bring the taskbar back: quietly hide it again
                _guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                _guard.Tick += (_, _) =>
                {
                    if (!Hidden || DateTime.Now < _peekUntil) return;
                    foreach (var h in Bars()) if (W.IsWindowVisible(h)) W.ShowWindow(h, W.SW_HIDE);
                };
                _guard.Start();
            }
        }
        catch (Exception ex) { App.Log(ex); }
    }

    public static void Restore()
    {
        try
        {
            Hidden = false;
            _guard?.Stop(); _guard = null;
            foreach (var h in Bars()) W.ShowWindow(h, W.SW_SHOWNA);
            if (_originalState is int s) { SetState(s); _originalState = null; }
        }
        catch { }
    }

    /// Shows the real taskbar for a few seconds (for its tray icons), then hides it again.
    public static void Peek(int seconds = 8)
    {
        if (!Hidden) return;
        _peekUntil = DateTime.Now.AddSeconds(seconds);
        foreach (var h in Bars()) W.ShowWindow(h, W.SW_SHOWNA);
        W.Combo(0x5B, 0x42); // Win+B: focus the tray area so its icons are reachable
    }

    /// Explorer restarted: its new taskbar is visible, hide it again.
    public static void OnTaskbarCreated()
    {
        if (!Hidden) return;
        _originalState = null;
        Hidden = false;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        t.Tick += (_, _) => { t.Stop(); Hide(); };
        t.Start();
    }
}
