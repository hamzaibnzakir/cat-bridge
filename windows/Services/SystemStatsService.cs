using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicIsland;

/// <summary>CPU / GPU / RAM / network sampled once a second on a background thread.</summary>
public sealed class SystemStatsService : IDisposable
{
    public volatile float Cpu, Gpu = -1, RamPct;
    public double RamUsedGb, RamTotalGb, DownBps, UpBps;
    /// <summary>Only sample while the System tab is on screen (adaptive polling — idle cost ≈ 0).</summary>
    public volatile bool Active;

    readonly CancellationTokenSource _cts = new();
    PerformanceCounter? _cpu;
    PerformanceCounterCategory? _gpuCat;
    Dictionary<string, CounterSample> _gpuPrev = new();
    long _rx, _tx; DateTime _netAt;
    static readonly Regex Luid = new(@"luid_0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+");

    public void Start() => Task.Run(Loop);

    async Task Loop()
    {
        try { _cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true); _cpu.NextValue(); }
        catch { try { _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", true); _cpu.NextValue(); } catch { _cpu = null; } }
        try { if (PerformanceCounterCategory.Exists("GPU Engine")) _gpuCat = new PerformanceCounterCategory("GPU Engine"); } catch { }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                if (!Active) continue;
                try { Sample(); } catch (Exception ex) { App.Log(ex); }
            }
        }
        catch (OperationCanceledException) { }
    }

    void Sample()
    {
        if (_cpu != null) Cpu = Math.Clamp(_cpu.NextValue(), 0, 100);

        var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref m))
        {
            RamTotalGb = m.ullTotalPhys / 1073741824.0;
            RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            RamPct = m.dwMemoryLoad;
        }

        // GPU: sum 3D-engine utilisation per adapter (Intel UHD + NVIDIA Quadro on the 5540), report the busiest.
        if (_gpuCat != null)
        {
            try
            {
                var data = _gpuCat.ReadCategory()["Utilization Percentage"];
                var now = new Dictionary<string, CounterSample>();
                var perAdapter = new Dictionary<string, double>();
                if (data != null)
                {
                    foreach (InstanceData inst in data.Values)
                    {
                        if (!inst.InstanceName.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                        now[inst.InstanceName] = inst.Sample;
                        if (_gpuPrev.TryGetValue(inst.InstanceName, out var prev))
                        {
                            var v = CounterSample.Calculate(prev, inst.Sample);
                            var luid = Luid.Match(inst.InstanceName).Value;
                            perAdapter[luid] = perAdapter.GetValueOrDefault(luid) + v;
                        }
                    }
                }
                _gpuPrev = now;
                Gpu = perAdapter.Count == 0 ? 0 : (float)Math.Clamp(perAdapter.Values.Max(), 0, 100);
            }
            catch { Gpu = -1; }
        }

        long rx = 0, tx = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try { var st = ni.GetIPStatistics(); rx += st.BytesReceived; tx += st.BytesSent; } catch { }
        }
        var t = DateTime.UtcNow;
        if (_netAt != default)
        {
            var dt = (t - _netAt).TotalSeconds;
            if (dt > 0) { DownBps = Math.Max(0, (rx - _rx) / dt); UpBps = Math.Max(0, (tx - _tx) / dt); }
        }
        _rx = rx; _tx = tx; _netAt = t;
    }

    public void Dispose() { _cts.Cancel(); _cpu?.Dispose(); }
}
