import Foundation

/// Builds Wake-on-LAN "magic packets": 6 × 0xFF followed by the MAC address 16 times.
public enum WakeOnLan {
    public static func packet(mac: String) -> Data? {
        let hex = mac.replacingOccurrences(of: ":", with: "").replacingOccurrences(of: "-", with: "")
        guard hex.count == 12 else { return nil }
        var bytes = [UInt8]()
        var i = hex.startIndex
        while i < hex.endIndex {
            let j = hex.index(i, offsetBy: 2)
            guard let b = UInt8(hex[i..<j], radix: 16) else { return nil }
            bytes.append(b)
            i = j
        }
        var p = [UInt8](repeating: 0xFF, count: 6)
        for _ in 0..<16 { p += bytes }
        return Data(p)
    }
}
