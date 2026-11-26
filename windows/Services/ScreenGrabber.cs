using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DynamicIsland;

/// <summary>
/// Captures the main screen for the iPhone's Remote tab: scales it down in one GDI StretchBlt (HALFTONE),
/// draws the mouse pointer into it, and encodes a JPEG. Next() returns null while nothing on screen has changed,
/// so a still desktop costs almost nothing to watch. One instance per viewer; not thread-safe.
/// </summary>
public sealed class ScreenGrabber : Bridge.IScreenFeed
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr old);
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, uint step, IntPtr brush, uint flags);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int size, width, height; public short planes, bitCount; public int compression, sizeImage, xPels, yPels, clrUsed, clrImportant; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFO { public BITMAPINFOHEADER h; public int colors; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int size, flags; public IntPtr cursor; public POINT pos; }
    [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool isIcon; public int hotX, hotY; public IntPtr mask, color; }

    const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000, DI_NORMAL = 0x3;
    const int HALFTONE = 4, CURSOR_SHOWING = 1;

    static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    IntPtr _mem, _dib, _old, _bits;
    int _w, _h;
    ulong _lastHash;

    /// <summary>The main screen's size in real pixels (the app is per-monitor DPI aware).</summary>
    public (int Width, int Height) Size()
    {
        var b = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
        return (b.Width, b.Height);
    }

    /// <summary>Forget the last frame, so the next call sends one even if nothing changed.</summary>
    public void Reset() => _lastHash = 0;

    /// <summary>A JPEG of the screen at most <paramref name="maxWidth"/> wide, or null when nothing changed since the last one.</summary>
    public byte[]? Next(int maxWidth, int quality)
    {
        var src = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
        if (src.Width <= 0 || src.Height <= 0) return null;
        double scale = Math.Min(1.0, Math.Clamp(maxWidth, 320, 3840) / (double)src.Width);
        int w = Math.Max(2, (int)Math.Round(src.Width * scale)) & ~1, h = Math.Max(2, (int)Math.Round(src.Height * scale)) & ~1;
        Ensure(w, h);

        var screen = GetDC(IntPtr.Zero);
        try
        {
            SetStretchBltMode(_mem, HALFTONE);
            SetBrushOrgEx(_mem, 0, 0, IntPtr.Zero);
            // CAPTUREBLT includes layered windows (the island itself, menus, tooltips)
            if (!StretchBlt(_mem, 0, 0, w, h, screen, src.X, src.Y, src.Width, src.Height, SRCCOPY | CAPTUREBLT)) return null;
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }

        DrawPointer(src, scale);

        var hash = Hash();
        if (hash == _lastHash) return null;
        _lastHash = hash;

        using var bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppRgb, _bits);
        using var ms = new MemoryStream(256 * 1024);
        using var ps = new EncoderParameters(1);
        ps.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 20, 95));
        bmp.Save(ms, Jpeg, ps);
        return ms.ToArray();
    }

    void DrawPointer(Rectangle src, double scale)
    {
        var ci = new CURSORINFO { size = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || (ci.flags & CURSOR_SHOWING) == 0 || ci.cursor == IntPtr.Zero) return;
        int hx = 0, hy = 0;
        if (GetIconInfo(ci.cursor, out var ii))
        {
            hx = ii.hotX; hy = ii.hotY;
            if (ii.mask != IntPtr.Zero) DeleteObject(ii.mask);
            if (ii.color != IntPtr.Zero) DeleteObject(ii.color);
        }
        // keep the pointer readable on a phone even when the picture is scaled right down
        double k = Math.Max(scale, 0.75);
        int size = (int)Math.Round(Math.Max(GetSystemMetrics(13), 32) * k);
        int x = (int)Math.Round((ci.pos.x - src.X) * scale - hx * k), y = (int)Math.Round((ci.pos.y - src.Y) * scale - hy * k);
        DrawIconEx(_mem, x, y, ci.cursor, size, size, 0, IntPtr.Zero, DI_NORMAL);
    }

    unsafe ulong Hash()
    {
        var p = (ulong*)_bits;
        long n = (long)_w * _h / 2;
        ulong h = 14695981039346656037UL;
        for (long i = 0; i < n; i++) { h ^= p[i]; h *= 1099511628211UL; }
        return h == 0 ? 1 : h;
    }

    void Ensure(int w, int h)
    {
        if (w == _w && h == _h && _dib != IntPtr.Zero) return;
        Free();
        var screen = GetDC(IntPtr.Zero);
        try
        {
            _mem = CreateCompatibleDC(screen);
            var bmi = new BITMAPINFO { h = new BITMAPINFOHEADER { size = Marshal.SizeOf<BITMAPINFOHEADER>(), width = w, height = -h, planes = 1, bitCount = 32 } };
            _dib = CreateDIBSection(screen, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero) throw new InvalidOperationException("Couldn't make a screen buffer.");
            _old = SelectObject(_mem, _dib);
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
        _w = w; _h = h; _lastHash = 0;
    }

    void Free()
    {
        if (_mem != IntPtr.Zero) { if (_old != IntPtr.Zero) SelectObject(_mem, _old); DeleteDC(_mem); }
        if (_dib != IntPtr.Zero) DeleteObject(_dib);
        _mem = _dib = _old = _bits = IntPtr.Zero; _w = _h = 0;
    }

    public void Dispose() => Free();
}
