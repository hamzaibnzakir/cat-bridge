using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace DynamicIsland;

/// <summary>
/// Turns the iPhone's trackpad and keyboard into real mouse and keyboard input (SendInput).
/// Commands, one per line:
///   m dx dy            move the pointer (pixels, fractions kept)      s dy [dx]    scroll (120 = one notch)
///   d|u|c l|r|m        button down / up / click                       dc           double-click
///   t text…            type text                                      k name       press a key (enter, esc, tab, left, f5, volup…)
///   h ctrl+shift+t     a shortcut (ctrl, alt, shift, win + one key)   z in|out     zoom (Ctrl + wheel)
///   a x y              put the pointer at a spot on the main screen (0–1 across, 0–1 down; from tapping the live screen)
/// Windows won't let any app type into the lock screen or into apps running as administrator.
/// </summary>
public static class InputInjector
{
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION u; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    const uint ABSOLUTE = 0x8000, MOVE = 0x1, LDOWN = 0x2, LUP = 0x4, RDOWN = 0x8, RUP = 0x10, MDOWN = 0x20, MUP = 0x40, WHEEL = 0x800, HWHEEL = 0x1000;
    const uint KEYUP = 0x2, UNICODE = 0x4, EXTENDED = 0x1;

    static double _fx, _fy;

