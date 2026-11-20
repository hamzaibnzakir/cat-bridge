using System;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

public enum FaceMood { Awake, Sleep, Happy, Surprised, Content }

public sealed record CatPalette(Color Fur, Color FurDark, Color Belly, Color InnerEar, Color Nose, Color Eye, Color Outline, Color Blush);

/// <summary>Original cat character, drawn with vectors so it stays crisp at any size and DPI.</summary>
public static class CatArt
{
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static CatPalette Palette(string? name) => (name ?? "").ToLowerInvariant() switch
    {
        "black" => new(C("#2E2E33"), C("#1E1E22"), C("#45454C"), C("#F2A7B5"), C("#F28CA0"), C("#E8D44D"), C("#141416"), C("#F28CA0")),
        "white" => new(C("#F8F4EF"), C("#E2DBD2"), C("#FFFFFF"), C("#F7B6C2"), C("#F28CA0"), C("#34302D"), C("#B8AEA2"), C("#F7A8B8")),
        "grey" or "gray" => new(C("#A3A9B1"), C("#858B93"), C("#D3D7DB"), C("#F2A7B5"), C("#E98A9C"), C("#2E3133"), C("#5F656C"), C("#F2A0B0")),
        _ => new(C("#F4A35D"), C("#DE8740"), C("#FFE4C7"), C("#FFB8C4"), C("#F28CA0"), C("#2B2420"), C("#A85E22"), C("#FF9AAE")),
    };

    static readonly System.Collections.Generic.Dictionary<Color, SolidColorBrush> Cache = new();
    public static SolidColorBrush B(Color c, double opacity = 1)
    {
        if (opacity >= 1 && Cache.TryGetValue(c, out var b)) return b;
        var nb = new SolidColorBrush(c) { Opacity = opacity };
        nb.Freeze();
        if (opacity >= 1) Cache[c] = nb;
        return nb;
    }

    static Pen P(Color c, double w, double opacity = 1)
    {
        var p = new Pen(B(c, opacity), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    static StreamGeometry Tri(Point a, Point b, Point c)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open()) { ctx.BeginFigure(a, true, true); ctx.LineTo(b, true, true); ctx.LineTo(c, true, true); }
        g.Freeze();
        return g;
    }

