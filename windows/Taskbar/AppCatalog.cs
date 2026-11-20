using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicIsland.Taskbar;

public sealed class AppRef
{
    public string Id { get; set; } = "";      // lower-case AUMID or exe path
    public string Name { get; set; } = "";
    public string Launch { get; set; } = "";  // exe path or shell:AppsFolder\...
    public string IconPath { get; set; } = "";
}

/// <summary>
/// Every installed app (the same list as Start → All apps, Store apps included), plus your pinned and recent apps.
/// Pins and recents live in %AppData%\DynamicIsland\apps.json.
/// </summary>
public static class AppCatalog
{
    public sealed record Entry(string Name, string ParsingPath)
    {
        public string Launch => @"shell:AppsFolder\" + ParsingPath;
    }

    static List<Entry> _all = new();
    static Dictionary<string, string> _byAumid = new(StringComparer.OrdinalIgnoreCase);
    static Task? _loading;
    public static IReadOnlyList<Entry> All => _all;
    public static event Action? Loaded;

    /// Reads shell:AppsFolder on a background STA thread (it's a COM shell folder).
    public static Task LoadAsync()
    {
        if (_loading != null) return _loading;
        var tcs = new TaskCompletionSource();
        var t = new Thread(() =>
        {
            try
            {
                var list = new List<Entry>();
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type != null)
                {
                    dynamic shell = Activator.CreateInstance(type)!;
                    dynamic folder = shell.NameSpace("shell:AppsFolder");
                    foreach (dynamic item in folder.Items())
                    {
                        string name = item.Name; string path = item.Path;
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                        if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(new Entry(name, path));
                    }
                }
                _all = list.GroupBy(e => e.ParsingPath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                _byAumid = _all.GroupBy(e => e.ParsingPath, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { App.Log(ex); }
            tcs.TrySetResult();
            try { Loaded?.Invoke(); } catch { }
        }) { IsBackground = true, Name = "AppsFolder" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return _loading = tcs.Task;
    }

    public static string? NameFor(string aumid) => _byAumid.TryGetValue(aumid, out var n) ? n : null;

    /// Simple ranked search: name starts with > word starts with > contains > initials ("vsc" → Visual Studio Code).
    public static List<Entry> Search(string q, int max = 8)
    {
        q = q.Trim();
        if (q.Length == 0) return new();
        int Score(Entry e)
        {
            var n = e.Name;
            if (n.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)) return 0;
            var words = n.Split(new[] { ' ', '-', '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(w => w.StartsWith(q, StringComparison.CurrentCultureIgnoreCase))) return 1;
            if (n.Contains(q, StringComparison.CurrentCultureIgnoreCase)) return 2;
            var initials = string.Concat(words.Select(w => char.ToLowerInvariant(w[0])));
            if (initials.StartsWith(q.ToLowerInvariant())) return 3;
            return 99;
        }
        return _all.Select(e => (e, s: Score(e))).Where(x => x.s < 99).OrderBy(x => x.s).ThenBy(x => x.e.Name.Length).Take(max).Select(x => x.e).ToList();
    }

    // ───────── pinned + recent ─────────

    sealed class Store { public List<AppRef> Pinned { get; set; } = new(); public List<AppRef> Recent { get; set; } = new(); }
    static Store? _store;
    static string FilePath => Path.Combine(AppSettings.Dir, "apps.json");
    public static event Action? PinsChanged;

    static Store S
    {
        get
        {
            if (_store != null) return _store;
            try { if (File.Exists(FilePath)) _store = JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)); } catch { }
            if (_store == null)
            {
                _store = new Store();
                var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                _store.Pinned.Add(new AppRef { Id = explorer.ToLowerInvariant(), Name = "File Explorer", Launch = explorer, IconPath = explorer });
            }
            return _store;
        }
    }

    static void Save() { try { Directory.CreateDirectory(AppSettings.Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(S)); } catch (Exception ex) { App.Log(ex); } }

    public static IReadOnlyList<AppRef> Pinned => S.Pinned;
    public static IReadOnlyList<AppRef> Recent => S.Recent;
    public static bool IsPinned(string id) => S.Pinned.Any(p => p.Id == id);

    public static AppRef From(AppWindow w) => new() { Id = w.AppId, Name = w.AppName, Launch = w.Launch, IconPath = w.IconPath };
    public static AppRef From(Entry e) => new() { Id = e.ParsingPath.ToLowerInvariant(), Name = e.Name, Launch = e.Launch, IconPath = e.Launch };

    public static void Pin(AppRef a) { if (IsPinned(a.Id) || a.Launch.Length == 0) return; S.Pinned.Add(a); Save(); PinsChanged?.Invoke(); }
    public static void Unpin(string id) { if (S.Pinned.RemoveAll(p => p.Id == id) > 0) { Save(); PinsChanged?.Invoke(); } }
    public static void MovePin(string id, int delta)
    {
        int i = S.Pinned.FindIndex(p => p.Id == id), j = i + delta;
        if (i < 0 || j < 0 || j >= S.Pinned.Count) return;
        (S.Pinned[i], S.Pinned[j]) = (S.Pinned[j], S.Pinned[i]);
        Save(); PinsChanged?.Invoke();
    }

    public static void Used(AppWindow w)
    {
        if (w.Launch.Length == 0) return;
        S.Recent.RemoveAll(r => r.Id == w.AppId);
        S.Recent.Insert(0, From(w));
        if (S.Recent.Count > 12) S.Recent.RemoveRange(12, S.Recent.Count - 12);
        Save();
    }
}
