using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using DynamicIsland.FaceId;
using Microsoft.Win32;

namespace DynamicIsland.FaceHelper;

/// <summary>
///   FaceHelper --verify               face check for the sign-in tile; prints STATUS|… lines then RESULT|passed / RESULT|password|why
///   FaceHelper --install DIR HANDOFF  (elevated) install the sign-in tile from the app folder DIR
///   FaceHelper --uninstall            (elevated) remove it completely
/// Runs separately from the sign-in screen so a crash here can never take sign-in down.
/// </summary>
static class Program
{
    static string LogPath => Path.Combine(SignInSecret.DataDir, "helper.log");

    static int Main(string[] args)
    {
        FaceLog.Sink = ex => Log(ex?.ToString() ?? "");
        try
        {
            return args.FirstOrDefault() switch
            {
                "--verify" => Verify(),
                "--install" when args.Length >= 3 => Install(args[1], args[2], args.Skip(3).ToArray()),
                "--uninstall" => Uninstall(),
                _ => Usage(),
            };
        }
        catch (Exception ex) { Log(ex.ToString()); Console.WriteLine("RESULT|error|" + ex.Message); return 2; }
    }

    static int Usage() { Console.WriteLine("FaceHelper --verify | --install <appDir> <handoffFile> | --uninstall"); return 1; }

    static void Log(string msg)
    {
        try { Directory.CreateDirectory(SignInSecret.DataDir); File.AppendAllText(LogPath, $"[{DateTime.Now:u}] {msg}\n"); } catch { }
    }

    static void Out(string line) { Console.WriteLine(line); Console.Out.Flush(); }

    // ───────── face check ─────────

    static int Verify()
    {
        var cfg = ReadConfig();
        var data = FaceStore.Load();
        if (data == null || data.Embeddings.Count == 0) { Out("RESULT|password|Face unlock isn't set up"); return 0; }

        var models = Path.Combine(AppContext.BaseDirectory, "models");
        var v = new FaceVerifier
        {
            Threshold = Get(cfg, "threshold", 0.42),
            FlipTurn = Get(cfg, "flip", 0) > 0,
        };
        var done = new ManualResetEventSlim();
        string result = "RESULT|password|Two tries didn't work. Type your password instead.";
        string last = "";
        DateTime pauseUntil = DateTime.MinValue;
        void Status(string s) { if (s != last) { last = s; Out("STATUS|" + s); } }

        Status("Waking up the camera…");
        using var cam = new FaceCamera((int)Get(cfg, "camera", 0), models, data.Embeddings, preview: false);
        cam.Error += msg => { result = "RESULT|password|" + msg; done.Set(); };
        cam.Frame += f =>
        {
            if (done.IsSet) return;
            if (!f.Ready) { Status("Getting ready…"); return; }
            if (DateTime.Now < pauseUntil) return;
            if (v.Step == VerifyStep.AttemptFailed) { v.NextAttempt(); if (v.Step == VerifyStep.NeedPassword) { done.Set(); return; } }

            var st = v.Feed(f.Obs);
            if (st is not (VerifyStep.Passed or VerifyStep.AttemptFailed))
            {
                if (!f.Obs.HasFace) { Status("Look at the camera"); return; }
                if (v.TooMany) { Status("Just you, please"); return; }
                if (v.TooFar) { Status("Come a little closer"); return; }
            }
            switch (st)
            {
                case VerifyStep.FindFace:
                case VerifyStep.Identify: Status("Checking it's you…"); break;
                case VerifyStep.Turn: Status(v.TurnDir > 0 ? "Turn your head a little to your left" : "Turn your head a little to your right"); break;
                case VerifyStep.ReturnCenter: Status("And back to the camera"); break;
                case VerifyStep.Confirm: Status("Almost there…"); break;
                case VerifyStep.Passed: result = "RESULT|passed"; done.Set(); break;
                case VerifyStep.AttemptFailed:
                    if (v.Attempt >= v.MaxAttempts) { done.Set(); break; }
                    Status(v.FailReason + ". One more try…");
                    pauseUntil = DateTime.Now.AddSeconds(1.5);
                    break;
            }
        };
        cam.Start();
        if (!done.Wait(TimeSpan.FromSeconds(45))) result = "RESULT|password|Face check timed out. Type your password instead.";
        Out(result);
        return 0;
    }