    /// <summary>Draws the cat's head. look = -1..1 shifts the features sideways (profile turn).</summary>
    public static void DrawFace(DrawingContext dc, Point c, double r, CatPalette p, FaceMood mood, double eyeOpen = 1, double look = 0, double earTwitch = 0, bool whiskers = true)
    {
        double ow = Math.Max(1, r * 0.075);
        var outline = P(p.Outline, ow);
        double lx = look * r * 0.32;

        // ears
        void Ear(int side, double twitch)
        {
            double s = side;
            var a = new Point(c.X + s * 0.88 * r, c.Y - 0.25 * r);
            var tip = new Point(c.X + s * (0.80 * r) + lx * 0.3, c.Y - (1.18 - twitch * 0.18) * r);
            var b = new Point(c.X + s * 0.22 * r, c.Y - 0.82 * r);
            dc.DrawGeometry(B(p.Fur), outline, Tri(a, tip, b));
            var ia = new Point(c.X + s * 0.74 * r, c.Y - 0.42 * r);
            var it = new Point(c.X + s * 0.76 * r + lx * 0.3, c.Y - (0.98 - twitch * 0.15) * r);
            var ib = new Point(c.X + s * 0.38 * r, c.Y - 0.75 * r);
            dc.DrawGeometry(B(p.InnerEar), null, Tri(ia, it, ib));
        }
        Ear(-1, earTwitch);
        Ear(1, 0);

        // head
        dc.DrawEllipse(B(p.Fur), outline, c, r, r * 0.88);
        // forehead stripes (tabby touch)
        var stripe = P(p.FurDark, ow * 1.1);
        dc.DrawLine(stripe, new Point(c.X + lx * 0.4, c.Y - 0.82 * r), new Point(c.X + lx * 0.4, c.Y - 0.58 * r));
        dc.DrawLine(stripe, new Point(c.X - 0.2 * r + lx * 0.4, c.Y - 0.78 * r), new Point(c.X - 0.16 * r + lx * 0.4, c.Y - 0.6 * r));
        dc.DrawLine(stripe, new Point(c.X + 0.2 * r + lx * 0.4, c.Y - 0.78 * r), new Point(c.X + 0.16 * r + lx * 0.4, c.Y - 0.6 * r));

        // muzzle + blush
        dc.DrawEllipse(B(p.Belly), null, new Point(c.X + lx, c.Y + 0.32 * r), r * 0.36, r * 0.24);
        dc.DrawEllipse(B(p.Blush, 0.55), null, new Point(c.X - 0.56 * r + lx, c.Y + 0.24 * r), r * 0.15, r * 0.09);
        dc.DrawEllipse(B(p.Blush, 0.55), null, new Point(c.X + 0.56 * r + lx, c.Y + 0.24 * r), r * 0.15, r * 0.09);

        // eyes
        double ex = 0.38 * r, ey = c.Y - 0.02 * r;
        var eyePen = P(p.Eye == p.Fur ? p.Outline : (mood == FaceMood.Awake || mood == FaceMood.Surprised ? p.Eye : p.Outline), Math.Max(1.2, r * 0.09));
        foreach (int s in new[] { -1, 1 })
        {
            var e = new Point(c.X + s * ex + lx, ey);
            switch (mood)
            {
                case FaceMood.Awake:
                case FaceMood.Surprised:
                {
                    double big = mood == FaceMood.Surprised ? 1.25 : 1;
                    double h = Math.Max(0.02, eyeOpen) * r * 0.19 * big;
                    if (eyeOpen < 0.25)
                        dc.DrawLine(P(p.Outline, Math.Max(1.2, r * 0.08)), new Point(e.X - r * 0.12, e.Y), new Point(e.X + r * 0.12, e.Y));
                    else
                    {
                        dc.DrawEllipse(B(p.Eye), null, e, r * 0.135 * big, h);
                        if (p.Eye.R > 150) dc.DrawEllipse(B(Color.FromRgb(0x1A, 0x1A, 0x1A)), null, e, r * 0.05, h * 0.85); // slit pupil for light eyes
                        dc.DrawEllipse(Brushes.White, null, new Point(e.X - r * 0.04 + lx * 0.1, e.Y - h * 0.4), r * 0.045, r * 0.045);
                    }
                    break;
                }
                case FaceMood.Sleep:
                case FaceMood.Content:
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(e.X - r * 0.14, e.Y - r * 0.02), false, false);
                        ctx.QuadraticBezierTo(new Point(e.X, e.Y + r * 0.12), new Point(e.X + r * 0.14, e.Y - r * 0.02), true, true);
                    }
                    dc.DrawGeometry(null, P(p.Outline, Math.Max(1.2, r * 0.08)), g);
                    break;
                }
                case FaceMood.Happy:
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(e.X - r * 0.14, e.Y + r * 0.06), false, false);
                        ctx.QuadraticBezierTo(new Point(e.X, e.Y - r * 0.16), new Point(e.X + r * 0.14, e.Y + r * 0.06), true, true);
                    }
                    dc.DrawGeometry(null, P(p.Outline, Math.Max(1.2, r * 0.08)), g);
                    break;
                }
            }
        }

        // nose + mouth
        var n = new Point(c.X + lx * 1.05, c.Y + 0.2 * r);
        dc.DrawGeometry(B(p.Nose), null, Tri(new Point(n.X - 0.075 * r, n.Y - 0.03 * r), new Point(n.X + 0.075 * r, n.Y - 0.03 * r), new Point(n.X, n.Y + 0.06 * r)));
        if (r >= 7)
        {
            var mouth = new StreamGeometry();
            using (var ctx = mouth.Open())
            {
                ctx.BeginFigure(new Point(n.X - 0.15 * r, n.Y + 0.12 * r), false, false);
                ctx.QuadraticBezierTo(new Point(n.X - 0.07 * r, n.Y + 0.2 * r), new Point(n.X, n.Y + 0.07 * r), true, true);
                ctx.QuadraticBezierTo(new Point(n.X + 0.07 * r, n.Y + 0.2 * r), new Point(n.X + 0.15 * r, n.Y + 0.12 * r), true, true);
            }
            dc.DrawGeometry(null, P(p.Outline, Math.Max(0.8, r * 0.05)), mouth);
        }

        if (whiskers && r >= 12)
        {
            var w = P(p.Outline, Math.Max(0.7, r * 0.035), 0.55);
            foreach (int s in new[] { -1, 1 })
            {
                double bx = c.X + s * 0.42 * r + lx;
                dc.DrawLine(w, new Point(bx, c.Y + 0.24 * r), new Point(bx + s * 0.62 * r, c.Y + 0.14 * r));
                dc.DrawLine(w, new Point(bx, c.Y + 0.32 * r), new Point(bx + s * 0.62 * r, c.Y + 0.36 * r));
            }
        }
    }

    public static void DrawHeart(DrawingContext dc, Point c, double s, Color col, double opacity)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y + s * 0.9), true, true);
            ctx.BezierTo(new Point(c.X - s * 1.4, c.Y - s * 0.1), new Point(c.X - s * 0.6, c.Y - s * 1.1), new Point(c.X, c.Y - s * 0.35), true, true);
            ctx.BezierTo(new Point(c.X + s * 0.6, c.Y - s * 1.1), new Point(c.X + s * 1.4, c.Y - s * 0.1), new Point(c.X, c.Y + s * 0.9), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(B(col, opacity), null, g);
    }

    public static void DrawPaw(DrawingContext dc, Point c, double s, Brush b)
    {
        dc.DrawEllipse(b, null, new Point(c.X, c.Y + s * 0.3), s * 0.48, s * 0.4);
        dc.DrawEllipse(b, null, new Point(c.X - s * 0.52, c.Y - s * 0.22), s * 0.17, s * 0.21);
        dc.DrawEllipse(b, null, new Point(c.X - s * 0.18, c.Y - s * 0.48), s * 0.17, s * 0.21);
        dc.DrawEllipse(b, null, new Point(c.X + s * 0.18, c.Y - s * 0.48), s * 0.17, s * 0.21);
        dc.DrawEllipse(b, null, new Point(c.X + s * 0.52, c.Y - s * 0.22), s * 0.17, s * 0.21);
    }
}
