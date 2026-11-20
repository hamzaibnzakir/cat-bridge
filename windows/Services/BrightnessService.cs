using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicIsland;

/// <summary>
/// Laptop panel via WMI first; external monitor via DDC/CI as a fallback.
/// Ported safety rule from PILLAR: never write a DDC brightness unless the monitor's range was read successfully,
/// so a bad reading can't slam the panel to its hardware minimum.
/// </summary>
public sealed class BrightnessService : IDisposable
{
    ManagementEventWatcher? _watch;
    int _pending = -1, _writing;
    public event Action<int>? Changed; // raised on a background thread when brightness changes (Fn keys, Windows slider…)

    public void StartWatching()
    {
        try
        {
            _watch = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watch.EventArrived += (_, e) =>
            {
                try { Changed?.Invoke(Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value)); } catch { }
            };
            _watch.Start();
        }
        catch { _watch = null; } // desktop monitors without WMI brightness
    }

    public int? Get()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject mo in s.Get())
                using (mo) return Convert.ToInt32(mo["CurrentBrightness"]);
        }
        catch { }
        return DdcGet();
    }

    /// Coalesces rapid slider drags: only the latest value is written.
    public void SetAsync(int level)
    {
        Interlocked.Exchange(ref _pending, Math.Clamp(level, 0, 100));
        if (Interlocked.Exchange(ref _writing, 1) == 1) return;
        Task.Run(async () =>
        {
            try
            {
                int v;
                while ((v = Interlocked.Exchange(ref _pending, -1)) >= 0) { Set(v); await Task.Delay(60); }
            }
            finally { Interlocked.Exchange(ref _writing, 0); }
        });
    }

    static bool Set(int level)
    {
        try
        {
            using var mc = new ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null);
            foreach (ManagementObject o in mc.GetInstances())
                using (o) { o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)level }); return true; }
        }
        catch { }
        return DdcSet(level);
    }

    // ── DDC/CI fallback ──
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYSICAL_MONITOR { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hmon, out uint n);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hmon, uint n, [Out] PHYSICAL_MONITOR[] arr);
    [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint v);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint n, PHYSICAL_MONITOR[] arr);

    static T? WithPrimary<T>(Func<IntPtr, T?> f)
    {
        var hmon = MonitorFromPoint(new POINT(), 1 /*MONITOR_DEFAULTTOPRIMARY*/);
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out var n) || n == 0) return default;
        var arr = new PHYSICAL_MONITOR[n];
        if (!GetPhysicalMonitorsFromHMONITOR(hmon, n, arr)) return default;
        try { return f(arr[0].h); }
        finally { DestroyPhysicalMonitors(n, arr); }
    }

    static int? DdcGet()
    {
        try
        {
            return WithPrimary<int?>(h =>
                GetMonitorBrightness(h, out var min, out var cur, out var max) && max > min
                    ? (int)Math.Round((cur - min) * 100.0 / (max - min)) : null);
        }
        catch { return null; }
    }

    static bool DdcSet(int level)
    {
        try
        {
            return WithPrimary(h =>
            {
                if (!GetMonitorBrightness(h, out var min, out _, out var max) || max <= min) return false; // unvalidated → don't write
                return SetMonitorBrightness(h, (uint)(min + (max - min) * level / 100.0));
            });
        }
        catch { return false; }
    }

    public void Dispose() { try { _watch?.Stop(); _watch?.Dispose(); } catch { } }
}
