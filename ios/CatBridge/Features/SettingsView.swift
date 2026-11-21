import SwiftUI
import UIKit
import CatBridgeCore

struct SettingsView: View {
    @Environment(AppModel.self) private var model
    @State private var scanning = false
    @State private var confirmUnpair = false
    @State private var testResult: String?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    LabeledContent("Laptop", value: model.pairing?.name ?? "—")
                    LabeledContent("Address", value: model.pairing.map { "\($0.host):\($0.port)" } ?? "—")
                    LabeledContent("Key", value: model.pairing.map { "••••" + String($0.key.suffix(4)) } ?? "—")
                    Button("Test connection") {
                        testResult = "Checking…"
                        Task {
                            guard let c = model.client else { return }
                            do { let h = try await c.hello(); testResult = "Connected to \(h.device) (Dynamic Island \(h.version))" }
                            catch { testResult = error.localizedDescription }
                        }
                    }
                    if let testResult { Text(testResult).font(.footnote).foregroundStyle(Theme.dim) }
                    Button("Scan a new code") { scanning = true }
                    Button("Unpair", role: .destructive) { confirmUnpair = true }
                } header: { Text("Your laptop") } footer: {
                    Text("Works anywhere both devices are on Tailscale. Only devices in your tailnet can reach the laptop, and each request needs this key.")
                }

                UnlockSection()

                Section {
                    Text("Your laptop sends pop-up alerts through the free Bark app: timers, low battery, servers going down, your Windows notifications while you're away, and \"Ring my iPhone\".")
                        .font(.subheadline).foregroundStyle(Theme.dim)
                    Link(destination: URL(string: "https://apps.apple.com/app/bark/id1403753865")!) { Label("Get Bark", systemImage: "arrow.down.app") }
                    NavigationLink("Set up Bark") { BarkGuide() }
                } header: { Text("Alerts from your laptop") }

                Section {
                    NavigationLink("Shopify, WhatsApp and other apps") { ShortcutsGuide() }
                    NavigationLink("Battery and Focus") { StatusGuide() }
                } header: { Text("Send your iPhone's notifications to the laptop") } footer: {
                    Text("Uses Shortcuts automations with Cat Bridge's \"Send to Laptop\" and \"Update Laptop\" actions.")
                }

                Section {
                    LabeledContent("Version", value: (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? "1.0")
                    Link(destination: URL(string: "tailscale://")!) { Label("Open Tailscale", systemImage: "network") }
                } header: { Text("About") }
            }
            .scrollContentBackground(.hidden)
            .background(Theme.bg)
            .navigationTitle("Settings")
            .sheet(isPresented: $scanning) { ScanSheet() }
            .confirmationDialog("Unpair this iPhone?", isPresented: $confirmUnpair, titleVisibility: .visible) {
                Button("Unpair", role: .destructive) { model.unpair() }
            }
        }
    }
}

struct GuideStep: View {
    let n: Int
    let text: String
    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(n)")
                .font(.system(size: 13, weight: .bold, design: .rounded))
                .foregroundStyle(Theme.accent)
                .frame(width: 26, height: 26)
                .background(Theme.cardHi, in: Circle())
            Text(text).font(.system(size: 15, design: .rounded))
            Spacer(minLength: 0)
        }
    }
}

struct GuidePage<Content: View>: View {
    let title: String
    @ViewBuilder var content: Content
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) { content }
                .padding(16)
        }
        .background(Theme.bg)
        .navigationTitle(title)
        .navigationBarTitleDisplayMode(.inline)
    }
}

struct BarkGuide: View {
    var body: some View {
        GuidePage(title: "Bark alerts") {
            CardBox {
                GuideStep(n: 1, text: "Install Bark from the App Store, open it and allow notifications.")
                GuideStep(n: 2, text: "Bark shows a sample link like https://api.day.app/YOURKEY/… Copy the key part.")
                GuideStep(n: 3, text: "On the laptop: Cat Center → iPhone → paste it into Bark key, then tap Send a test.")
                GuideStep(n: 4, text: "Recommended: Bark → Settings → Encryption → AES-128, CBC. Copy the key and IV into Cat Center too, so only your iPhone can read alerts.")
            }
            Text("Bark is free and open source. Your sideloaded Cat Bridge app can't receive push notifications by itself without a paid Apple developer account, so Bark delivers them.")
                .font(.footnote).foregroundStyle(Theme.dim)
        }
    }
}