    static Dictionary<string, string> ReadConfig()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadAllLines(SignInSecret.ConfigPath))
            {
                int i = line.IndexOf('=');
                if (i > 0) d[line[..i].Trim()] = line[(i + 1)..].Trim();
            }
        }
        catch { }
        return d;
    }

    static double Get(Dictionary<string, string> d, string k, double def) =>
        d.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : def;

    // ───────── install / uninstall (elevated) ─────────

    static bool IsAdmin() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static readonly string[] Files =
    {
        "FaceHelper.exe", "FaceHelper.dll", "FaceHelper.deps.json", "FaceHelper.runtimeconfig.json",
        "OpenCvSharp.dll", "OpenCvSharpExtern.dll", "System.Security.Cryptography.ProtectedData.dll",
        "DynamicIsland.FaceSignIn.dll",
    };

    static int Install(string appDir, string handoff, string[] extra)
    {
        if (!IsAdmin()) { Console.WriteLine("Needs administrator rights."); return 3; }
        Log("install from " + appDir);

        // 1. Program Files (only admins can change it, which matters because SYSTEM will run these files)
        var dest = SignInSecret.InstallDir;
        Directory.CreateDirectory(Path.Combine(dest, "models"));
        foreach (var f in Files)
        {
            var src = Path.Combine(appDir, f);
            if (File.Exists(src)) File.Copy(src, Path.Combine(dest, f), overwrite: true);
            else if (f != "System.Security.Cryptography.ProtectedData.dll") throw new FileNotFoundException("Missing " + f, src);
        }
        foreach (var m in Directory.GetFiles(Path.Combine(appDir, "models")))
            File.Copy(m, Path.Combine(dest, "models", Path.GetFileName(m)), overwrite: true);

        // 2. the saved Windows credential, re-encrypted for the sign-in screen
        SignInSecret.WriteMachineSecret(SignInSecret.ReadHandoff(handoff));
        File.WriteAllLines(SignInSecret.ConfigPath, extra.Where(a => a.Contains('=')));

        // 3. COM registration + tell Windows about the new sign-in option
        var regasm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe");
        int rc = Run(regasm, $"/codebase \"{Path.Combine(dest, "DynamicIsland.FaceSignIn.dll")}\"");
        if (rc != 0) throw new Exception("RegAsm failed with code " + rc + " (is .NET Framework 4.8 installed?)");
        using (var k = Registry.LocalMachine.CreateSubKey(SignInSecret.ProviderKey)) k.SetValue("", SignInSecret.ProviderName);
        Log("installed");
        Console.WriteLine("OK");
        return 0;
    }

    static int Uninstall()
    {
        if (!IsAdmin()) { Console.WriteLine("Needs administrator rights."); return 3; }
        Log("uninstall");
        try { Registry.LocalMachine.DeleteSubKeyTree(SignInSecret.ProviderKey, false); } catch (Exception ex) { Log(ex.ToString()); }
        var dll = Path.Combine(SignInSecret.InstallDir, "DynamicIsland.FaceSignIn.dll");
        if (File.Exists(dll))
            Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"), $"/u \"{dll}\"");
        foreach (var p in new[] { SignInSecret.SecretPath, SignInSecret.ConfigPath }) try { File.Delete(p); } catch { }
        try { Directory.Delete(SignInSecret.InstallDir, true); }
        catch (Exception ex) { Log("files in use, removing at next restart: " + ex.Message); ScheduleDelete(SignInSecret.InstallDir); }
        Console.WriteLine("OK");
        return 0;
    }

    static int Run(string exe, string args)
    {
        var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        Log(exe + " " + args + "\n" + p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd());
        p.WaitForExit();
        return p.ExitCode;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool MoveFileEx(string src, string? dst, int flags);

    static void ScheduleDelete(string dir)
    {
        const int MOVEFILE_DELAY_UNTIL_REBOOT = 4;
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) { try { File.Delete(f); } catch { MoveFileEx(f, null, MOVEFILE_DELAY_UNTIL_REBOOT); } }
        foreach (var d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) MoveFileEx(d, null, MOVEFILE_DELAY_UNTIL_REBOOT);
        MoveFileEx(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
    }
}
