using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DynamicIsland;

public sealed record LyricLine(TimeSpan Time, string Text);

public sealed class Lyrics
{
    public List<LyricLine> Lines { get; } = new();
    public bool Synced;
    public bool Instrumental;
}

/// <summary>Free, keyless synced lyrics from LRCLIB (lrclib.net).</summary>
public sealed class LyricsService
{
    readonly HttpClient _http = new() { BaseAddress = new Uri("https://lrclib.net/"), Timeout = TimeSpan.FromSeconds(8) };
    readonly Dictionary<string, Lyrics?> _cache = new();

    public LyricsService() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0 (Windows desktop widget)");

    public async Task<Lyrics?> GetAsync(string artist, string title, double durationSec)
    {
        var key = $"{artist}|{title}".ToLowerInvariant();
        if (_cache.TryGetValue(key, out var c)) return c;
        Lyrics? result = null;
        var cands = Candidates(artist, title).ToList();
        try
        {
            foreach (var (a, t) in cands)
                if ((result = await TryGet(a, t, durationSec)) != null) break;
            if (result == null)
                foreach (var (a, t) in cands)
                    if ((result = await TrySearch(a, t, durationSec)) != null) break;
        }
        catch (Exception ex) { App.Log(ex); return null; } // network error: don't cache, retry next time
        _cache[key] = result;
        return result;
    }

    static readonly Regex Junk = new(@"\s*[\(\[][^\)\]]*(official|video|audio|lyric|visuali[sz]er|hd|4k|remaster|mv|explicit|clean)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase);

    static IEnumerable<(string a, string t)> Candidates(string artist, string title)
    {
        var a = Regex.Replace(artist, @"\s*-\s*Topic$|VEVO$|\s*Official$", "", RegexOptions.IgnoreCase).Trim();
        var t = Junk.Replace(title, "").Trim();
        if (a.Length > 0 && t.Length > 0) yield return (a, t);
        var dash = t.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) yield return (t[..dash].Trim(), t[(dash + 3)..].Trim()); // YouTube "Artist - Song"
    }

    async Task<Lyrics?> TryGet(string a, string t, double dur)
    {
        var url = $"api/get?artist_name={Uri.EscapeDataString(a)}&track_name={Uri.EscapeDataString(t)}";
        if (dur > 1) url += $"&duration={Math.Round(dur).ToString(CultureInfo.InvariantCulture)}";
        using var res = await _http.GetAsync(url);
        if (res.StatusCode == HttpStatusCode.NotFound || !res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return Parse(doc.RootElement);
    }

    async Task<Lyrics?> TrySearch(string a, string t, double dur)
    {
        var url = $"api/search?track_name={Uri.EscapeDataString(t)}&artist_name={Uri.EscapeDataString(a)}";
        using var res = await _http.GetAsync(url);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        var items = doc.RootElement.EnumerateArray().ToList();
        if (items.Count == 0) return null;
        double D(JsonElement e) => e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
        bool HasSync(JsonElement e) => e.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String;
        var best = items.OrderByDescending(HasSync).ThenBy(e => dur > 1 ? Math.Abs(D(e) - dur) : 0).First();
        return Parse(best);
    }

    static Lyrics? Parse(JsonElement e)
    {
        var l = new Lyrics();
        if (e.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True) { l.Instrumental = true; return l; }
        if (e.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString()))
        {
            l.Lines.AddRange(ParseLrc(s.GetString()!));
            l.Synced = l.Lines.Count > 0;
            if (l.Synced) return l;
        }
        if (e.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
        {
            foreach (var line in p.GetString()!.Split('\n')) l.Lines.Add(new LyricLine(TimeSpan.Zero, line.TrimEnd('\r')));
            return l;
        }
        return null;
    }

    static readonly Regex Tag = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]");

    public static List<LyricLine> ParseLrc(string lrc)
    {
        var list = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var ms = Tag.Matches(line);
            if (ms.Count == 0) continue;
            var text = Tag.Replace(line, "").Trim();
            foreach (Match m in ms)
            {
                int min = int.Parse(m.Groups[1].Value), sec = int.Parse(m.Groups[2].Value);
                double frac = 0;
                if (m.Groups[3].Success) { var f = m.Groups[3].Value; frac = int.Parse(f) / Math.Pow(10, f.Length); }
                list.Add(new LyricLine(TimeSpan.FromSeconds(min * 60 + sec + frac), text));
            }
        }
        list.Sort((x, y) => x.Time.CompareTo(y.Time));
        return list;
    }
}
