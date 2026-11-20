using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DynamicIsland.Bridge;

/// <summary>Finds this laptop's Tailscale addresses (100.64.0.0/10 and fd7a:115c:a1e0::/48) without needing the CLI.</summary>
public static class Tailnet
{
    public static bool IsTailscale(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        var b = a.GetAddressBytes();
        if (a.AddressFamily == AddressFamily.InterNetwork) return b[0] == 100 && (b[1] & 0xC0) == 64;
        return b.Length == 16 && b[0] == 0xfd && b[1] == 0x7a && b[2] == 0x11 && b[3] == 0x5c && b[4] == 0xa1 && b[5] == 0xe0;
    }

    /// Tailscale addresses currently assigned to this PC, IPv4 first.
    public static List<IPAddress> Addresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var u in nic.GetIPProperties().UnicastAddresses)
                    if (IsTailscale(u.Address)) list.Add(u.Address);
            }
        }
        catch { }
        return list.Distinct().OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToList();
    }

    public static IPAddress? IPv4 => Addresses().FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

    /// Adds an inbound firewall rule for the bridge port that only accepts Tailscale addresses. Shows the Windows admin prompt.
    public static async Task<(bool ok, string message)> AllowThroughFirewall(int port)
    {
        const string name = "Dynamic Island Cat Bridge";
        var exe = Environment.ProcessPath ?? "";
        var cmd = $"/c netsh advfirewall firewall delete rule name=\"{name}\" >nul 2>&1 & " +
                  $"netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={port} remoteip=100.64.0.0/10,fd7a:115c:a1e0::/48 profile=any" +
                  (exe.Length > 0 ? $" program=\"{exe}\"" : "");
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", cmd) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p == null) return (false, "Windows didn't start the firewall tool.");
            await Task.Run(() => p.WaitForExit(20000));
            return p.HasExited && p.ExitCode == 0 ? (true, "Done. Only your Tailscale devices can reach the bridge port.") : (false, $"The firewall tool stopped (code {(p.HasExited ? p.ExitCode : -1)}).");
        }
        catch (System.ComponentModel.Win32Exception) { return (false, "You said no to the admin prompt, so nothing changed."); }
        catch (Exception ex) { App.Log(ex); return (false, ex.Message); }
    }

    public static bool FirewallRuleExists()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("netsh", "advfirewall firewall show rule name=\"Dynamic Island Cat Bridge\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            if (p == null) return false;
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000);
            return p.ExitCode == 0 && o.Contains("Dynamic Island Cat Bridge");
        }
        catch { return false; }
    }
}
