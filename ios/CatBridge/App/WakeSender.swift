import Foundation
import Network
import CatBridgeCore

/// Sends Wake-on-LAN magic packets to the laptop's last known Wi-Fi/Ethernet addresses (only works on the same network).
enum WakeSender {
    private static let key = "netinfo.v1"

    static func remember(_ info: NetInfo) {
        if let d = try? JSONEncoder().encode(info) { UserDefaults.standard.set(d, forKey: key) }
    }

    static var known: NetInfo? {
        guard let d = UserDefaults.standard.data(forKey: key) else { return nil }
        return try? JSONDecoder().decode(NetInfo.self, from: d)
    }

    /// Returns how many packets were sent.
    static func send() async -> Int {
        guard let info = known else { return 0 }
        var sent = 0
        for a in info.adapters {
            guard let packet = WakeOnLan.packet(mac: a.mac) else { continue }
            for host in a.broadcasts + a.ips {
                for port: UInt16 in [9, 7] {
                    if await udp(packet, host: host, port: port) { sent += 1 }
                }
            }
        }
        return sent
    }

    private static func udp(_ data: Data, host: String, port: UInt16) async -> Bool {
        await withCheckedContinuation { (cont: CheckedContinuation<Bool, Never>) in
            let conn = NWConnection(host: NWEndpoint.Host(host), port: NWEndpoint.Port(rawValue: port)!, using: .udp)
            var finished = false
            let lock = NSLock()
            func finish(_ ok: Bool) {
                lock.lock(); defer { lock.unlock() }
                if finished { return }
                finished = true
                conn.cancel()
                cont.resume(returning: ok)
            }
            conn.stateUpdateHandler = { state in
                switch state {
                case .ready: conn.send(content: data, completion: .contentProcessed { err in finish(err == nil) })
                case .failed, .cancelled: finish(false)
                default: break
                }
            }
            conn.start(queue: .global(qos: .userInitiated))
            DispatchQueue.global().asyncAfter(deadline: .now() + 1.5) { finish(false) }
        }
    }
}
