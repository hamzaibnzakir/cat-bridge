import Foundation
import SwiftUI
import UIKit
import CatBridgeCore

enum Connection: Equatable {
    case unknown, online, offline(String), notPaired
}

@MainActor
@Observable
final class AppModel {
    static let shared = AppModel()

    private(set) var pairing: Pairing?
    var status: LaptopStatus?
    var alerts: [AlertEntry] = []
    var inbox: [InboxItem] = []
    var shelf: [ShelfEntry] = []
    var connection: Connection = .unknown
    var lastUpdate: Date?
    var toast: Toast?
    var pairingError: String?
    var focus: FocusStats?
    var unlockState: UnlockSetup = UnlockKey.exists ? (UserDefaults.standard.bool(forKey: "unlock.trusted") ? .ready : .unknown) : .notSetUp
    var unlocking = false

    enum UnlockSetup: Equatable { case notSetUp, unknown, waiting(code: String), ready, denied, failed(String) }

    @ObservationIgnored private(set) var client: BridgeClient?
    @ObservationIgnored private var pollTask: Task<Void, Never>?
    @ObservationIgnored private var lastNetInfo: Date?

    struct Toast: Equatable, Identifiable {
        let id = UUID()
        let text: String
        let isError: Bool
    }

    init() {
        if let p = PairingStore.load() { setPairing(p) }
    }

    private func setPairing(_ p: Pairing?) {
        pairing = p
        client = p.map { BridgeClient(pairing: $0) }
        connection = p == nil ? .notPaired : .unknown
    }

    var isPaired: Bool { pairing != nil }

    // ───────── pairing ─────────

    func handle(url: URL) {
        if url.host == "unlock" { Task { await unlockLaptop() }; return }
        if let p = Pairing.parse(url.absoluteString) { Task { await pair(p) } }
    }

    /// Checks the laptop answers with this key before saving it.
    @discardableResult
    func pair(_ p: Pairing) async -> Bool {
        pairingError = nil
        let c = BridgeClient(pairing: p, timeout: 8)
        do {
            let hello = try await c.hello()
            var named = p
            named.name = hello.device
            PairingStore.save(named)
            setPairing(named)
            show("Paired with \(hello.device)")
            UINotificationFeedbackGenerator().notificationOccurred(.success)
            await refresh()
            return true
        } catch {
            pairingError = error.localizedDescription
            UINotificationFeedbackGenerator().notificationOccurred(.error)
            return false
        }
    }

    func unpair() {
        PairingStore.clear()
        setPairing(nil)
        status = nil; alerts = []; inbox = []; shelf = []
    }

    // ───────── live updates ─────────

    func startPolling() {
        pollTask?.cancel()
        pollTask = Task { [weak self] in
            while !Task.isCancelled {
                await self?.refresh()
                try? await Task.sleep(nanoseconds: 3_000_000_000)
            }
        }
    }

    func stopPolling() { pollTask?.cancel(); pollTask = nil }

    func refresh() async {
        guard let c = client else { connection = .notPaired; return }
        do {
            async let s = c.status()
            async let a = c.alerts()
            async let i = c.inbox()
            async let sh = c.shelf()
            let (st, al, inb, shl) = try await (s, a, i, sh)
            status = st; alerts = al; inbox = inb; shelf = shl
            connection = .online
            lastUpdate = Date()
            focus = try? await c.focusStats()
            if WakeSender.known == nil || (lastNetInfo.map { Date().timeIntervalSince($0) > 3600 } ?? true) {
                if let n = try? await c.netInfo() { WakeSender.remember(n); lastNetInfo = Date() }
            }
        } catch let e as BridgeError where e == .notPaired {
            connection = .notPaired
        } catch {
            connection = .offline(error.localizedDescription)
        }
    }

    // ───────── actions ─────────

    /// Runs a laptop action with a little confirmation (or the error) at the top of the screen.
    func act(_ done: String? = nil, haptic: Bool = true, _ work: @escaping (BridgeClient) async throws -> Void) {
        guard let c = client else { show("Pair with your laptop first", error: true); return }
        Task {
            do {
                try await work(c)
                if haptic { UIImpactFeedbackGenerator(style: .light).impactOccurred() }
                if let done { show(done) }
                await refresh()
            } catch {
                show(error.localizedDescription, error: true)
            }
        }
    }

