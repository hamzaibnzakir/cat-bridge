import SwiftUI

/// The Cat Bridge cat, drawn in code (same ginger cat as the laptop's island).
struct CatFaceView: View {
    enum Mood { case happy, sleepy, alert }
    var mood: Mood = .happy

    private let fur = Color(red: 0.96, green: 0.66, blue: 0.36)
    private let furDark = Color(red: 0.86, green: 0.52, blue: 0.24)
    private let inner = Color(red: 0.97, green: 0.65, blue: 0.71)
    private let ink = Color(red: 0.16, green: 0.1, blue: 0.08)

    var body: some View {
        Canvas { ctx, size in
            let w = size.width, h = size.height
            func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x * w, y: y * h) }
            let mirror = CGAffineTransform(translationX: w, y: 0).scaledBy(x: -1, y: 1)

            // ears
            var ear = Path()
            ear.move(to: p(0.10, 0.48)); ear.addLine(to: p(0.17, 0.03)); ear.addLine(to: p(0.47, 0.30)); ear.closeSubpath()
            var innerEar = Path()
            innerEar.move(to: p(0.16, 0.40)); innerEar.addLine(to: p(0.20, 0.13)); innerEar.addLine(to: p(0.38, 0.31)); innerEar.closeSubpath()
            for t in [CGAffineTransform.identity, mirror] {
                ctx.fill(ear.applying(t), with: .color(fur))
                ctx.fill(innerEar.applying(t), with: .color(inner))
            }

            // head
            let head = Path(ellipseIn: CGRect(x: 0.04 * w, y: 0.20 * h, width: 0.92 * w, height: 0.78 * h))
            ctx.fill(head, with: .linearGradient(Gradient(colors: [fur, furDark]), startPoint: p(0.5, 0.2), endPoint: p(0.5, 1.0)))
            // forehead stripes
            for x in [0.42, 0.5, 0.58] as [CGFloat] {
                var s = Path(); s.move(to: p(x, 0.24)); s.addLine(to: p(x, 0.34))
                ctx.stroke(s, with: .color(furDark), style: StrokeStyle(lineWidth: 0.025 * w, lineCap: .round))
            }
            // muzzle
            ctx.fill(Path(ellipseIn: CGRect(x: 0.30 * w, y: 0.60 * h, width: 0.40 * w, height: 0.30 * h)), with: .color(Color(red: 1, green: 0.9, blue: 0.8)))

            // eyes
            let lw = 0.045 * w
            for cx in [0.32, 0.68] as [CGFloat] {
                switch mood {
                case .happy:
                    var e = Path()
                    e.addArc(center: p(cx, 0.56), radius: 0.07 * w, startAngle: .degrees(200), endAngle: .degrees(340), clockwise: false)
                    ctx.stroke(e, with: .color(ink), style: StrokeStyle(lineWidth: lw, lineCap: .round))
                case .sleepy:
                    var e = Path(); e.move(to: p(cx - 0.07, 0.55)); e.addLine(to: p(cx + 0.07, 0.55))
                    ctx.stroke(e, with: .color(ink), style: StrokeStyle(lineWidth: lw, lineCap: .round))
                case .alert:
                    ctx.fill(Path(ellipseIn: CGRect(x: (cx - 0.06) * w, y: 0.47 * h, width: 0.12 * w, height: 0.14 * h)), with: .color(ink))
                    ctx.fill(Path(ellipseIn: CGRect(x: (cx - 0.02) * w, y: 0.49 * h, width: 0.04 * w, height: 0.045 * h)), with: .color(.white))
                }
            }

            // nose + mouth
            var nose = Path()
            nose.move(to: p(0.46, 0.66)); nose.addLine(to: p(0.54, 0.66)); nose.addLine(to: p(0.5, 0.71)); nose.closeSubpath()
            ctx.fill(nose, with: .color(Color(red: 0.93, green: 0.45, blue: 0.5)))
            var mouth = Path()
            mouth.move(to: p(0.5, 0.71))
            mouth.addQuadCurve(to: p(0.42, 0.75), control: p(0.47, 0.77))
            mouth.move(to: p(0.5, 0.71))
            mouth.addQuadCurve(to: p(0.58, 0.75), control: p(0.53, 0.77))
            ctx.stroke(mouth, with: .color(ink), style: StrokeStyle(lineWidth: 0.025 * w, lineCap: .round))

            // whiskers
            for (y0, y1) in [(0.68, 0.64), (0.72, 0.73)] as [(CGFloat, CGFloat)] {
                var l = Path(); l.move(to: p(0.30, y0)); l.addLine(to: p(0.02, y1))
                ctx.stroke(l, with: .color(.white.opacity(0.7)), style: StrokeStyle(lineWidth: 0.012 * w, lineCap: .round))
                ctx.stroke(l.applying(mirror), with: .color(.white.opacity(0.7)), style: StrokeStyle(lineWidth: 0.012 * w, lineCap: .round))
            }
        }
        .aspectRatio(1.1, contentMode: .fit)
        .accessibilityHidden(true)
    }
}
