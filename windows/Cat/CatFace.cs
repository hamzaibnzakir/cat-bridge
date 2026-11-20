using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>The cat's face as a little live control: blinks, breathes, floats z's while asleep.</summary>
public sealed class CatFace : FrameworkElement
{
    public static CatPalette CurrentPalette = CatArt.Palette("ginger");

    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(nameof(Mood), typeof(FaceMood), typeof(CatFace),
        new FrameworkPropertyMetadata(FaceMood.Awake, FrameworkPropertyMetadataOptions.AffectsRender));
    public FaceMood Mood { get => (FaceMood)GetValue(MoodProperty); set => SetValue(MoodProperty, value); }

    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly DateTime _born = DateTime.Now;
    DateTime _nextBlink = DateTime.Now.AddSeconds(2);
    double _eye = 1, _twitch;
    DateTime _nextTwitch = DateTime.Now.AddSeconds(5);

    public CatFace()
    {
        _t.Tick += (_, _) => Step();
        IsVisibleChanged += (_, _) => { if (IsVisible) _t.Start(); else _t.Stop(); };
        IsHitTestVisible = false;
    }

    void Step()
    {
        var now = DateTime.Now;
        if (Mood == FaceMood.Awake || Mood == FaceMood.Surprised)
        {
            double since = (now - _nextBlink).TotalMilliseconds;
            if (since >= 0 && since < 160) _eye = Math.Abs(since - 80) / 80.0;
            else if (since >= 160) { _eye = 1; _nextBlink = now.AddMilliseconds(Random.Shared.Next(2500, 6000)); }
        }
        double ts = (now - _nextTwitch).TotalMilliseconds;
        _twitch = ts >= 0 && ts < 220 ? Math.Sin(ts / 220 * Math.PI) : 0;
        if (ts >= 220) _nextTwitch = now.AddMilliseconds(Random.Shared.Next(4000, 11000));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double t = (DateTime.Now - _born).TotalSeconds;
        double breathe = Mood == FaceMood.Sleep ? 1 + 0.035 * Math.Sin(t * 2.2) : 1;
        double r = Math.Min(w, h * 1.05) * 0.36;
        var c = new Point(w / 2, h * 0.6);
        dc.PushTransform(new ScaleTransform(breathe, breathe, c.X, c.Y + r));
        var pal = CurrentPalette;
        if (pal.Fur.R + pal.Fur.G + pal.Fur.B < 200) pal = pal with { Outline = Color.FromRgb(0x6A, 0x6A, 0x74) }; // stay visible on the black island
        CatArt.DrawFace(dc, c, r, pal, Mood, _eye, 0, _twitch, whiskers: r > 12);
        dc.Pop();

        if (Mood == FaceMood.Sleep)
        {
            double phase = (t % 2.4) / 2.4;
            var ft = new FormattedText("z", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), r * 0.7, new SolidColorBrush(Color.FromArgb((byte)(220 * (1 - phase)), 255, 255, 255)), 1.25);
            dc.DrawText(ft, new Point(c.X + r * 0.9 + phase * r * 0.3, c.Y - r * 0.9 - phase * r * 0.8));
        }
    }
}
