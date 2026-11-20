using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;

namespace DynamicIsland.Taskbar;

/// <summary>One open window, the way the taskbar sees it.</summary>
public sealed class AppWindow
{
    public IntPtr Hwnd;
    public string Title = "";
    public uint Pid;
    public string? Exe;
    public string? Aumid;
    public string AppId = "";      // groups windows of the same app
    public string AppName = "";
    public string Launch = "";     // how to start the app again
    public bool Minimized;
    public ImageSource? Icon => ShellIcons.Get(IconPath);
    public string IconPath => Aumid != null && (Exe == null || Path.GetFileName(Exe).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(Exe))
        ? @"shell:AppsFolder\" + Aumid : Exe ?? "";
}

/// <summary>Lists, switches, minimises and closes app windows (like the taskbar / Alt+Tab).</summary>
public static class WindowList
{
    static readonly HashSet<string> SkipClasses = new(StringComparer.Ordinal)
    { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow" };

    static readonly Dictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);

    public static List<AppWindow> List()
    {
        var self = (uint)Environment.ProcessId;
        var list = new List<AppWindow>();
        W.EnumWindows((h, _) =>
        {
            try { if (IsTaskbarWindow(h, self)) list.Add(Describe(h)); } catch { }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static bool IsTaskbarWindow(IntPtr h, uint self)
    {
        if (!W.IsWindowVisible(h) || W.Cloaked(h)) return false;
        long ex = (long)W.GetWindowLongPtr(h, W.GWL_EXSTYLE);
        bool appWin = (ex & W.WS_EX_APPWINDOW) != 0;
        if (!appWin && (ex & W.WS_EX_TOOLWINDOW) != 0) return false;
        if (!appWin && (ex & W.WS_EX_NOACTIVATE) != 0) return false;
        if (!appWin && W.GetWindow(h, W.GW_OWNER) != IntPtr.Zero) return false;
        if (W.GetWindowTextLength(h) == 0) return false;
        if (SkipClasses.Contains(W.Class(h))) return false;
        W.GetWindowThreadProcessId(h, out uint pid);
        return pid != self;
    }

    public static AppWindow Describe(IntPtr h)
    {
        W.GetWindowThreadProcessId(h, out uint pid);
        var exe = W.ExePath(pid);
        var aumid = W.Aumid(h);
        var w = new AppWindow { Hwnd = h, Title = W.Text(h), Pid = pid, Exe = exe, Aumid = aumid, Minimized = W.IsIconic(h) };
        w.AppId = (aumid ?? exe ?? ("pid:" + pid)).ToLowerInvariant();
        w.AppName = AppNameFor(exe, aumid, w.Title);
        w.Launch = aumid != null ? @"shell:AppsFolder\" + aumid : exe ?? "";
        return w;
    }

    public static string AppNameFor(string? exe, string? aumid, string title)
    {
        var key = aumid ?? exe ?? "";
        lock (NameCache) if (key.Length > 0 && NameCache.TryGetValue(key, out var cached)) return cached;
        string name = "";
        if (aumid != null) name = AppCatalog.NameFor(aumid) ?? "";
        if (name.Length == 0 && exe != null)
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exe);
                name = (vi.FileDescription ?? "").Trim();
                if (name.Length == 0 || name.Length > 40) name = (vi.ProductName ?? "").Trim();
            }
            catch { }
            if (name.Length == 0) name = Path.GetFileNameWithoutExtension(exe);
            if (Path.GetFileName(exe).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) name = "File Explorer";
        }
        if (name.Length == 0) name = title;
        lock (NameCache) if (key.Length > 0) NameCache[key] = name;
        return name;
    }

    /// Brings a window to the front (restoring it if minimised). Works around Windows' focus-stealing rules.
    public static void Activate(IntPtr h)
    {
        if (!W.IsWindow(h)) return;
        if (W.IsIconic(h)) W.ShowWindow(h, W.SW_RESTORE);
        if (W.SetForegroundWindow(h) && W.GetForegroundWindow() == h) return;
        // a quick Alt tap tells Windows this switch is user-initiated
        W.Key(0x12, false); W.Key(0x12, true);
        W.SetForegroundWindow(h);
        if (W.GetForegroundWindow() == h) return;
        var fg = W.GetForegroundWindow();
        uint a = W.GetWindowThreadProcessId(fg, out _), me = W.GetCurrentThreadId();
        if (a != me) W.AttachThreadInput(me, a, true);
        W.BringWindowToTop(h); W.SetForegroundWindow(h);
        if (a != me) W.AttachThreadInput(me, a, false);
    }

    /// Taskbar behaviour: click the front window → minimise it; otherwise bring it forward.
    public static void Toggle(IntPtr h)
    {
        if (W.GetForegroundWindow() == h && !W.IsIconic(h)) Minimize(h);
        else Activate(h);
    }

    public static void Minimize(IntPtr h) => W.PostMessage(h, W.WM_SYSCOMMAND, (IntPtr)W.SC_MINIMIZE, IntPtr.Zero);
    public static void Close(IntPtr h) => W.PostMessage(h, W.WM_SYSCOMMAND, (IntPtr)W.SC_CLOSE, IntPtr.Zero);

    public static void Launch(string launch)
    {
        if (string.IsNullOrWhiteSpace(launch)) return;
        try
        {
            if (launch.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", launch) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(launch) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(launch) ?? "" });
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // ── Windows shortcuts (Start, Task view, etc. still work without the taskbar) ──
    const byte LWIN = 0x5B;
    public static void Start() => W.Combo(LWIN);
    public static void TaskView() => W.Combo(LWIN, 0x09);          // Win+Tab
    public static void ShowDesktop() => W.Combo(LWIN, 0x44);       // Win+D
    public static void NotificationCenter() => W.Combo(LWIN, 0x4E); // Win+N
    public static void QuickSettings() => W.Combo(LWIN, 0x41);      // Win+A
    public static void Search() => W.Combo(LWIN, 0x53);             // Win+S
}
