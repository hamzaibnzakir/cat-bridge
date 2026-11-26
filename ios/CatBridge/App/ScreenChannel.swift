import Foundation
import SwiftUI
import UIKit

/// The laptop's screen, live, for the top half of the Remote tab.
/// Frames are pulled: after each JPEG arrives we ask for the next one, so a slow connection never builds a backlog
/// and what you see is always current. The laptop only sends a frame when something on screen changed.
@MainActor
@Observable
final class ScreenChannel {
    enum State: Equatable { case idle, connecting, live, locked, off(String), failed(String) }

    enum Quality: String, CaseIterable, Identifiable {
        case sharp, smooth, saver
        var id: String { rawValue }
        var title: String {
            switch self { case .sharp: "Sharp"; case .smooth: "Smooth"; case .saver: "Data saver" }
        }
        var detail: String {
            switch self { case .sharp: "Best picture, for Wi-Fi"; case .smooth: "More frames per second"; case .saver: "For mobile data" }
        }
        /// (share of the view's pixel width, JPEG quality, frames per second)
        var settings: (widthFactor: Double, jpeg: Int, fps: Int) {
            switch self {
            case .sharp: (1.0, 72, 12)
            case .smooth: (0.75, 58, 20)
            case .saver: (0.5, 42, 6)
            }
        }
    }

    var state: State = .idle
    var image: UIImage?
    /// The laptop's screen size in real pixels (from "screen W H").
    var screenSize = CGSize(width: 16, height: 10)
    var fps: Double = 0
    var kilobytesPerSecond: Double = 0
    var quality: Quality = Quality(rawValue: UserDefaults.standard.string(forKey: "screen.quality") ?? "") ?? .sharp {
        didSet { UserDefaults.standard.set(quality.rawValue, forKey: "screen.quality"); sendSettings() }
    }

    @ObservationIgnored private var task: URLSessionWebSocketTask?
    @ObservationIgnored private var viewPixelWidth: Double = 1170
    @ObservationIgnored private var frameTimes: [Date] = []
    @ObservationIgnored private var byteLog: [(Date, Int)] = []
    @ObservationIgnored private var decoding = false

    func connect(_ url: URL?) {
        guard let url else { state = .failed("Pair with your laptop first."); return }
        disconnect()
        state = .connecting
        let t = URLSession.shared.webSocketTask(with: url)
        t.maximumMessageSize = 16 * 1024 * 1024
        task = t
        t.resume()
        sendSettings()
        // two requests in flight: one frame travels while the next is being captured
        t.send(.string("next\nnext")) { _ in }
        receive(t)
    }

    func disconnect() {
        task?.cancel(with: .goingAway, reason: nil)
        task = nil
        fps = 0; kilobytesPerSecond = 0
        if state == .live || state == .connecting || state == .locked { state = .idle }
    }

    /// The width the picture is shown at, in pixels, so the laptop never sends more detail than the phone can show.
    func setViewWidth(points: CGFloat, scale: CGFloat) {
        let px = Double(points * scale)
        guard px > 100, abs(px - viewPixelWidth) > 40 else { return }
        viewPixelWidth = px
        sendSettings()
    }

    private func sendSettings() {
        let s = quality.settings
        let width = Int(min(2560, max(480, viewPixelWidth * s.widthFactor)))
        task?.send(.string("q \(width) \(s.jpeg) \(s.fps)")) { _ in }
    }

    private func receive(_ t: URLSessionWebSocketTask) {
        t.receive { [weak self] result in
            Task { @MainActor in
                guard let self, self.task === t else { return }
                switch result {
                case .success(.data(let data)):
                    self.gotFrame(data, on: t)
                    self.receive(t)
                case .success(.string(let s)):
                    self.gotText(s)
                    self.receive(t)
                case .success:
                    self.receive(t)
                case .failure(let e):
                    if case .off = self.state { return }
                    self.state = .failed("Lost the screen (\(e.localizedDescription)).")
                }
            }
        }
    }

    private func gotText(_ s: String) {
        let parts = s.split(separator: " ")
        switch parts.first {
        case "screen":
            if parts.count >= 3, let w = Double(parts[1]), let h = Double(parts[2]), w > 0, h > 0 { screenSize = CGSize(width: w, height: h) }
            if state == .connecting { state = .live }
        case "locked":
            state = .locked
        case "unlocked":
            state = .live
        case "off":
            state = .off(parts.dropFirst().joined(separator: " "))
            task?.cancel(with: .normalClosure, reason: nil)
            task = nil
        default:
            break
        }
    }

    private func gotFrame(_ data: Data, on t: URLSessionWebSocketTask) {
        // ask for the next one straight away; decoding happens while it's on its way
        t.send(.string("next")) { _ in }
        let now = Date()
        frameTimes.append(now); frameTimes.removeAll { now.timeIntervalSince($0) > 2 }
        byteLog.append((now, data.count)); byteLog.removeAll { now.timeIntervalSince($0.0) > 2 }
        fps = Double(frameTimes.count) / 2
        kilobytesPerSecond = Double(byteLog.reduce(0) { $0 + $1.1 }) / 2048
        if state != .live { state = .live }
        guard !decoding else { return }   // a newer frame will follow; never queue stale ones
        decoding = true
        Task.detached(priority: .userInitiated) {
            let img = UIImage(data: data)?.preparingForDisplay()
            await MainActor.run {
                self.decoding = false
                if let img { self.image = img }
            }
        }
    }
}
