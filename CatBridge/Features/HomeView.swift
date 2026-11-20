import SwiftUI
import UIKit
import CatBridgeCore

struct HomeView: View {
    @Environment(AppModel.self) private var model
    @State private var typed = ""
    @State private var link = ""
    @State private var confirmLock = false
    @FocusState private var focused: Bool

    private var online: Bool { model.connection == .online }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    header
                    if case .offline(let why) = model.connection { offlineCard(why) }
                    if case .notPaired = model.connection { notPairedCard }
                    stats
                    nowPlaying
                    SectionTitle(text: "Laptop")
                    actions
                    SectionTitle(text: "Clipboard")
                    clipboard
                    SectionTitle(text: "Open on laptop")
                    linkCard
                    if let s = model.sales, s.orders > 0 { salesCard(s) }
                }
                .padding(.horizontal, 16)
                .padding(.bottom, 30)
            }
            .scrollDismissesKeyboard(.interactively)
            .refreshable { await model.refresh() }
            .background(Theme.bg)
            .toolbar(.hidden, for: .navigationBar)
            .confirmationDialog("Lock your laptop?", isPresented: $confirmLock, titleVisibility: .visible) {
                Button("Lock laptop", role: .destructive) { model.act("Laptop locked") { try await $0.lock() } }
            }
        }
    }

    // ───────── header ─────────

    private var header: some View {
        HStack(spacing: 14) {
            CatFaceView(mood: online ? .happy : model.connection == .unknown ? .alert : .sleepy)
                .frame(width: 58)
            VStack(alignment: .leading, spacing: 5) {
                Text(model.status?.device ?? model.pairing?.name ?? "Laptop")
                    .font(.system(size: 26, weight: .bold, design: .rounded))
                    .lineLimit(1).minimumScaleFactor(0.7)
                switch model.connection {
                case .online: StatusPill(text: "Connected", color: Theme.green)
                case .unknown: StatusPill(text: "Connecting…", color: Theme.yellow)
                case .offline: StatusPill(text: "Can't reach it", color: Theme.red)
                case .notPaired: StatusPill(text: "Not paired", color: Theme.red)
                }
            }
            Spacer()
        }
        .padding(.top, 12)
    }

    private func offlineCard(_ why: String) -> some View {
        CardBox {
            Label("Your laptop isn't answering", systemImage: "wifi.exclamationmark")
                .font(.system(size: 16, weight: .semibold, design: .rounded))
            Text("Make sure Tailscale is connected on this iPhone and the laptop, and Dynamic Island is running.")
                .font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim)
            HStack {
                SecondaryButton(title: "Open Tailscale", icon: "network") {
                    if let u = URL(string: "tailscale://") { UIApplication.shared.open(u) }
                }
                SecondaryButton(title: "Try again", icon: "arrow.clockwise") { Task { await model.refresh() } }
            }
        }
    }

    private var notPairedCard: some View {
        CardBox {
            Label("Pairing key changed", systemImage: "key")
                .font(.system(size: 16, weight: .semibold, design: .rounded))
            Text("Scan the code in Cat Center → iPhone again (Settings → Scan a new code).")
                .font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim)
        }
    }

    // ───────── stats ─────────

    private var stats: some View {
        HStack(spacing: 12) {
            CardBox(padding: 14) {
                HStack(spacing: 12) {
                    let b = model.status?.battery ?? 0
                    ZStack {
                        RingView(value: Double(b) / 100, color: (model.status?.charging ?? false) ? Theme.green : b <= 20 ? Theme.red : Theme.accent)
                        Image(systemName: (model.status?.charging ?? false) ? "bolt.fill" : "laptopcomputer").font(.system(size: 13, weight: .bold))
                    }
                    .frame(width: 44, height: 44)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(model.status.map { $0.hasBattery ? "\($0.battery)%" : "Plugged in" } ?? "—")
                            .font(.system(size: 22, weight: .bold, design: .rounded))
                        Text((model.status?.charging ?? false) ? "Charging" : "Battery")
                            .font(.system(size: 13, design: .rounded)).foregroundStyle(Theme.dim)
                    }
                }
            }
            CardBox(padding: 14) {
                HStack(spacing: 12) {
                    let left = model.status?.timerSecondsLeft ?? 0
                    ZStack {
                        RingView(value: left > 0 ? Double(left) / 1500 : 0, color: Theme.accent)
                        Image(systemName: "timer").font(.system(size: 14, weight: .bold))
                    }
                    .frame(width: 44, height: 44)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(left > 0 ? Fmt.timer(left) : "Off")
                            .font(.system(size: 22, weight: .bold, design: .rounded)).monospacedDigit()
                        Text("Focus").font(.system(size: 13, design: .rounded)).foregroundStyle(Theme.dim)
                    }
                }
            }
        }
    }

    // ───────── music ─────────

    private var nowPlaying: some View {
        CardBox {
            HStack(spacing: 14) {
                Image(systemName: "music.note")
                    .font(.system(size: 22, weight: .semibold))
                    .foregroundStyle(Theme.accent)
                    .frame(width: 54, height: 54)
                    .background(Theme.cardHi, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                VStack(alignment: .leading, spacing: 3) {
                    Text(model.status?.title ?? "Nothing playing")
                        .font(.system(size: 17, weight: .semibold, design: .rounded)).lineLimit(1)
                    Text(model.status?.artist ?? "Music on your laptop")
                        .font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim).lineLimit(1)
                }
                Spacer(minLength: 0)
            }
            HStack(spacing: 10) {
                mediaButton("backward.fill") { model.act(nil) { try await $0.media(.prev) } }
                mediaButton((model.status?.playing ?? false) ? "pause.fill" : "play.fill", big: true) { model.act(nil) { try await $0.media(.toggle) } }
                mediaButton("forward.fill") { model.act(nil) { try await $0.media(.next) } }
            }
        }
    }

    private func mediaButton(_ icon: String, big: Bool = false, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: big ? 22 : 17, weight: .bold))
                .foregroundStyle(big ? Theme.onAccent : .white)
                .frame(maxWidth: .infinity)
                .frame(height: 48)
                .background(big ? Theme.accent : Theme.cardHi, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        }
        .buttonStyle(PressStyle())
    }

    // ───────── actions ─────────

    private var actions: some View {
        LazyVGrid(columns: [GridItem(.flexible(), spacing: 12), GridItem(.flexible(), spacing: 12)], spacing: 12) {
            if (model.status?.timerSecondsLeft ?? 0) > 0 {
                ActionTile(icon: "stop.circle", label: "Stop focus", tint: Theme.red) { model.act("Focus stopped") { try await $0.stopFocus() } }
            } else {
                ActionTile(icon: "timer", label: "Focus 25 min") { model.act("Focus timer started") { try await $0.focus(minutes: 25) } }
            }
            ActionTile(icon: "speaker.wave.3.fill", label: "Find laptop", tint: Theme.yellow) { model.act("Meow! The cat is calling") { try await $0.ping() } }
            ActionTile(icon: "lock.fill", label: "Lock laptop", tint: Theme.blue) { confirmLock = true }
            ActionTile(icon: "arrow.clockwise", label: "Refresh", tint: Theme.green) { Task { await model.refresh() } }
        }
    }

    // ───────── clipboard + link ─────────

    private var clipboard: some View {
        CardBox {
            HStack(spacing: 10) {
                SecondaryButton(title: "Send mine", icon: "arrow.up.doc.on.clipboard") {
                    if let s = UIPasteboard.general.string, !s.isEmpty { model.act("Sent to laptop clipboard") { try await $0.setLaptopClipboard(s) } }
                    else { model.show("Your clipboard is empty", error: true) }
                }
                SecondaryButton(title: "Get laptop's", icon: "arrow.down.doc") {
                    model.act(nil) { c in
                        let t = try await c.laptopClipboard()
                        await MainActor.run {
                            UIPasteboard.general.string = t
                            model.show(t.isEmpty ? "Laptop clipboard is empty" : "Copied from laptop")
                        }
                    }
                }
            }
            HStack(spacing: 10) {
                TextField("Or type something to send", text: $typed, axis: .vertical)
                    .lineLimit(1...4)
                    .focused($focused)
                    .padding(12)
                    .background(Theme.bg, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                Button {
                    let t = typed
                    guard !t.isEmpty else { return }
                    model.act("Sent to laptop clipboard") { try await $0.setLaptopClipboard(t) }
                    typed = ""; focused = false
                } label: {
                    Image(systemName: "paperplane.fill").font(.system(size: 17, weight: .bold)).foregroundStyle(Theme.onAccent)
                        .frame(width: 46, height: 46).background(Theme.accent, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                }
                .buttonStyle(PressStyle())
                .disabled(typed.isEmpty)
            }
        }
    }

    private var linkCard: some View {
        CardBox {
            HStack(spacing: 10) {
                TextField("https://…", text: $link)
                    .keyboardType(.URL).textInputAutocapitalization(.never).autocorrectionDisabled()
                    .padding(12)
                    .background(Theme.bg, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                Button("Paste") { link = UIPasteboard.general.url?.absoluteString ?? UIPasteboard.general.string ?? link }
                    .font(.system(size: 15, weight: .semibold, design: .rounded))
            }
            PrimaryButton(title: "Open on laptop", icon: "safari") {
                var u = link.trimmingCharacters(in: .whitespacesAndNewlines)
                if !u.isEmpty && !u.lowercased().hasPrefix("http") { u = "https://" + u }
                guard URL(string: u) != nil, !u.isEmpty else { model.show("That doesn't look like a link", error: true); return }
                model.act("Opened on laptop") { try await $0.openLink(u) }
                link = ""
            }
        }
    }

    private func salesCard(_ s: SalesInfo) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            SectionTitle(text: "Shopify today")
            CardBox {
                HStack {
                    Image(systemName: "bag.fill").foregroundStyle(.white)
                        .frame(width: 40, height: 40).background(Theme.shopify, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                    VStack(alignment: .leading) {
                        Text("\(s.orders) order\(s.orders == 1 ? "" : "s")").font(.system(size: 20, weight: .bold, design: .rounded))
                        Text(s.totals.sorted { $0.value > $1.value }.map { Fmt.money($0.value, $0.key) }.joined(separator: " + "))
                            .font(.system(size: 15, design: .rounded)).foregroundStyle(Theme.shopify)
                    }
                    Spacer()
                }
            }
        }
    }
}
