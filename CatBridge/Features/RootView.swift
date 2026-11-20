import SwiftUI
import CatBridgeCore

struct RootView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.scenePhase) private var phase

    var body: some View {
        ZStack(alignment: .top) {
            Theme.bg.ignoresSafeArea()
            if model.isPaired {
                TabView {
                    HomeView().tabItem { Label("Laptop", systemImage: "laptopcomputer") }
                    AlertsView().tabItem { Label("Alerts", systemImage: "bell.badge") }
                    FilesView().tabItem { Label("Files", systemImage: "folder") }
                    SettingsView().tabItem { Label("Settings", systemImage: "gearshape") }
                }
            } else {
                OnboardingView()
            }
            if let t = model.toast {
                ToastView(toast: t)
                    .transition(.move(edge: .top).combined(with: .opacity))
                    .padding(.top, 6)
                    .zIndex(1)
            }
        }
        .onChange(of: phase, initial: true) { _, p in
            if p == .active && model.isPaired { model.startPolling() } else { model.stopPolling() }
        }
        .onChange(of: model.isPaired) { _, paired in
            if paired && phase == .active { model.startPolling() }
        }
    }
}

/// First run: pair with the laptop.
struct OnboardingView: View {
    @Environment(AppModel.self) private var model
    @State private var scanning = false
    @State private var manual = false

    var body: some View {
        VStack(spacing: 22) {
            Spacer()
            CatFaceView(mood: .happy).frame(width: 150)
            VStack(spacing: 10) {
                Text("Cat Bridge")
                    .font(.system(size: 34, weight: .bold, design: .rounded))
                Text("Your iPhone and your laptop, together over Tailscale. Clipboard, files, music, alerts and more.")
                    .font(.system(size: 16, design: .rounded))
                    .foregroundStyle(Theme.dim)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 30)
            }
            Spacer()
            VStack(spacing: 12) {
                Text("On the laptop: Cat Center → iPhone shows a pairing code.")
                    .font(.system(size: 14, design: .rounded))
                    .foregroundStyle(Theme.dim)
                    .multilineTextAlignment(.center)
                PrimaryButton(title: "Scan pairing code", icon: "qrcode.viewfinder") { scanning = true }
                SecondaryButton(title: "Enter details instead", icon: "keyboard") { manual = true }
                if let err = model.pairingError {
                    Text(err).font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.red).multilineTextAlignment(.center)
                }
            }
            .padding(.horizontal, 24)
            .padding(.bottom, 30)
        }
        .sheet(isPresented: $scanning) { ScanSheet() }
        .sheet(isPresented: $manual) { ManualPairSheet() }
    }
}

struct ScanSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var message: String?
    @State private var checking = false

    var body: some View {
        ZStack {
            QRScannerView { code in
                guard !checking else { return }
                guard let p = Pairing.parse(code) else { message = "That isn't a Cat Bridge code."; return }
                checking = true
                message = "Checking your laptop…"
                Task {
                    if await model.pair(p) { dismiss() }
                    else { message = model.pairingError; checking = false }
                }
            }
            .ignoresSafeArea()
            RoundedRectangle(cornerRadius: 28, style: .continuous)
                .stroke(Theme.accent, lineWidth: 4)
                .frame(width: 250, height: 250)
            VStack {
                HStack {
                    Spacer()
                    Button { dismiss() } label: {
                        Image(systemName: "xmark").font(.system(size: 16, weight: .bold)).padding(12).background(.ultraThinMaterial, in: Circle())
                    }
                }
                .padding()
                Spacer()
                Text(message ?? "Point at the code in Cat Center → iPhone")
                    .font(.system(size: 15, weight: .semibold, design: .rounded))
                    .padding(.horizontal, 16).padding(.vertical, 10)
                    .background(.ultraThinMaterial, in: Capsule())
                    .padding(.bottom, 40)
            }
        }
    }
}

struct ManualPairSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var host = ""
    @State private var port = "47800"
    @State private var key = ""
    @State private var busy = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("100.x.y.z or laptop.tailnet.ts.net", text: $host)
                        .textInputAutocapitalization(.never).autocorrectionDisabled().keyboardType(.URL)
                    TextField("Port", text: $port).keyboardType(.numberPad)
                } header: { Text("Laptop's Tailscale address") } footer: { Text("Shown in Cat Center → iPhone on the laptop.") }
                Section {
                    TextField("Pairing key", text: $key)
                        .textInputAutocapitalization(.never).autocorrectionDisabled().font(.system(.body, design: .monospaced))
                } header: { Text("Pairing key") } footer: {
                    if let err = model.pairingError { Text(err).foregroundStyle(Theme.red) }
                }
                Section {
                    Button(busy ? "Checking…" : "Pair") {
                        busy = true
                        Task {
                            let p = Pairing(host: host.trimmingCharacters(in: .whitespaces), port: Int(port) ?? 47800, key: key.trimmingCharacters(in: .whitespaces))
                            if p.isValid, await model.pair(p) { dismiss() }
                            else if !p.isValid { model.pairingError = "Check the address and the key (it's at least 16 characters)." }
                            busy = false
                        }
                    }
                    .disabled(busy || host.isEmpty || key.isEmpty)
                }
            }
            .scrollContentBackground(.hidden)
            .background(Theme.bg)
            .navigationTitle("Pair with laptop")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } } }
        }
    }
}