    // ───────── unlock with Face ID ─────────

    /// Wakes the laptop's screen, gets a one-time challenge from its lock screen, signs it with Face ID and sends it back.
    func unlockLaptop() async {
        guard let c = client else { show("Pair with your laptop first", error: true); return }
        guard UnlockKey.exists else { show("Set up unlock first: Settings → Unlock laptop with Face ID", error: true); return }
        guard !unlocking else { return }
        unlocking = true
        defer { unlocking = false }
        do {
            try? await c.wake()
            let ch = try await c.unlockChallenge()
            let message = ch.message
            let name = ch.machine
            let sig = try await Task.detached(priority: .userInitiated) { try UnlockKey.sign(message, reason: "Unlock \(name)") }.value
            try await c.unlockApprove(nonce: ch.nonce, signature: sig)
            UINotificationFeedbackGenerator().notificationOccurred(.success)
            show("Unlocked \(name)")
        } catch let e as BridgeError {
            show(e.localizedDescription, error: true)
        } catch {
            if (error as NSError).code != 3 { show(error.localizedDescription, error: true) } // 3 = cancelled
        }
    }

    /// Creates the Face ID key and asks the laptop to trust it (you press Allow on the laptop).
    func setUpUnlock(name: String) async {
        guard let c = client else { return }
        do {
            let pub = try UnlockKey.create()
            let r = try await c.unlockRegister(name: name.isEmpty ? "iPhone" : name, publicKey: pub)
            UserDefaults.standard.set(false, forKey: "unlock.trusted")
            if r.state == "trusted" { markTrusted(); return }
            unlockState = .waiting(code: r.code ?? "")
            for _ in 0..<120 {
                try await Task.sleep(nanoseconds: 2_000_000_000)
                guard case .waiting = unlockState else { return }
                let st = try await c.unlockRegistration(publicKey: pub)
                if st.state == "trusted" { markTrusted(); return }
                if st.state == "denied" { unlockState = .denied; UnlockKey.delete(); return }
            }
            unlockState = .failed("Nobody pressed Allow on the laptop. Try again.")
        } catch {
            unlockState = .failed(error.localizedDescription)
        }
    }

    /// Checks with the laptop whether this iPhone is still trusted.
    func checkUnlock() async {
        guard let c = client, let pub = UnlockKey.publicKey() else { unlockState = .notSetUp; return }
        if let st = try? await c.unlockRegistration(publicKey: pub) {
            if st.state == "trusted" { markTrusted() }
            else if case .waiting = unlockState {} else { unlockState = .notSetUp; UserDefaults.standard.set(false, forKey: "unlock.trusted") }
        }
    }

    private func markTrusted() {
        unlockState = .ready
        UserDefaults.standard.set(true, forKey: "unlock.trusted")
        UINotificationFeedbackGenerator().notificationOccurred(.success)
        show("Face ID can now unlock your laptop")
    }

    func removeUnlock() {
        UnlockKey.delete()
        UserDefaults.standard.set(false, forKey: "unlock.trusted")
        unlockState = .notSetUp
    }

    // ───────── wake ─────────

    func wakeLaptop() async {
        if let c = client, (try? await c.wake()) != nil { show("Screen is on"); return }
        let n = await WakeSender.send()
        show(n > 0 ? "Wake-up signal sent. It works on the same Wi-Fi, with Wake-on-LAN on." : "Can't reach the laptop, and it hasn't shared its Wi-Fi details yet.", error: n == 0)
    }

    func show(_ text: String, error: Bool = false) {
        let t = Toast(text: text, isError: error)
        withAnimation(.spring(response: 0.35, dampingFraction: 0.8)) { toast = t }
        Task {
            try? await Task.sleep(nanoseconds: 2_600_000_000)
            if toast == t { withAnimation(.easeOut(duration: 0.25)) { toast = nil } }
        }
    }
}
