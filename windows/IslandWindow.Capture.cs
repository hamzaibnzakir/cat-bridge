using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DynamicIsland;

/// <summary>
/// Ctrl+Alt+T: drag over anything on screen and its text is copied (Windows' built-in OCR, works offline).
/// Ctrl+Alt+S: drag to take a screenshot; it's saved, copied and put on the shelf.
/// Screenshots you take with Win+Shift+S or Print Screen also land on the shelf.
/// </summary>
public partial class IslandWindow
{
    public static string ScreenshotDir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots", "Cat Bridge");
    bool _capturing;

    public async void CaptureText() => await Capture(ocr: true);
    public async void CaptureScreenshot() => await Capture(ocr: false);

    async Task Capture(bool ocr)
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            if (_mode == Mode.Expanded) { _st.ForceCollapse(); Reconcile(); }
            await Task.Delay(180); // let the island get out of the picture
            using var shot = GrabVirtualScreen();
            var rect = await RegionPicker.Pick(shot, ocr ? "Drag over the text you want to copy" : "Drag to take a screenshot");
            if (rect is not System.Drawing.Rectangle r || r.Width < 4 || r.Height < 4) return;
            using var crop = shot.Clone(r, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            if (ocr) await CopyText(crop);
            else SaveShot(crop);
        }
        catch (Exception ex) { App.Log(ex); ShowNotice("That didn't work", ex.Message); }
        finally { _capturing = false; }
    }

    static System.Drawing.Bitmap GrabVirtualScreen()
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen; // physical pixels (the app is DPI aware)
        var bmp = new System.Drawing.Bitmap(vs.Width, vs.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size);
        return bmp;
    }

    async Task CopyText(System.Drawing.Bitmap img)
    {
        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null) { ShowNotice("No text recognition language", "Add a language with OCR in Windows Settings → Time & language."); return; }
        using var ms = new MemoryStream();
        // small text reads better a little bigger
        using (var scaled = img.Width < 1200 ? new System.Drawing.Bitmap(img, img.Width * 2, img.Height * 2) : new System.Drawing.Bitmap(img))
            scaled.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        using var sb = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
        var res = await engine.RecognizeAsync(sb);
        var text = string.Join(Environment.NewLine, res.Lines.Select(l => l.Text)).Trim();
        if (text.Length == 0) { ShowToast(new ToastSpec("No text found there", "Try a bigger area", Glyph: "", Seconds: 3)); return; }
        try { Clipboard.SetText(text); } catch { }
        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        ShowToast(new ToastSpec($"Copied {words} word{(words == 1 ? "" : "s")}", text.Length > 80 ? text[..80] + "…" : text, Glyph: "", Seconds: 5,
            OnClick: () => SendClipboardToPhone()));
    }

    void SaveShot(System.Drawing.Bitmap img)
    {
        Directory.CreateDirectory(ScreenshotDir);
        var path = System.IO.Path.Combine(ScreenshotDir, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
        img.Save(path, ImageFormat.Png);
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(img.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            _ignoreNextClipboardImage = true;
            Clipboard.SetImage(src);
        }
        catch { }
        AddToShelf(new[] { path });
        ShowToast(new ToastSpec("Screenshot saved", "On the shelf and copied", Glyph: "", Seconds: 3,
            OnClick: () => { try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { } }));
    }

    // ───────── Win+Shift+S / Print Screen → shelf ─────────

    bool _ignoreNextClipboardImage;
    DateTime _lastShotSaved = DateTime.MinValue;

    void ClipboardImageToShelf()
    {
        if (!_s.ScreenshotShelf) return;
        if (_ignoreNextClipboardImage) { _ignoreNextClipboardImage = false; return; }
        try
        {
            if (!Clipboard.ContainsImage() || DateTime.Now - _lastShotSaved < TimeSpan.FromSeconds(1)) return;
            var img = Clipboard.GetImage();
            if (img == null || img.PixelWidth < 8) return;
            _lastShotSaved = DateTime.Now;
            Directory.CreateDirectory(ScreenshotDir);
            var path = System.IO.Path.Combine(ScreenshotDir, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            using (var fs = File.Create(path)) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img)); enc.Save(fs); }
            AddToShelf(new[] { path });
            ShowToast(new ToastSpec("Screenshot on the shelf", null, Glyph: "", Seconds: 2));
        }
        catch (Exception ex) { App.Log(ex); }
    }
}

/// <summary>Full-screen frozen screenshot; drag a rectangle; Esc or right-click cancels.</summary>
sealed class RegionPicker : Window
{
    readonly TaskCompletionSource<System.Drawing.Rectangle?> _done = new();
    readonly System.Drawing.Bitmap _shot;
    readonly Canvas _canvas = new();
    readonly Rectangle _sel = new() { Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x7A)), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xB3, 0x7A)), Visibility = Visibility.Collapsed };
    readonly System.Windows.Shapes.Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)) };
    Point? _start;

    RegionPicker(System.Drawing.Bitmap shot, string hint)
    {
        _shot = shot;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        var bg = Imaging.CreateBitmapSourceFromHBitmap(shot.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        Background = new ImageBrush(bg) { Stretch = Stretch.Fill };
        _dim.Data = new RectangleGeometry(new Rect(0, 0, Width, Height));
        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_sel);
        var tip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x18, 0x18, 0x1B)), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 9, 16, 9),
            Child = new TextBlock { Text = hint + "   ·   Esc to cancel", Foreground = Brushes.White, FontSize = 14 },
        };
        Canvas.SetTop(tip, 24);
        tip.Loaded += (_, _) => Canvas.SetLeft(tip, (Width - tip.ActualWidth) / 2);
        _canvas.Children.Add(tip);
        Content = _canvas;
        MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(this); CaptureMouse(); };
        MouseMove += (_, e) => { if (_start is Point s) Draw(s, e.GetPosition(this)); };
        MouseLeftButtonUp += (_, e) =>
        {
            ReleaseMouseCapture();
            if (_start is not Point s) return;
            var r = new Rect(s, e.GetPosition(this));
            // window DIPs → screenshot pixels
            double sx = _shot.Width / ActualWidth, sy = _shot.Height / ActualHeight;
            Finish(new System.Drawing.Rectangle((int)(r.X * sx), (int)(r.Y * sy), Math.Max(1, (int)(r.Width * sx)), Math.Max(1, (int)(r.Height * sy))));
        };
        MouseRightButtonUp += (_, _) => Finish(null);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };
        Deactivated += (_, _) => Finish(null);
    }

    void Draw(Point a, Point b)
    {
        var r = new Rect(a, b);
        _sel.Visibility = Visibility.Visible;
        Canvas.SetLeft(_sel, r.X); Canvas.SetTop(_sel, r.Y); _sel.Width = r.Width; _sel.Height = r.Height;
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)), new RectangleGeometry(r));
    }

    void Finish(System.Drawing.Rectangle? r)
    {
        if (_done.Task.IsCompleted) return;
        if (r is System.Drawing.Rectangle rr) r = System.Drawing.Rectangle.Intersect(rr, new System.Drawing.Rectangle(0, 0, _shot.Width, _shot.Height));
        _done.TrySetResult(r);
        Close();
    }

    public static Task<System.Drawing.Rectangle?> Pick(System.Drawing.Bitmap shot, string hint)
    {
        var w = new RegionPicker(shot, hint);
        w.Show();
        w.Activate();
        w.Focus();
        return w._done.Task;
    }
}
