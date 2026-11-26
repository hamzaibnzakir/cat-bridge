import SwiftUI
import UIKit
import CatBridgeCore

/// The Remote tab works like a small laptop: the live screen on top, the trackpad and keys below.
/// Tap the screen to click right there, hold for a right-click, pinch to zoom in.
struct RemoteView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.scenePhase) private var phase
    @Environment(\.displayScale) private var displayScale
    @State private var channel = InputChannel()
    @State private var screen = ScreenChannel()
    @State private var typing = false
    @AppStorage("remote.showScreen") private var showScreen = true

    var body: some View {
        NavigationStack {
            VStack(spacing: 10) {
                statusBar
                if showScreen {
                    screenPanel
                        .transition(.move(edge: .top).combined(with: .opacity))
                }
                TrackpadArea(channel: channel)
                    .frame(minHeight: 130)
                    .clipShape(RoundedRectangle(cornerRadius: 26, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 26, style: .continuous).stroke(Theme.edge, lineWidth: 1))
                    .overlay(alignment: .center) {
                        if case .connected = channel.state {
                            if !showScreen || screen.image == nil {
                                Text("Trackpad").font(.system(size: 13, weight: .semibold, design: .rounded)).foregroundStyle(Theme.faint).allowsHitTesting(false)
                            }
                        } else {
                            Text(hint).font(.system(size: 15, design: .rounded)).foregroundStyle(Theme.dim).multilineTextAlignment(.center).padding(24).allowsHitTesting(false)
                        }
                    }
                HStack(spacing: 10) {
                    padButton("Left click") { channel.click("l") }
                    padButton("Right click") { channel.click("r") }
                }
                keysRow
                KeyCatcher(isActive: $typing, channel: channel).frame(width: 1, height: 1).opacity(0.01)
            }
            .padding(.horizontal, 14)
            .padding(.bottom, 10)
            .background(Theme.bg)
            .animation(.spring(response: 0.35, dampingFraction: 0.85), value: showScreen)
            .navigationTitle("Remote")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button { typing.toggle() } label: { Image(systemName: typing ? "keyboard.chevron.compact.down" : "keyboard") }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        showScreen.toggle()
                        if showScreen { screen.connect(model.client?.screenURL) } else { screen.disconnect() }
                    } label: { Image(systemName: showScreen ? "display" : "rectangle.slash") }
                    .accessibilityLabel(showScreen ? "Hide the laptop screen" : "Show the laptop screen")
                }
                ToolbarItem(placement: .topBarLeading) {
                    Menu {
                        Picker("Screen quality", selection: $screen.quality) {
                            ForEach(ScreenChannel.Quality.allCases) { q in
                                Text(q.title + " · " + q.detail).tag(q)
                            }
                        }
                        Picker("Pointer speed", selection: $channel.sensitivity) {
                            Text("Slow").tag(1.0); Text("Normal").tag(1.6); Text("Fast").tag(2.4); Text("Very fast").tag(3.4)
                        }
                    } label: { Image(systemName: "slider.horizontal.3") }
                }
            }
            .onAppear { connectAll() }
            .onDisappear { channel.disconnect(); screen.disconnect(); typing = false }
            .onChange(of: phase) { _, p in
                if p == .active { connectAll() } else { channel.disconnect(); screen.disconnect() }
            }
        }
    }

    private func connectAll() {
        if channel.state != .connecting { channel.connect(model.client?.inputURL) }
        if showScreen, screen.state != .connecting, screen.state != .live { screen.connect(model.client?.screenURL) }
    }

    // ───────── the live screen ─────────

    private var screenPanel: some View {
        let ratio = max(0.4, min(3, screen.screenSize.width / max(1, screen.screenSize.height)))
        return ZStack {
            Color.black
            LiveScreenView(image: screen.image,
                           onTap: { x, y in channel.send(String(format: "a %.4f %.4f", x, y)); channel.click("l") },
                           onHold: { x, y in channel.send(String(format: "a %.4f %.4f", x, y)); channel.click("r") })
            screenOverlay
        }
        .aspectRatio(ratio, contentMode: .fit)
        .frame(maxWidth: .infinity)
        .frame(maxHeight: UIScreen.main.bounds.height * 0.42)
        .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).stroke(Theme.edge, lineWidth: 1))
        .background(GeometryReader { g in
            Color.clear
                .onAppear { screen.setViewWidth(points: g.size.width, scale: displayScale) }
                .onChange(of: g.size.width) { _, w in screen.setViewWidth(points: w, scale: displayScale) }
        })
    }

    @ViewBuilder private var screenOverlay: some View {
        switch screen.state {
        case .live where screen.image != nil:
            EmptyView()
        case .live, .connecting:
            VStack(spacing: 10) {
                ProgressView().tint(Theme.accent)
                Text("Connecting to the screen…").font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim)
            }
        case .locked:
            VStack(spacing: 12) {
                Image(systemName: "lock.fill").font(.system(size: 26, weight: .semibold)).foregroundStyle(Theme.accent)
                Text("Your laptop is locked").font(.system(size: 15, weight: .semibold, design: .rounded)).foregroundStyle(.white)
                Button {
                    Task { await model.unlockLaptop() }
                } label: {
                    Label("Unlock with Face ID", systemImage: "faceid").font(.system(size: 14, weight: .semibold, design: .rounded))
                        .foregroundStyle(Theme.onAccent).padding(.horizontal, 16).padding(.vertical, 10)
                        .background(Theme.accent, in: Capsule())
                }
                .buttonStyle(PressStyle())
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(.black.opacity(screen.image == nil ? 0 : 0.6))
        case .off(let why), .failed(let why):
            VStack(spacing: 10) {
                Image(systemName: "display.trianglebadge.exclamationmark").font(.system(size: 24)).foregroundStyle(Theme.dim)
                Text(why).font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim).multilineTextAlignment(.center).padding(.horizontal, 20)
                Button("Try again") { screen.connect(model.client?.screenURL) }.font(.system(size: 14, weight: .semibold, design: .rounded))
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(.black.opacity(0.7))
        case .idle:
            Button("Show the laptop screen") { screen.connect(model.client?.screenURL) }.font(.system(size: 14, weight: .semibold, design: .rounded))
        }
    }

    private var hint: String {
        switch channel.state {
        case .connecting: return "Connecting to your laptop…"
        case .failed(let why): return why
        default: return "One finger moves the pointer. Tap to click, tap with two fingers to right-click, slide two fingers to scroll. Hold, then move, to drag."
        }
    }

    private var statusBar: some View {
        HStack(spacing: 8) {
            switch channel.state {
            case .connected(let name): StatusPill(text: name, color: Theme.green)
            case .connecting: StatusPill(text: "Connecting…", color: Theme.yellow)
            case .failed: StatusPill(text: "Not connected", color: Theme.red)
            case .idle: StatusPill(text: "Not connected", color: Theme.faint)
            }
            if showScreen, screen.state == .live, screen.image != nil {
                Text(String(format: "%.0f fps · %@", screen.fps, screen.quality.title))
                    .font(.system(size: 12, weight: .medium, design: .rounded).monospacedDigit())
                    .foregroundStyle(Theme.faint)
            }
            Spacer()
            if case .failed = channel.state {
                Button("Retry") { connectAll() }.font(.system(size: 15, weight: .semibold, design: .rounded))
            }
        }
        .padding(.top, 6)
    }

    private func padButton(_ title: String, action: @escaping () -> Void) -> some View {
        Button {
            UIImpactFeedbackGenerator(style: .light).impactOccurred()
            action()
        } label: {
            Text(title).font(.system(size: 15, weight: .semibold, design: .rounded)).foregroundStyle(.white)
                .frame(maxWidth: .infinity).frame(height: 50)
                .background(Theme.card, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).stroke(Theme.edge, lineWidth: 1))
        }
        .buttonStyle(PressStyle())
    }

    private struct KeyDef: Identifiable { let id = UUID(); let label: String; let icon: String?; let send: String }

    private let keys: [KeyDef] = [
        KeyDef(label: "esc", icon: nil, send: "k esc"), KeyDef(label: "tab", icon: nil, send: "k tab"),
        KeyDef(label: "", icon: "return", send: "k enter"), KeyDef(label: "", icon: "delete.left", send: "k backspace"),
        KeyDef(label: "", icon: "arrow.left", send: "k left"), KeyDef(label: "", icon: "arrow.up", send: "k up"),
        KeyDef(label: "", icon: "arrow.down", send: "k down"), KeyDef(label: "", icon: "arrow.right", send: "k right"),
        KeyDef(label: "", icon: "squareshape.split.2x2", send: "k win"), KeyDef(label: "Copy", icon: nil, send: "h ctrl+c"),
        KeyDef(label: "Paste", icon: nil, send: "h ctrl+v"), KeyDef(label: "Undo", icon: nil, send: "h ctrl+z"),
        KeyDef(label: "Switch app", icon: nil, send: "h alt+tab"), KeyDef(label: "Desktop", icon: nil, send: "h win+d"),
        KeyDef(label: "Zoom +", icon: nil, send: "z in"), KeyDef(label: "Zoom −", icon: nil, send: "z out"),
        KeyDef(label: "", icon: "speaker.minus", send: "k voldown"), KeyDef(label: "", icon: "speaker.plus", send: "k volup"),
        KeyDef(label: "", icon: "playpause", send: "k playpause"), KeyDef(label: "F5", icon: nil, send: "k f5"),
    ]

    private var keysRow: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(keys) { k in
                    Button {
                        UIImpactFeedbackGenerator(style: .light).impactOccurred()
                        channel.send(k.send)
                    } label: {
                        Group {
                            if let icon = k.icon { Image(systemName: icon).font(.system(size: 15, weight: .semibold)) }
                            else { Text(k.label).font(.system(size: 14, weight: .semibold, design: .rounded)) }
                        }
                        .foregroundStyle(.white)
                        .padding(.horizontal, 14).frame(minWidth: 46).frame(height: 42)
                        .background(Theme.cardHi, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                    }
                    .buttonStyle(PressStyle())
                }
            }
            .padding(.vertical, 2)
        }
    }
}

