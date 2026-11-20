using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Taskbar;

/// <summary>Crisp app icons from the Windows shell (works for normal apps and Store apps), with real transparency.</summary>
public static class ShellIcons
{
    static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory f);

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbmp); }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, ref BITMAP b);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }

    /// path: an exe/file path, or "shell:AppsFolder\{AUMID or app id}".
    public static ImageSource? Get(string path, int size = 48)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Cache.GetOrAdd(path + "|" + size, _ => Load(path, size));
    }

    static ImageSource? Load(string path, int size)
    {
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var f);
            try
            {
                if (f.GetImage(new SIZE(size, size), 0x1 | 0x4, out hbmp) != 0 || hbmp == IntPtr.Zero) // BIGGERSIZEOK | ICONONLY
                    if (f.GetImage(new SIZE(size, size), 0x1, out hbmp) != 0 || hbmp == IntPtr.Zero) return null;
            }
            finally { Marshal.ReleaseComObject(f); }
            return FromHBitmap(hbmp);
        }
        catch { return null; }
        finally { if (hbmp != IntPtr.Zero) DeleteObject(hbmp); }
    }

    /// Copies a 32-bit HBITMAP top-down, keeping its alpha (CreateBitmapSourceFromHBitmap would turn it black).
    static ImageSource? FromHBitmap(IntPtr hbmp)
    {
        var bm = new BITMAP();
        if (GetObject(hbmp, Marshal.SizeOf<BITMAP>(), ref bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bits = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try { if (GetDIBits(dc, hbmp, 0, (uint)h, bits, ref bi, 0) == 0) return null; }
        finally { ReleaseDC(IntPtr.Zero, dc); }
        bool anyAlpha = false;
        for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bits, w * 4);
        src.Freeze();
        return src;
    }
}
