using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DynamicIsland.Taskbar;

/// <summary>A live preview of a window (the same DWM thumbnail the taskbar uses), shown under the island while you hover an app.</summary>
public sealed class PreviewWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; public RECT(int l, int t, int r, int b) { this.l = l; this.t = t; this.r = r; this.b = b; } }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)]
    struct DWM_THUMBNAIL_PROPERTIES { public int dwFlags; public RECT rcDestination, rcSource; public byte opacity; [MarshalAs(UnmanagedType.Bool)] public bool fVisible, fSourceClientAreaOnly; }
    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES p);
    [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE s);

    const double W_ = 300, ThumbH = 172;
    readonly TextBlock _title = new() { Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 9, 12, 0) };
    IntPtr _thumb, _hwnd, _src;

    public PreviewWindow()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        Width = W_; Height = ThumbH + 44; Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1F));
        Content = new Grid { Children = { _title } };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = (long)W.GetWindowLongPtr(_hwnd, W.GWL_EXSTYLE);
            Native.SetWindowLongPtr(_hwnd, W.GWL_EXSTYLE, new IntPtr(ex | W.WS_EX_NOACTIVATE | W.WS_EX_TOOLWINDOW));
            int round = 2; W.DwmSetWindowAttribute(_hwnd, 33, ref round, 4);   // rounded corners (Windows 11)
            int dark = 1; W.DwmSetWindowAttribute(_hwnd, 20, ref dark, 4);
        };
        IsHitTestVisible = false;
    }

    /// Shows the preview centred under `anchor` (screen DIPs).
    public void ShowFor(AppWindow w, Point anchor)
    {
        _title.Text = w.Title;
        Left = Math.Max(SystemParameters.VirtualScreenLeft + 4, anchor.X - W_ / 2);
        Top = anchor.Y + 8;
        if (!IsVisible) Show();
        if (_src != w.Hwnd) { Unregister(); _src = w.Hwnd; }
        if (_thumb == IntPtr.Zero && DwmRegisterThumbnail(_hwnd, w.Hwnd, out _thumb) != 0) { _thumb = IntPtr.Zero; return; }
        Fit();
    }

    void Fit()
    {
        if (_thumb == IntPtr.Zero) return;
        DwmQueryThumbnailSourceSize(_thumb, out var s);
        var dpi = VisualTreeHelper.GetDpi(this);
        double maxW = (W_ - 20) * dpi.DpiScaleX, maxH = ThumbH * dpi.DpiScaleY;
        double scale = s.cx > 0 && s.cy > 0 ? Math.Min(maxW / s.cx, maxH / s.cy) : 1;
        int tw = (int)(s.cx * scale), th = (int)(s.cy * scale);
        int left = (int)((W_ * dpi.DpiScaleX - tw) / 2), top = (int)(34 * dpi.DpiScaleY + (maxH - th) / 2);
        var p = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = 0x1 | 0x4 | 0x8 | 0x10, rcDestination = new RECT(left, top, left + tw, top + th),
            opacity = 255, fVisible = true, fSourceClientAreaOnly = false,
        };
        DwmUpdateThumbnailProperties(_thumb, ref p);
    }

    void Unregister() { if (_thumb != IntPtr.Zero) { DwmUnregisterThumbnail(_thumb); _thumb = IntPtr.Zero; } }

    public void HidePreview() { Unregister(); _src = IntPtr.Zero; if (IsVisible) Hide(); }
}