// ───────── the live screen (zoomable, tap to click) ─────────

/// Shows the laptop's screen. Pinch or double-tap to zoom, drag to look around when zoomed,
/// tap to click that spot on the laptop, hold for a right-click there.
struct LiveScreenView: UIViewRepresentable {
    let image: UIImage?
    let onTap: (Double, Double) -> Void
    let onHold: (Double, Double) -> Void

    func makeUIView(context: Context) -> ScreenScrollView {
        let v = ScreenScrollView()
        v.onTap = onTap; v.onHold = onHold
        return v
    }

    func updateUIView(_ v: ScreenScrollView, context: Context) {
        v.onTap = onTap; v.onHold = onHold
        if v.imageView.image !== image { v.imageView.image = image }
    }
}

final class ScreenScrollView: UIScrollView, UIScrollViewDelegate {
    let imageView = UIImageView()
    var onTap: ((Double, Double) -> Void)?
    var onHold: ((Double, Double) -> Void)?
    private let haptic = UIImpactFeedbackGenerator(style: .light)

    override init(frame: CGRect) {
        super.init(frame: frame)
        delegate = self
        minimumZoomScale = 1; maximumZoomScale = 5
        showsHorizontalScrollIndicator = false; showsVerticalScrollIndicator = false
        bouncesZoom = true
        contentInsetAdjustmentBehavior = .never
        backgroundColor = .black
        imageView.contentMode = .scaleAspectFit
        imageView.isUserInteractionEnabled = true
        addSubview(imageView)

        let doubleTap = UITapGestureRecognizer(target: self, action: #selector(onDoubleTap(_:)))
        doubleTap.numberOfTapsRequired = 2
        let tap = UITapGestureRecognizer(target: self, action: #selector(onSingleTap(_:)))
        tap.require(toFail: doubleTap)
        let hold = UILongPressGestureRecognizer(target: self, action: #selector(onLongPress(_:)))
        hold.minimumPressDuration = 0.45
        for g in [doubleTap, tap, hold] { imageView.addGestureRecognizer(g) }
    }

    required init?(coder: NSCoder) { fatalError() }

    override func layoutSubviews() {
        super.layoutSubviews()
        if zoomScale == 1, imageView.frame.size != bounds.size {
            imageView.frame = bounds
            contentSize = bounds.size
        }
    }

    func viewForZooming(in scrollView: UIScrollView) -> UIView? { imageView }

    /// Where on the laptop screen (0–1 across, 0–1 down) a touch landed, allowing for letterboxing.
    private func spot(_ g: UIGestureRecognizer) -> (Double, Double)? {
        guard let img = imageView.image, img.size.width > 0, img.size.height > 0 else { return nil }
        let p = g.location(in: imageView)
        let box = imageView.bounds.size
        let k = min(box.width / img.size.width, box.height / img.size.height)
        let w = img.size.width * k, h = img.size.height * k
        let x = (p.x - (box.width - w) / 2) / w, y = (p.y - (box.height - h) / 2) / h
        guard (0...1).contains(x), (0...1).contains(y) else { return nil }
        return (Double(x), Double(y))
    }

    @objc private func onSingleTap(_ g: UITapGestureRecognizer) {
        guard let (x, y) = spot(g) else { return }
        haptic.impactOccurred()
        onTap?(x, y)
    }

    @objc private func onLongPress(_ g: UILongPressGestureRecognizer) {
        guard g.state == .began, let (x, y) = spot(g) else { return }
        UIImpactFeedbackGenerator(style: .medium).impactOccurred()
        onHold?(x, y)
    }

    @objc private func onDoubleTap(_ g: UITapGestureRecognizer) {
        if zoomScale > 1.05 { setZoomScale(1, animated: true); return }
        let p = g.location(in: imageView)
        let s: CGFloat = 2.5
        let size = CGSize(width: bounds.width / s, height: bounds.height / s)
        zoom(to: CGRect(x: p.x - size.width / 2, y: p.y - size.height / 2, width: size.width, height: size.height), animated: true)
    }
}

// ───────── the trackpad surface ─────────

struct TrackpadArea: UIViewRepresentable {
    let channel: InputChannel

    func makeUIView(context: Context) -> TrackpadSurface {
        let v = TrackpadSurface()
        v.channel = channel
        return v
    }

    func updateUIView(_ v: TrackpadSurface, context: Context) { v.channel = channel }
}

final class TrackpadSurface: UIView, UIGestureRecognizerDelegate {
    weak var channel: InputChannel?
    private var dragging = false
    private var lastHold: CGPoint = .zero
    private let haptic = UIImpactFeedbackGenerator(style: .medium)

    override init(frame: CGRect) {
        super.init(frame: frame)
        backgroundColor = UIColor(red: 0.094, green: 0.094, blue: 0.106, alpha: 1)
        isMultipleTouchEnabled = true

        let move = UIPanGestureRecognizer(target: self, action: #selector(onMove(_:)))
        move.minimumNumberOfTouches = 1; move.maximumNumberOfTouches = 1
        let scroll = UIPanGestureRecognizer(target: self, action: #selector(onScroll(_:)))
        scroll.minimumNumberOfTouches = 2; scroll.maximumNumberOfTouches = 2
        let tap = UITapGestureRecognizer(target: self, action: #selector(onTap))
        tap.numberOfTouchesRequired = 1
        let twoTap = UITapGestureRecognizer(target: self, action: #selector(onTwoTap))
        twoTap.numberOfTouchesRequired = 2
        let hold = UILongPressGestureRecognizer(target: self, action: #selector(onHold(_:)))
        hold.minimumPressDuration = 0.35; hold.allowableMovement = 12
        for g in [move, scroll, tap, twoTap, hold] as [UIGestureRecognizer] { g.delegate = self; addGestureRecognizer(g) }
    }

    required init?(coder: NSCoder) { fatalError() }

    func gestureRecognizer(_ g: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith other: UIGestureRecognizer) -> Bool {
        (g is UILongPressGestureRecognizer && other is UIPanGestureRecognizer) || (other is UILongPressGestureRecognizer && g is UIPanGestureRecognizer)
    }

    private func speed(_ v: CGPoint) -> Double {
        // a little acceleration: slow moves are precise, fast flicks cross the screen
        let s = Double(hypot(v.x, v.y))
        return (channel?.sensitivity ?? 1.6) * (1 + min(s / 900, 1.8))
    }

    @objc private func onMove(_ g: UIPanGestureRecognizer) {
        guard !dragging else { return }
        let t = g.translation(in: self)
        let k = speed(g.velocity(in: self))
        self.channel?.move(Double(t.x) * k, Double(t.y) * k)
        g.setTranslation(.zero, in: self)
    }

    @objc private func onScroll(_ g: UIPanGestureRecognizer) {
        let t = g.translation(in: self)
        // natural scrolling, like a Mac trackpad: fingers up = page moves up
        let dy = Int((Double(t.y) * 4).rounded()), dx = Int((Double(-t.x) * 4).rounded())
        if dy != 0 || dx != 0 { self.channel?.send("s \(dy) \(dx)") }
        g.setTranslation(.zero, in: self)
    }

    @objc private func onTap() { self.channel?.click("l") }

    @objc private func onTwoTap() {
        haptic.impactOccurred()
        self.channel?.click("r")
    }

    @objc private func onHold(_ g: UILongPressGestureRecognizer) {
        let p = g.location(in: self)
        switch g.state {
        case .began:
            dragging = true; lastHold = p
            haptic.impactOccurred()
            self.channel?.send("d l")
        case .changed:
            let dx = p.x - lastHold.x, dy = p.y - lastHold.y
            lastHold = p
            let k = channel?.sensitivity ?? 1.6
            self.channel?.move(Double(dx) * k, Double(dy) * k)
        default:
            if dragging { self.channel?.send("u l") }
            dragging = false
        }
    }
}

// ───────── typing on the laptop ─────────

/// An invisible view that brings up the iPhone keyboard and sends each key to the laptop.
struct KeyCatcher: UIViewRepresentable {
    @Binding var isActive: Bool
    let channel: InputChannel

    func makeUIView(context: Context) -> KeyView {
        let v = KeyView()
        v.channel = channel
        return v
    }

    func updateUIView(_ v: KeyView, context: Context) {
        v.channel = channel
        DispatchQueue.main.async {
            if isActive, !v.isFirstResponder { v.becomeFirstResponder() }
            if !isActive, v.isFirstResponder { v.resignFirstResponder() }
        }
    }
}

final class KeyView: UIView, UIKeyInput {
    weak var channel: InputChannel?
    override var canBecomeFirstResponder: Bool { true }
    var hasText: Bool { true }
    var autocorrectionType: UITextAutocorrectionType = .no
    var autocapitalizationType: UITextAutocapitalizationType = .none
    var spellCheckingType: UITextSpellCheckingType = .no
    var smartQuotesType: UITextSmartQuotesType = .no
    var smartDashesType: UITextSmartDashesType = .no
    var keyboardAppearance: UIKeyboardAppearance = .dark
    var returnKeyType: UIReturnKeyType = .default

    func insertText(_ text: String) {
        if text == "\n" { channel?.key("enter") } else { channel?.type(text) }
    }

    func deleteBackward() {
        channel?.key("backspace")
    }
}
