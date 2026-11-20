using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DynamicIsland.Taskbar;

/// <summary>
/// Apps that are running with no window open — the ones that normally hide in the taskbar's tray (Discord, Tailscale,
/// OneDrive, Phone Link…). Only programs installed for you (Program Files / AppData) are shown, never Windows' own processes.
/// </summary>
public static class BackgroundApps
{
    public sealed record Item(string Exe, string Name, int[] Pids);

    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedgewebview2", "crashpad_handler", "CefSharp.BrowserSubprocess", "dllhost", "conhost", "RuntimeBroker", "backgroundTaskHost",
        "WidgetService", "Widgets", "SearchHost", "StartMenuExperienceHost", "TextInputHost", "ShellExperienceHost", "PhoneExperienceHost",
        "msedge", "chrome", "firefox", "Code", "node", "python", "pythonw", "dotnet", "MicrosoftEdgeUpdate", "GoogleUpdate", "updater",
        "SecurityHealthSystray", "AggregatorHost", "CrossDeviceResume", "OfficeClickToRun", "sihost", "taskhostw", "ctfmon", "smartscreen",
    };

    public static List<Item> List(IEnumerable<AppWindow> windows)
    {
        var withWindows = new HashSet<string>(windows.Where(w => w.Exe != null).Select(w => w.Exe!), StringComparer.OrdinalIgnoreCase);
        int session = Process.GetCurrentProcess().SessionId, self = Environment.ProcessId;
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        }.Where(r => r.Length > 0).ToArray();
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");

        var byExe = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId != session || p.Id == self || Skip.Contains(p.ProcessName)) continue;
                var exe = W.ExePath((uint)p.Id);
                if (exe == null || withWindows.Contains(exe)) continue;
                if (!roots.Any(r => exe.StartsWith(r, StringComparison.OrdinalIgnoreCase))) continue;
                if (exe.StartsWith(windowsApps, StringComparison.OrdinalIgnoreCase) && exe.Contains("Microsoft.Windows", StringComparison.OrdinalIgnoreCase)) continue;
                var file = Path.GetFileNameWithoutExtension(exe);
                if (file.Contains("update", StringComparison.OrdinalIgnoreCase) || file.Contains("helper", StringComparison.OrdinalIgnoreCase)
                    || file.Contains("crash", StringComparison.OrdinalIgnoreCase) || file.Contains("service", StringComparison.OrdinalIgnoreCase)) continue;
                if (!byExe.TryGetValue(exe, out var l)) byExe[exe] = l = new();
                l.Add(p.Id);
            }
            catch { }
            finally { p.Dispose(); }
        }
        return byExe.Select(kv => new Item(kv.Key, WindowList.AppNameFor(kv.Key, null, Path.GetFileNameWithoutExtension(kv.Key)), kv.Value.ToArray()))
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// Most tray apps bring their window back when started again.
    public static void Open(Item i) => WindowList.Launch(i.Exe);

    /// Politely asks the app to quit (closes its hidden windows). Returns true if it's gone after a few seconds.
    public static async Task<bool> Quit(Item i)
    {
        foreach (var pid in i.Pids)
            W.EnumWindows((h, _) =>
            {
                W.GetWindowThreadProcessId(h, out uint p);
                if (p == pid) W.PostMessage(h, W.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        for (int t = 0; t < 30; t++)
        {
            await Task.Delay(100);
            if (i.Pids.All(Gone)) return true;
        }
        return false;
    }

    public static void ForceQuit(Item i)
    {
        foreach (var pid in i.Pids) { try { using var p = Process.GetProcessById(pid); p.Kill(true); } catch { } }
    }

    static bool Gone(int pid) { try { using var p = Process.GetProcessById(pid); return p.HasExited; } catch { return true; } }
}
