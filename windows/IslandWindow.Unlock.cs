using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using DynamicIsland.Bridge;
using DynamicIsland.Shared;
using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>Unlock with iPhone (Face ID), waking the screen, and the info the iPhone needs for Wake-on-LAN.</summary>
public partial class IslandWindow
{
    // ───────── pairing an iPhone for unlock (approved in Cat Center) ─────────

    public sealed record UnlockRequest(string Name, byte[] PublicKey, string Code, DateTime Time);
    public UnlockRequest? PendingUnlockPhone { get; private set; }
    readonly HashSet<string> _deniedKeys = new();
    public event Action? UnlockPhonesChanged;

    (int status, object body) IBridgeHost.UnlockRegister(string name, string publicKey)
    {
        byte[] key;
        try { key = Convert.FromBase64String(publicKey); } catch { return (400, new { error = "Bad key." }); }
        if (key.Length != 65 || key[0] != 4) return (400, new { error = "Bad key." });
        var id = PhoneTrust.KeyId(key);
        if (PhoneTrust.Load().Any(p => p.Id == id)) return (200, new { state = "trusted", code = PhoneTrust.PairingCode(key) });
        var req = new UnlockRequest(name, key, PhoneTrust.PairingCode(key), DateTime.Now);
        UiAsync(() =>
        {
            PendingUnlockPhone = req;
            _deniedKeys.Remove(id);
            UnlockPhonesChanged?.Invoke();
            ShowToast(new ToastSpec($"{name} wants to unlock this PC", $"Check the code {req.Code[..3]} {req.Code[3..]} matches, then allow it in Cat Center",
                Glyph: "", Seconds: 12, OnClick: () => Center.CenterWindow.Open(this, "face")));
            Center.CenterWindow.Open(this, "face");
        });
        return (200, new { state = "pending", code = req.Code });
    }

    object IBridgeHost.UnlockRegistration(string publicKey)
    {
        try
        {
            var key = Convert.FromBase64String(publicKey);
            var id = PhoneTrust.KeyId(key);
            if (PhoneTrust.Load().Any(p => p.Id == id)) return new { state = "trusted" };
            if (_deniedKeys.Contains(id)) return new { state = "denied" };
            if (PendingUnlockPhone != null && PhoneTrust.KeyId(PendingUnlockPhone.PublicKey) == id) return new { state = "pending", code = PendingUnlockPhone.Code };
        }
        catch { }
        return new { state = "none" };
    }

    /// Cat Center → Allow: writes the key to the admin-only trust file (one admin prompt).
    public async Task<(bool ok, string message)> AllowUnlockPhone()
    {
        var req = PendingUnlockPhone;
        if (req == null) return (false, "Nothing to allow.");
        if (!FaceId.FaceSignInWindow.Installed) return (false, "Turn on Cat Face on the sign-in screen first (Face unlock → Use on sign-in screen). Your iPhone unlocks through it.");
        var (ok, msg) = await FaceId.FaceSignInWindow.RunElevated($"--trust-phone {Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(req.Name))} {Convert.ToBase64String(req.PublicKey)}");
        if (ok) { PendingUnlockPhone = null; UnlockPhonesChanged?.Invoke(); return (true, $"{req.Name} can now unlock this PC."); }
        return (false, msg);
    }

    public void DenyUnlockPhone()
    {
        if (PendingUnlockPhone is { } r) _deniedKeys.Add(PhoneTrust.KeyId(r.PublicKey));
        PendingUnlockPhone = null;
        UnlockPhonesChanged?.Invoke();
    }

    public async Task<(bool ok, string message)> RemoveUnlockPhone(string id)
    {
        var (ok, msg) = await FaceId.FaceSignInWindow.RunElevated($"--untrust-phone {id}");
        UnlockPhonesChanged?.Invoke();
        return (ok, ok ? "Removed." : msg);
    }

    // ───────── unlocking ─────────

    async Task<(int status, object body)> IBridgeHost.UnlockChallenge()
    {
        WakeScreen();
        var reply = await PhoneUnlockRelay.Ask("CHALLENGE");
        if (reply == null)
            return _sessionLocked
                ? (409, new { error = "The lock screen isn't ready. Tap the screen on the laptop, or turn on Cat Face on the sign-in screen." })
                : (409, new { error = "Your laptop is already unlocked." });
        var p = reply.Split(' ');
        if (p.Length < 3 || p[0] != "NONCE") return (502, new { error = "The sign-in tile didn't answer properly." });
        return (200, new { nonce = p[1], machine = p[2] });
    }

