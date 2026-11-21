import SwiftUI

/// Cat Bridge look: warm peach accent on deep graphite, soft rounded cards (matches the laptop's island).
enum Theme {
    static let bg = Color(red: 0.055, green: 0.055, blue: 0.063)
    static let card = Color(red: 0.094, green: 0.094, blue: 0.106)
    static let cardHi = Color(red: 0.13, green: 0.13, blue: 0.145)
    static let edge = Color.white.opacity(0.07)
    static let accent = Color(red: 1.0, green: 0.70, blue: 0.48)
    static let onAccent = Color(red: 0.1, green: 0.06, blue: 0.03)
    static let dim = Color.white.opacity(0.58)
    static let faint = Color.white.opacity(0.35)
    static let green = Color(red: 0.19, green: 0.82, blue: 0.35)
    static let red = Color(red: 1.0, green: 0.36, blue: 0.33)
    static let yellow = Color(red: 1.0, green: 0.84, blue: 0.04)
    static let blue = Color(red: 0.25, green: 0.6, blue: 1.0)
}

struct CardBox<Content: View>: View {
    var padding: CGFloat = 16
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 12) { content }
            .padding(padding)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Theme.card, in: RoundedRectangle(cornerRadius: 22, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).stroke(Theme.edge, lineWidth: 1))
    }
}

struct SectionTitle: View {
    let text: String
    var body: some View {
        Text(text.uppercased())
            .font(.system(size: 12, weight: .semibold, design: .rounded))
            .foregroundStyle(Theme.dim)
            .padding(.leading, 4)
            .padding(.top, 8)
    }
}

/// Big rounded action tile (icon + label).
struct ActionTile: View {
    let icon: String
    let label: String
    var tint: Color = Theme.accent
    var action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 10) {
                Image(systemName: icon)
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundStyle(tint)
                    .frame(width: 36, height: 36)
                    .background(tint.opacity(0.15), in: RoundedRectangle(cornerRadius: 11, style: .continuous))
                Text(label)
                    .font(.system(size: 15, weight: .semibold, design: .rounded))
                    .foregroundStyle(.white)
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Theme.card, in: RoundedRectangle(cornerRadius: 20, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 20, style: .continuous).stroke(Theme.edge, lineWidth: 1))
        }
        .buttonStyle(PressStyle())
    }
}

struct PressStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
            .opacity(configuration.isPressed ? 0.85 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.7), value: configuration.isPressed)
    }
}

struct PrimaryButton: View {
    let title: String
    var icon: String? = nil
    var action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 8) {
                if let icon { Image(systemName: icon) }
                Text(title)
            }
            .font(.system(size: 16, weight: .semibold, design: .rounded))
            .foregroundStyle(Theme.onAccent)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 14)
            .background(Theme.accent, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        }
        .buttonStyle(PressStyle())
    }
}

struct SecondaryButton: View {
    let title: String
    var icon: String? = nil
    var action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 8) {
                if let icon { Image(systemName: icon) }
                Text(title).lineLimit(1).minimumScaleFactor(0.8)
            }
            .font(.system(size: 15, weight: .semibold, design: .rounded))
            .foregroundStyle(.white)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 13)
            .background(Theme.cardHi, in: RoundedRectangle(cornerRadius: 15, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 15, style: .continuous).stroke(Theme.edge, lineWidth: 1))
        }
        .buttonStyle(PressStyle())
    }
}

struct StatusPill: View {
    let text: String
    let color: Color
    var body: some View {
        HStack(spacing: 6) {
            Circle().fill(color).frame(width: 7, height: 7)
            Text(text).font(.system(size: 13, weight: .semibold, design: .rounded))
        }
        .padding(.horizontal, 10).padding(.vertical, 5)
        .background(color.opacity(0.14), in: Capsule())
        .foregroundStyle(color)
    }
}

/// Little ring used for battery.
struct RingView: View {
    let value: Double
    var color: Color = Theme.accent
    var lineWidth: CGFloat = 6
    var body: some View {
        ZStack {
            Circle().stroke(Color.white.opacity(0.1), lineWidth: lineWidth)
            Circle().trim(from: 0, to: max(0.001, min(1, value)))
                .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .animation(.spring(response: 0.6, dampingFraction: 0.8), value: value)
        }
    }
}

struct ToastView: View {
    let toast: AppModel.Toast
    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: toast.isError ? "exclamationmark.triangle.fill" : "checkmark.circle.fill")
                .foregroundStyle(toast.isError ? Theme.red : Theme.green)
            Text(toast.text)
                .font(.system(size: 14, weight: .semibold, design: .rounded))
                .foregroundStyle(.white)
                .lineLimit(3)
        }
        .padding(.horizontal, 16).padding(.vertical, 12)
        .background(.ultraThinMaterial, in: Capsule())
        .overlay(Capsule().stroke(Theme.edge, lineWidth: 1))
        .shadow(color: .black.opacity(0.4), radius: 16, y: 6)
        .padding(.horizontal, 20)
    }
}

struct ShareSheet: UIViewControllerRepresentable {
    let items: [Any]
    func makeUIViewController(context: Context) -> UIActivityViewController { UIActivityViewController(activityItems: items, applicationActivities: nil) }
    func updateUIViewController(_ vc: UIActivityViewController, context: Context) {}
}
