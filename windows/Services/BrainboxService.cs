using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;

namespace DynamicIsland;

public sealed record StatusRow(string Key, string Value, Brush Dot);
public sealed record QueueCounts(int Pending, int Running, int Done, int Failed);

/// <summary>
/// Live status for the Brainbox / Hermes stack. Nothing is ever faked:
/// no URL → "not configured", unreachable → "unavailable".
/// </summary>
public sealed class BrainboxService
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

    static readonly Brush Good = Frozen("#39FF88"), Bad = Frozen("#FF453A"), Warn = Frozen("#FFD60A"), Neutral = Frozen("#8E8E93");
    static Brush Frozen(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!; b.Freeze(); return b; }

    public static QueueCounts? ReadQueue(string root)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
            int C(string sub) { var d = Path.Combine(root, sub); return Directory.Exists(d) ? Directory.EnumerateFiles(d).Count() : 0; }
            return new QueueCounts(C("pending"), C("in_progress"), C("done"), C("failed"));
        }
        catch { return null; }
    }

    public async Task<(string State, List<StatusRow> Rows)> FetchAsync(string url, string token)
    {
        if (string.IsNullOrWhiteSpace(url)) return ("not-configured", new());
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return ($"HTTP {(int)res.StatusCode}", new());
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return ("ok", Flatten(doc.RootElement).Take(12).ToList());
        }
        catch { return ("unavailable", new()); }
    }

    static IEnumerable<StatusRow> Flatten(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in root.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Object && Str(e, "name") is string n)
                    yield return Row(n, Str(e, "status") ?? Str(e, "state") ?? "—");
            yield break;
        }
        if (root.ValueKind != JsonValueKind.Object) yield break;
        foreach (var p in root.EnumerateObject())
        {
            switch (p.Value.ValueKind)
            {
                case JsonValueKind.String: yield return Row(p.Name, p.Value.GetString() ?? ""); break;
                case JsonValueKind.Number: yield return Row(p.Name, p.Value.ToString()); break;
                case JsonValueKind.True: yield return Row(p.Name, "yes"); break;
                case JsonValueKind.False: yield return Row(p.Name, "no"); break;
                case JsonValueKind.Object:
                    yield return Row(p.Name, Str(p.Value, "status") ?? Str(p.Value, "state") ?? $"{p.Value.EnumerateObject().Count()} fields"); break;
                case JsonValueKind.Array:
                    yield return Row(p.Name, $"{p.Value.GetArrayLength()} items"); break;
            }
        }
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;

    static StatusRow Row(string k, string v)
    {
        var l = v.ToLowerInvariant();
        Brush dot = l is "ok" or "up" or "yes" or "true" || l.Contains("running") || l.Contains("healthy") || l.Contains("online") || l.Contains("active") ? Good
                  : l is "no" or "false" || l.Contains("down") || l.Contains("fail") || l.Contains("error") || l.Contains("offline") || l.Contains("dead") ? Bad
                  : l.Contains("degraded") || l.Contains("warn") || l.Contains("idle") || l.Contains("paused") ? Warn
                  : Neutral;
        return new StatusRow(k.Replace('_', ' '), v, dot);
    }
}
