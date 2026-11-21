using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>
/// Keeps Cat Bridge up to date from GitHub Releases. Checks a minute after start and every 6 hours.
/// The installer is downloaded, checked against GitHub's SHA-256 for the file, run quietly,
/// and the app comes back on its own. Copies run from a zip (not installed) are told about updates instead.
/// </summary>
public static class Updater
{
    public sealed record Release(Version Version, string Tag, string Notes, string PageUrl, string? SetupUrl, long SetupSize, string? Sha256);

    public static Version CurrentVersion { get; } = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0);
    public static Release? Available { get; private set; }
    public static string Status { get; private set; } = "Not checked yet";
    public static DateTime LastCheck { get; private set; } = DateTime.MinValue;
    public static double? Progress { get; private set; }
    public static event Action? Changed;

    /// Installed with the Cat Bridge installer (it leaves an uninstaller next to the app)?
    public static bool Installed => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static IslandWindow? _win;
    static AppSettings? _s;
    static DispatcherTimer? _timer;
    static bool _busy;

    static Updater()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("CatBridge-Updater/" + CurrentVersion.ToString(3));
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static void Start(IslandWindow win, AppSettings s)
    {
        _win = win; _s = s;
        CleanupOldDownloads();
        if (s.UpdateMode == "off") { Status = "Automatic checks are off"; return; }
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(6);
            if (_s.UpdateMode != "off") await CheckAsync(userAsked: false);
        };
        _timer.Start();
    }

    public static async Task CheckAsync(bool userAsked)
    {
        if (_busy || _s == null) return;
        _busy = true;
        Status = "Checking…"; Changed?.Invoke();
        try
        {
            var r = await Latest(_s.UpdateRepo);
            LastCheck = DateTime.Now;
            if (r == null || r.Version <= CurrentVersion)
            {
                Available = null;
                Status = $"You're up to date ({CurrentVersion.ToString(3)})";
                return;
            }
            Available = r;
            Status = $"Cat Bridge {r.Version.ToString(3)} is available";
            if (!userAsked && _s.SkippedVersion == r.Tag) return;
            if (_s.UpdateMode == "auto" && Installed && r.SetupUrl != null)
            {
                _busy = false;
                await InstallAsync(quiet: true);
                return;
            }
            _win?.Dispatcher.Invoke(() => _win.ShowUpdateToast(r));
        }
        catch (Exception ex)
        {
            Status = "Couldn't check for updates: " + (ex is HttpRequestException or TaskCanceledException ? "no connection to GitHub" : ex.Message);
            if (userAsked) App.Log(ex);
        }
        finally { _busy = false; Changed?.Invoke(); }
    }

    static async Task<Release?> Latest(string repo)
    {
        using var res = await Http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParse(tag, out var v)) return null;
        string? url = null, sha = null; long size = 0;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (!name.Equals("DynamicIsland-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
            url = a.GetProperty("browser_download_url").GetString();
            size = a.GetProperty("size").GetInt64();
            if (a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String && dg.GetString()!.StartsWith("sha256:"))
                sha = dg.GetString()![7..];
        }
        return new Release(v, tag, root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            root.GetProperty("html_url").GetString() ?? $"https://github.com/{repo}/releases", url, size, sha);
    }

    public static bool TryParse(string tag, out Version v)
    {
        var t = tag.Trim().TrimStart('v', 'V');
        var dash = t.IndexOf('-'); if (dash > 0) t = t[..dash];
        if (Version.TryParse(t, out var parsed))
        {
            v = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
            return true;
        }
        v = new Version(0, 0);
        return false;
    }

    /// Downloads the installer, checks it, runs it and closes the app so it can be replaced. The installer restarts the app.
    public static async Task InstallAsync(bool quiet)
    {
        var r = Available;
        if (r == null || _busy) return;
        if (!Installed || r.SetupUrl == null)
        {
            Process.Start(new ProcessStartInfo(r.PageUrl) { UseShellExecute = true });
            return;
        }
        _busy = true;
        try
        {
            var dest = Path.Combine(Path.GetTempPath(), $"CatBridge-Setup-{r.Version.ToString(3)}.exe");
            Status = "Downloading the update…"; Progress = 0; Changed?.Invoke();
            using (var res = await Http.GetAsync(r.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                res.EnsureSuccessStatusCode();
                long total = res.Content.Headers.ContentLength ?? r.SetupSize;
                await using var src = await res.Content.ReadAsStreamAsync();
                await using var fs = File.Create(dest);
                var buf = new byte[1 << 16]; long got = 0; int n; var last = DateTime.MinValue;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, n));
                    got += n;
                    if (total > 0 && DateTime.Now - last > TimeSpan.FromMilliseconds(250)) { Progress = (double)got / total; last = DateTime.Now; Changed?.Invoke(); }
                }
            }
            Status = "Checking the download…"; Progress = null; Changed?.Invoke();
            if (r.SetupSize > 0 && new FileInfo(dest).Length != r.SetupSize) throw new InvalidOperationException("The download is incomplete.");
            if (r.Sha256 != null)
            {
                await using var f = File.OpenRead(dest);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(f));
                if (!hash.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(dest); throw new InvalidOperationException("The download didn't match GitHub's checksum, so it wasn't installed."); }
            }

            Status = "Installing…"; Changed?.Invoke();
            var args = quiet ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE" : "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE";
            Process.Start(new ProcessStartInfo(dest, args) { UseShellExecute = true });
            await Task.Delay(800);
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown()); // the installer starts the new version
        }
        catch (Exception ex)
        {
            App.Log(ex);
            Status = "Update failed: " + ex.Message;
            Progress = null;
            _win?.Dispatcher.Invoke(() => _win.ShowNotice("The update didn't install", ex.Message));
        }
        finally { _busy = false; Changed?.Invoke(); }
    }

    public static void Skip()
    {
        if (Available == null || _s == null) return;
        _s.SkippedVersion = Available.Tag; _s.Save();
    }

    static void CleanupOldDownloads()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), "CatBridge-Setup-*.exe"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddMinutes(-10)) File.Delete(f);
        }
        catch { }
    }
}
