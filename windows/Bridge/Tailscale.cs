using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DynamicIsland.Bridge;

/// <summary>Talks to the Tailscale command line tool that ships with Tailscale for Windows.</summary>
public static class Tailscale
{
    public sealed record State(bool Installed, bool Running, string? DnsName, string? Message);

    public static string? Exe
    {
        get
        {
            foreach (var p in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tailscale", "tailscale.exe"),
            })
                if (File.Exists(p)) return p;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { var p = Path.Combine(dir.Trim(), "tailscale.exe"); if (File.Exists(p)) return p; } catch { }
            }
            return null;
        }
    }

    public sealed record RunResult(int Code, string Output, bool TimedOut);

    /// Runs the CLI hidden. onLine sees output as it arrives (used to catch "approve HTTPS" links).
    public static async Task<RunResult> Run(string args, int timeoutMs = 15000, Action<string>? onLine = null)
    {
        var exe = Exe ?? throw new InvalidOperationException("Tailscale isn't installed.");
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        void Got(string? l) { if (l == null) return; lock (sb) sb.AppendLine(l); try { onLine?.Invoke(l); } catch { } }
        p.OutputDataReceived += (_, e) => Got(e.Data);
        p.ErrorDataReceived += (_, e) => Got(e.Data);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        var exited = await Task.Run(() => p.WaitForExit(timeoutMs));
        if (!exited) { try { p.Kill(true); } catch { } }
        else p.WaitForExit();
        lock (sb) return new RunResult(exited ? p.ExitCode : -1, sb.ToString(), !exited);
    }

    public static async Task<State> GetState()
    {
        if (Exe == null) return new State(false, false, null, "Tailscale isn't installed on this laptop.");
        try
        {
            var r = await Run("status --json", 8000);
            int start = r.Output.IndexOf('{');
            if (start < 0) return new State(true, false, null, "Tailscale isn't running. Open Tailscale and sign in.");
            using var doc = JsonDocument.Parse(r.Output[start..]);
            var root = doc.RootElement;
            var backend = root.TryGetProperty("BackendState", out var b) ? b.GetString() : null;
            string? dns = null;
            if (root.TryGetProperty("Self", out var self) && self.TryGetProperty("DNSName", out var d)) dns = d.GetString()?.TrimEnd('.');
            if (backend != "Running") return new State(true, false, dns, backend == "NeedsLogin" ? "Sign in to Tailscale first." : "Tailscale isn't connected. Open Tailscale and connect.");
            if (string.IsNullOrEmpty(dns)) return new State(true, true, null, "Turn on MagicDNS in your Tailscale admin console (DNS page).");
            return new State(true, true, dns, null);
        }
        catch (Exception ex) { return new State(true, false, null, "Couldn't read Tailscale's status: " + ex.Message); }
    }

    /// True if Tailscale Serve is already forwarding HTTPS to our port.
    public static async Task<bool> IsServing(int port)
    {
        try
        {
            var r = await Run("serve status", 8000);
            return r.Output.Contains($"127.0.0.1:{port}") || r.Output.Contains($"localhost:{port}");
        }
        catch { return false; }
    }

    /// Turns on HTTPS for this laptop's tailnet name, forwarding to the bridge. Tailnet only (this is Serve, not Funnel).
    /// If HTTPS certificates aren't enabled for the tailnet yet, Tailscale prints an approval link; we return it.
    public static async Task<(bool ok, string? approveUrl, string message)> Serve(int port)
    {
        string? approve = null;
        var r = await Run($"serve --bg {port}", 20000, line =>
        {
            int i = line.IndexOf("https://login.tailscale.com/", StringComparison.Ordinal);
            if (i >= 0 && approve == null) approve = line[i..].Trim().Split(' ')[0];
        });
        if (approve != null) return (false, approve, "Approve HTTPS for your tailnet in the page that just opened, then press Connect again.");
        if (r.Code == 0 || await IsServing(port)) return (true, null, "Connected");
        return (false, null, "Tailscale said: " + (r.Output.Trim().Length > 0 ? r.Output.Trim() : "error " + r.Code));
    }

    public static Task<RunResult> StopServe() => Run("serve --https=443 off", 10000);
}