    async Task<(int status, object body)> IBridgeHost.UnlockApprove(string nonce, string signature)
    {
        var reply = await PhoneUnlockRelay.Ask($"APPROVE {nonce} {signature}", 3000);
        if (reply == null) return (409, new { error = "The lock screen went away. Is the laptop already unlocked?" });
        if (reply.StartsWith("OK")) return (200, new { ok = true, phone = reply.Length > 3 ? reply[3..] : "" });
        var why = reply.Length > 3 ? reply[3..] : "";
        return (403, new
        {
            error = why switch
            {
                "not-trusted" => "This iPhone isn't allowed to unlock the laptop. Set it up again in Settings.",
                "expired" => "That took too long. Try again.",
                "too-many-tries" => "Too many tries. Use your password on the laptop.",
                _ => "The laptop said no (" + why + ").",
            }
        });
    }

    // ───────── "Unlock with my iPhone" pressed on the lock screen → tap on the phone ─────────

    bool _sessionLocked;
    DispatcherTimer? _lockPoll;
    DateTime _lastUnlockPing = DateTime.MinValue;

    void WireUnlock()
    {
        ApplyStayAwake();
        if (_s.LostMode) ApplyLostMode();
        SystemEvents.SessionSwitch += (_, e) => Dispatcher.InvokeAsync(() =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect) { _sessionLocked = true; StartLockPoll(); }
            else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.SessionLogon) { _sessionLocked = false; _lockPoll?.Stop(); }
        });
    }

    void StartLockPoll()
    {
        if (PhoneTrust.Load().Count == 0) return;
        _lockPoll ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _lockPoll.Tick -= LockPollTick;
        _lockPoll.Tick += LockPollTick;
        _lockPoll.Start();
    }

    async void LockPollTick(object? sender, EventArgs e)
    {
        if (!_sessionLocked) { _lockPoll?.Stop(); return; }
        var r = await PhoneUnlockRelay.Ask("POLL", 800);
        if (r == "WANT" && DateTime.Now - _lastUnlockPing > TimeSpan.FromSeconds(25))
        {
            _lastUnlockPing = DateTime.Now;
            Push.Send($"Unlock {Environment.MachineName}?", "Tap here, then look at your iPhone.", "unlock", url: "catbridge://unlock", tag: "unlock");
        }
    }

    // ───────── remote trackpad + keyboard ─────────

    string? IBridgeHost.Input(string command) => _sessionLocked ? "locked" : InputInjector.Run(command);

    string IBridgeHost.InputHello()
    {
        var b = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
        Dispatcher.InvokeAsync(() => ShowToast(new ToastSpec("Your iPhone is the trackpad", "Mouse and keyboard from Cat Bridge", Glyph: "\uEFA5", Seconds: 2.5)));
        return $"hi {Environment.MachineName} {b?.Width ?? 0} {b?.Height ?? 0}";
    }

    // ───────── wake ─────────

    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;

    /// Turns the screen on (the iPhone's "Wake" button, and before an unlock).
    public void WakeScreen()
    {
        try
        {
            SetThreadExecutionState(ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED); // resets the idle timers: display comes back
            SendMessageTimeout((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF170, (IntPtr)(-1), 0x2, 300, out _); // SC_MONITORPOWER: on
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void IBridgeHost.Wake() => Dispatcher.InvokeAsync(() => { WakeScreen(); if (!_sessionLocked) ShowToast(new ToastSpec("Woken up by your iPhone", null, Glyph: "", Seconds: 2.5)); });

    /// "Stay reachable": while plugged in, don't let Windows sleep (the screen can still turn off).
    public void ApplyStayAwake()
    {
        bool want = _s.StayAwakeOnPower && _power.OnAC;
        SetThreadExecutionState(want ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);
    }

    /// MAC and LAN addresses, so the iPhone can send a Wake-on-LAN packet when it's on the same network.
    object IBridgeHost.NetInfo()
    {
        var list = new List<object>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet)) continue;
            if (nic.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) || nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
            var mac = nic.GetPhysicalAddress().GetAddressBytes();
            if (mac.Length != 6 || mac.All(b => b == 0)) continue;
            var ips = new List<string>(); var bcasts = new List<string>();
            foreach (var u in nic.GetIPProperties().UnicastAddresses)
            {
                if (u.Address.AddressFamily != AddressFamily.InterNetwork || Tailnet.IsTailscale(u.Address)) continue;
                ips.Add(u.Address.ToString());
                var a = u.Address.GetAddressBytes(); var m = u.IPv4Mask.GetAddressBytes();
                bcasts.Add(new System.Net.IPAddress(new[] { (byte)(a[0] | ~m[0]), (byte)(a[1] | ~m[1]), (byte)(a[2] | ~m[2]), (byte)(a[3] | ~m[3]) }).ToString());
            }
            list.Add(new { name = nic.Name, wifi = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211, up = nic.OperationalStatus == OperationalStatus.Up,
                mac = string.Join(":", mac.Select(b => b.ToString("X2"))), ips, broadcasts = bcasts });
        }
        return new { machine = Environment.MachineName, adapters = list };
    }
}
