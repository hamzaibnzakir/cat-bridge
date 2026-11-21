import Foundation
import SwiftUI
import UIKit

/// The live trackpad/keyboard connection (a WebSocket to the laptop). Pointer moves are merged and sent ~60 times a second.
@MainActor
@Observable
final class InputChannel {
    enum State: Equatable { case idle, connecting, connected(String), failed(String) }

    var state: State = .idle
    var sensitivity: Double = UserDefaults.standard.object(forKey: "trackpad.speed") as? Double ?? 1.6 {
        didSet { UserDefaults.standard.set(sensitivity, forKey: "trackpad.speed") }
    }

    @ObservationIgnored private var task: URLSessionWebSocketTask?
    @ObservationIgnored private var lines: [String] = []
    @ObservationIgnored private var moveX: Double = 0
    @ObservationIgnored private var moveY: Double = 0
    @ObservationIgnored private var scheduled = false

    func connect(_ url: URL?) {
        guard let url else { state = .failed("Pair with your laptop first."); return }
        disconnect()
        state = .connecting
        let t = URLSession.shared.webSocketTask(with: url)
        task = t
        t.resume()
        receive(t)
    }

    func disconnect() {
        task?.cancel(with: .goingAway, reason: nil)
        task = nil
        if case .connected = state { state = .idle }
    }

    private func receive(_ t: URLSessionWebSocketTask) {
        t.receive { [weak self] result in
            Task { @MainActor in
                guard let self, self.task === t else { return }
                switch result {
                case .success(.string(let s)):
                    if s.hasPrefix("hi ") {
                        let name = s.split(separator: " ").dropFirst().first.map(String.init) ?? "Laptop"
                        self.state = .connected(name)
                        UIImpactFeedbackGenerator(style: .soft).impactOccurred()
                    } else if s == "locked" {
                        self.state = .failed("Your laptop is locked. Unlock it first.")
                    }
                    self.receive(t)
                case .success:
                    self.receive(t)
                case .failure(let e):
                    self.state = .failed("Lost the connection (\(e.localizedDescription)).")
                }
            }
        }
    }

    // ───────── sending ─────────

    func move(_ dx: Double, _ dy: Double) {
        moveX += dx; moveY += dy
        schedule()
    }

    func send(_ line: String) {
        flushMove()
        lines.append(line)
        schedule()
    }

    func click(_ button: String = "l") { send("c \(button)") }
    func key(_ name: String) { send("k \(name)") }
    func shortcut(_ combo: String) { send("h \(combo)") }
    func type(_ text: String) { send("t \(text)") }

    private func flushMove() {
        if abs(moveX) > 0.01 || abs(moveY) > 0.01 {
            lines.append(String(format: "m %.2f %.2f", moveX, moveY))
            moveX = 0; moveY = 0
        }
    }

    private func schedule() {
        guard !scheduled else { return }
        scheduled = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.016) { [weak self] in self?.flush() }
    }

    private func flush() {
        scheduled = false
        flushMove()
        guard !lines.isEmpty, let task else { lines.removeAll(); return }
        let text = lines.joined(separator: "\n")
        lines.removeAll()
        task.send(.string(text)) { _ in }
    }
}
