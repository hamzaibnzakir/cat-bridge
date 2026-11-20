import AppIntents
import UIKit
import CatBridgeCore

/// Shortcuts actions. They run in the background (no app opening), using the saved pairing.

enum CatIntentError: Error, CustomLocalizedStringResourceConvertible {
    case notPaired
    case failed(String)

    var localizedStringResource: LocalizedStringResource {
        switch self {
        case .notPaired: return "Open Cat Bridge and pair it with your laptop first."
        case .failed(let why): return "\(why)"
        }
    }
}

func intentClient() throws -> BridgeClient {
    guard let p = PairingStore.load() else { throw CatIntentError.notPaired }
    return BridgeClient(pairing: p, timeout: 12)
}

func runIntent(_ work: (BridgeClient) async throws -> Void) async throws {
    let c = try intentClient()
    do { try await work(c) } catch let e as BridgeError { throw CatIntentError.failed(e.localizedDescription) }
}

struct SendToLaptopIntent: AppIntent {
    static var title: LocalizedStringResource = "Send to Laptop"
    static var openAppWhenRun: Bool = false

    @Parameter(title: "App") var app: String?
    @Parameter(title: "Title") var notificationTitle: String?
    @Parameter(title: "Subtitle") var subtitle: String?
    @Parameter(title: "Body") var body: String?

    func perform() async throws -> some IntentResult {
        let a = app ?? "iPhone", t = notificationTitle ?? "", s = subtitle ?? "", b = body ?? ""
        try await runIntent { try await $0.notify(app: a, title: t, subtitle: s, body: b) }
        return .result()
    }
}

struct UpdateLaptopIntent: AppIntent {
    static var title: LocalizedStringResource = "Update Laptop"
    static var openAppWhenRun: Bool = false

    @Parameter(title: "Focus") var focus: String?
    @Parameter(title: "Focus is on", default: true) var focusOn: Bool

    @MainActor
    func perform() async throws -> some IntentResult {
        UIDevice.current.isBatteryMonitoringEnabled = true
        let level = UIDevice.current.batteryLevel
        let state = UIDevice.current.batteryState
        let battery: Int? = level >= 0 ? Int((level * 100).rounded()) : nil
        let charging: Bool? = state == .unknown ? nil : (state == .charging || state == .full)
        var f: String? = nil
        if let focus, !focus.isEmpty { f = focusOn ? focus : "off" }
        try await runIntent { try await $0.phoneStatus(battery: battery, charging: charging, focus: f) }
        return .result()
    }
}

struct SendTextToLaptopIntent: AppIntent {
    static var title: LocalizedStringResource = "Copy to Laptop Clipboard"
    static var openAppWhenRun: Bool = false
    @Parameter(title: "Text") var text: String

    func perform() async throws -> some IntentResult {
        let t = text
        try await runIntent { try await $0.setLaptopClipboard(t) }
        return .result()
    }
}

struct OpenOnLaptopIntent: AppIntent {
    static var title: LocalizedStringResource = "Open on Laptop"
    static var openAppWhenRun: Bool = false
    @Parameter(title: "Link") var link: URL

    func perform() async throws -> some IntentResult {
        let u = link.absoluteString
        try await runIntent { try await $0.openLink(u) }
        return .result()
    }
}

struct SendFileToLaptopIntent: AppIntent {
    static var title: LocalizedStringResource = "Send File to Laptop"
    static var openAppWhenRun: Bool = false
    @Parameter(title: "File") var file: IntentFile

    func perform() async throws -> some IntentResult {
        let data = file.data
        let name = file.filename
        try await runIntent { try await $0.upload(data: data, name: name.isEmpty ? "From iPhone" : name) }
        return .result()
    }
}

struct FindMyLaptopIntent: AppIntent {
    static var title: LocalizedStringResource = "Find My Laptop"
    static var openAppWhenRun: Bool = false
    func perform() async throws -> some IntentResult {
        try await runIntent { try await $0.ping() }
        return .result()
    }
}

struct LockLaptopIntent: AppIntent {
    static var title: LocalizedStringResource = "Lock Laptop"
    static var openAppWhenRun: Bool = false
    func perform() async throws -> some IntentResult {
        try await runIntent { try await $0.lock() }
        return .result()
    }
}

struct StartFocusIntent: AppIntent {
    static var title: LocalizedStringResource = "Start Laptop Focus Timer"
    static var openAppWhenRun: Bool = false
    @Parameter(title: "Minutes", default: 25) var minutes: Int

    func perform() async throws -> some IntentResult {
        let m = max(1, min(240, minutes))
        try await runIntent { try await $0.focus(minutes: m) }
        return .result()
    }
}

struct CatBridgeShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: FindMyLaptopIntent(), phrases: ["Find my laptop with \(.applicationName)"], shortTitle: "Find My Laptop", systemImageName: "laptopcomputer")
        AppShortcut(intent: LockLaptopIntent(), phrases: ["Lock my laptop with \(.applicationName)"], shortTitle: "Lock Laptop", systemImageName: "lock")
        AppShortcut(intent: StartFocusIntent(), phrases: ["Start a laptop focus timer with \(.applicationName)"], shortTitle: "Focus Timer", systemImageName: "timer")
    }
}