    static void Send(params INPUT[] inputs) { if (inputs.Length > 0) SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()); }
    static INPUT Mouse(uint flags, int dx = 0, int dy = 0, int data = 0) => new() { type = 0, u = new UNION { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = unchecked((uint)data), dwFlags = flags } } };
    static INPUT Key(ushort vk, bool up, ushort scan = 0, uint extra = 0) => new() { type = 1, u = new UNION { ki = new KEYBDINPUT { vk = vk, scan = scan, flags = (up ? KEYUP : 0) | extra } } };

    static readonly Dictionary<string, ushort> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = 0x0D, ["return"] = 0x0D, ["backspace"] = 0x08, ["tab"] = 0x09, ["esc"] = 0x1B, ["escape"] = 0x1B, ["space"] = 0x20,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28, ["home"] = 0x24, ["end"] = 0x23, ["pgup"] = 0x21, ["pgdn"] = 0x22,
        ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D, ["win"] = 0x5B, ["ctrl"] = 0x11, ["alt"] = 0x12, ["shift"] = 0x10,
        ["volup"] = 0xAF, ["voldown"] = 0xAE, ["mute"] = 0xAD, ["playpause"] = 0xB3, ["next"] = 0xB0, ["prev"] = 0xB1,
        ["printscreen"] = 0x2C, ["menu"] = 0x5D, ["capslock"] = 0x14,
    };
    static readonly HashSet<ushort> Extended = new() { 0x25, 0x26, 0x27, 0x28, 0x24, 0x23, 0x21, 0x22, 0x2E, 0x2D, 0x5B, 0x5D, 0xAF, 0xAE, 0xAD, 0xB3, 0xB0, 0xB1 };

    static ushort? Vk(string name)
    {
        if (Keys.TryGetValue(name, out var v)) return v;
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
            var map = new Dictionary<char, ushort> { [','] = 0xBC, ['.'] = 0xBE, ['/'] = 0xBF, [';'] = 0xBA, ['\''] = 0xDE, ['['] = 0xDB, [']'] = 0xDD, ['-'] = 0xBD, ['='] = 0xBB, ['`'] = 0xC0, ['\\'] = 0xDC };
            if (map.TryGetValue(c, out var m)) return m;
        }
        if (name.Length is 2 or 3 && (name[0] == 'f' || name[0] == 'F') && int.TryParse(name[1..], out int f) && f is >= 1 and <= 24) return (ushort)(0x70 + f - 1);
        return null;
    }

    static INPUT[] Tap(ushort vk)
    {
        uint ext = Extended.Contains(vk) ? EXTENDED : 0;
        return new[] { Key(vk, false, 0, ext), Key(vk, true, 0, ext) };
    }

    static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? Math.Clamp(d, -5000, 5000) : 0;

    /// Runs one command. Returns an error line for bad commands, otherwise null.
    public static string? Run(string line)
    {
        var sp = line.IndexOf(' ');
        var cmd = sp < 0 ? line : line[..sp];
        var rest = sp < 0 ? "" : line[(sp + 1)..];
        var a = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (cmd)
        {
            case "m":
                if (a.Length < 2) return "err m";
                _fx += D(a[0]); _fy += D(a[1]);
                int ix = (int)Math.Truncate(_fx), iy = (int)Math.Truncate(_fy);
                _fx -= ix; _fy -= iy;
                if (ix != 0 || iy != 0) Send(Mouse(MOVE, ix, iy));
                return null;
            case "a":
            {
                if (a.Length < 2) return "err a";
                // absolute coordinates are 0–65535 across the main monitor, the same screen the Remote tab shows
                double nx = Math.Clamp(D(a[0]), 0, 1), ny = Math.Clamp(D(a[1]), 0, 1);
                _fx = _fy = 0;
                Send(Mouse(MOVE | ABSOLUTE, (int)Math.Round(nx * 65535), (int)Math.Round(ny * 65535)));
                return null;
            }
            case "d": case "u": case "c":
            {
                var b = a.FirstOrDefault() ?? "l";
                (uint down, uint upf) = b switch { "r" => (RDOWN, RUP), "m" => (MDOWN, MUP), _ => (LDOWN, LUP) };
                if (cmd == "d") Send(Mouse(down)); else if (cmd == "u") Send(Mouse(upf)); else Send(Mouse(down), Mouse(upf));
                return null;
            }
            case "dc":
                Send(Mouse(LDOWN), Mouse(LUP), Mouse(LDOWN), Mouse(LUP));
                return null;
            case "s":
            {
                int dy = (int)Math.Round(D(a.ElementAtOrDefault(0) ?? "0")), dx = (int)Math.Round(D(a.ElementAtOrDefault(1) ?? "0"));
                var list = new List<INPUT>();
                if (dy != 0) list.Add(Mouse(WHEEL, data: dy));
                if (dx != 0) list.Add(Mouse(HWHEEL, data: dx));
                Send(list.ToArray());
                return null;
            }
            case "z":
            {
                int d = rest.Trim() == "out" ? -120 : 120;
                Send(Key(0x11, false), Mouse(WHEEL, data: d), Key(0x11, true));
                return null;
            }
            case "t":
            {
                if (rest.Length > 4000) rest = rest[..4000];
                var list = new List<INPUT>();
                foreach (var ch in rest.Replace("\r\n", "\n"))
                {
                    if (ch == '\n') { list.AddRange(Tap(0x0D)); continue; }
                    if (ch == '\t') { list.AddRange(Tap(0x09)); continue; }
                    list.Add(Key(0, false, ch, UNICODE));
                    list.Add(Key(0, true, ch, UNICODE));
                }
                Send(list.ToArray());
                return null;
            }
            case "k":
            {
                var vk = Vk(rest.Trim());
                if (vk == null) return "err key " + rest.Trim();
                Send(Tap(vk.Value));
                return null;
            }
            case "h":
            {
                var parts = rest.Trim().ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "err h";
                var vks = new List<ushort>();
                foreach (var p in parts) { var v = Vk(p); if (v == null) return "err key " + p; vks.Add(v.Value); }
                var list = new List<INPUT>();
                foreach (var v in vks) list.Add(Key(v, false, 0, Extended.Contains(v) ? EXTENDED : 0));
                for (int i = vks.Count - 1; i >= 0; i--) list.Add(Key(vks[i], true, 0, Extended.Contains(vks[i]) ? EXTENDED : 0));
                Send(list.ToArray());
                return null;
            }
            case "ping": return "pong";
            default: return "err unknown " + cmd;
        }
    }
}
