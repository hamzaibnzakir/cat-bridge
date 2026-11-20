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
    var sales: SalesInfo?
    var connection: Connection = .unknown
    var lastUpdate: Date?
    var toast: Toast?
    var pairingError: String?

    @ObservationIgnored private(set) var client: BridgeClient?
    @ObservationIgnored private var pollTask: Task<Void, Never>?

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
        status = nil; alerts = []; inbox = []; shelf = []; sales = nil
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
            async let sa = c.sales()
            let (st, al, inb, shl, sal) = try await (s, a, i, sh, sa)
            status = st; alerts = al; inbox = inb; shelf = shl; sales = sal
            connection = .online
            lastUpdate = Date()
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

    func show(_ text: String, error: Bool = false) {
        let t = Toast(text: text, isError: error)
        withAnimation(.spring(response: 0.35, dampingFraction: 0.8)) { toast = t }
        Task {
            try? await Task.sleep(nanoseconds: 2_600_000_000)
            if toast == t { withAnimation(.easeOut(duration: 0.25)) { toast = nil } }
        }
    }
}
