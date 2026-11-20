using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DynamicIsland;

public sealed record MonitorResult(string Name, bool Up, int Ms);

/// <summary>Pings the VPS, SSH tunnels and local services. Reports real reachability only.</summary>
public sealed class MonitorService
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    readonly Dictionary<string, bool> _last = new();
    public List<MonitorResult> Results { get; private set; } = new();

    public async Task<List<(string Name, bool Up)>> CheckAsync(IEnumerable<MonitorTarget> targets)
    {
        var valid = targets.Where(t => t.Type.Equals("http", StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(t.Url) : !string.IsNullOrWhiteSpace(t.Host) && t.Port > 0).ToList();
        var results = await Task.WhenAll(valid.Select(Check));
        Results = results.ToList();
        var changes = new List<(string, bool)>();
        foreach (var r in results)
        {
            if (_last.TryGetValue(r.Name, out var prev) && prev != r.Up) changes.Add((r.Name, r.Up));
            _last[r.Name] = r.Up;
        }
        return changes;
    }

    async Task<MonitorResult> Check(MonitorTarget t)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (t.Type.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                using var res = await _http.GetAsync(t.Url, HttpCompletionOption.ResponseHeadersRead);
                return new MonitorResult(t.Name, (int)res.StatusCode < 500, (int)sw.ElapsedMilliseconds);
            }
            using var c = new TcpClient();
            var connect = c.ConnectAsync(t.Host, t.Port);
            var done = await Task.WhenAny(connect, Task.Delay(3000));
            bool up = done == connect && connect.IsCompletedSuccessfully && c.Connected;
            return new MonitorResult(t.Name, up, (int)sw.ElapsedMilliseconds);
        }
        catch { return new MonitorResult(t.Name, false, (int)sw.ElapsedMilliseconds); }
    }
}