struct ShortcutsGuide: View {
    var body: some View {
        GuidePage(title: "App notifications") {
            CardBox {
                GuideStep(n: 1, text: "Open Shortcuts → Automation → New Automation.")
                GuideStep(n: 2, text: "Choose App Notification (Notification), pick Shopify (or WhatsApp, Messages, Mail…), and select Run Immediately.")
                GuideStep(n: 3, text: "Add the action Send to Laptop (Cat Bridge).")
                GuideStep(n: 4, text: "Set App to the app's name. Tap Title and choose the notification's Title; do the same for Subtitle and Body.")
                GuideStep(n: 5, text: "Done. Those notifications now pop up on the laptop too.")
            }
            Text("iOS lets each automation watch one app, so make one per app you care about. If iOS asks before running, check that Run Immediately is on.")
                .font(.footnote).foregroundStyle(Theme.dim)
        }
    }
}

struct StatusGuide: View {
    var body: some View {
        GuidePage(title: "Battery and Focus") {
            CardBox {
                GuideStep(n: 1, text: "Automation → Battery Level → choose \"Falls below 20%\" (and another for \"Rises above 80%\") → Run Immediately.")
                GuideStep(n: 2, text: "Add Update Laptop (Cat Bridge). It reads the battery by itself.")
                GuideStep(n: 3, text: "Do the same for Charger (is connected / disconnected).")
                GuideStep(n: 4, text: "For Focus: Automation → Focus → pick one (e.g. Work) → When turning on. Add Update Laptop and type the Focus name. Make a second one for When turning off with Focus is on switched off.")
            }
            Text("The island's iPhone widget shows the battery ring and your Focus.")
                .font(.footnote).foregroundStyle(Theme.dim)
        }
    }
}

/// Settings → Unlock laptop with Face ID.
struct UnlockSection: View {
    @Environment(AppModel.self) private var model
    @State private var name = UserDefaults.standard.string(forKey: "unlock.name") ?? "My iPhone"
    @State private var busy = false

    var body: some View {
        Section {
            switch model.unlockState {
            case .ready:
                Label("Face ID can unlock your laptop", systemImage: "checkmark.seal.fill").foregroundStyle(Theme.green)
                Button("Unlock laptop now") { Task { await model.unlockLaptop() } }
                Button("Stop using Face ID for this", role: .destructive) { model.removeUnlock() }
            case .waiting(let code):
                VStack(alignment: .leading, spacing: 8) {
                    Text("On the laptop, Cat Center shows a request. Check this code matches, then press Allow:")
                        .font(.subheadline).foregroundStyle(Theme.dim)
                    Text(code.count == 6 ? String(code.prefix(3)) + " " + String(code.suffix(3)) : code)
                        .font(.system(size: 34, weight: .bold, design: .monospaced)).foregroundStyle(Theme.accent)
                    HStack { ProgressView(); Text("Waiting for the laptop…").font(.footnote).foregroundStyle(Theme.dim) }
                }
                Button("Cancel", role: .destructive) { model.removeUnlock() }
            default:
                Text("Unlock your locked laptop with Face ID from this iPhone. Your Face ID key never leaves the iPhone's Secure Enclave.")
                    .font(.subheadline).foregroundStyle(Theme.dim)
                if case .denied = model.unlockState { Text("The laptop said no.").font(.footnote).foregroundStyle(Theme.red) }
                if case .failed(let why) = model.unlockState { Text(why).font(.footnote).foregroundStyle(Theme.red) }
                TextField("Name shown on the laptop", text: $name)
                Button(busy ? "Setting up…" : "Set up") {
                    busy = true
                    UserDefaults.standard.set(name, forKey: "unlock.name")
                    Task { await model.setUpUnlock(name: name); busy = false }
                }
                .disabled(busy)
            }
        } header: { Text("Unlock laptop with Face ID") } footer: {
            Text("Needs Cat Face turned on for the laptop's sign-in screen. Works when you're signed in and the laptop is locked.")
        }
        .task { if model.unlockState == .unknown { await model.checkUnlock() } }
    }
}
