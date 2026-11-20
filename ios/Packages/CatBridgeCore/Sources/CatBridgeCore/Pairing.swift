import Foundation

/// Where the laptop is and the key to talk to it. Comes from the QR code in Cat Center → iPhone.
public struct Pairing: Codable, Equatable, Sendable {
    public var host: String
    public var port: Int
    public var key: String
    public var name: String

    public init(host: String, port: Int = 47800, key: String, name: String = "Laptop") {
        self.host = host; self.port = port; self.key = key; self.name = name
    }

    public var baseURL: URL? {
        var c = URLComponents()
        c.scheme = "http"
        c.host = host.contains(":") && !host.hasPrefix("[") ? "[\(host)]" : host // IPv6 needs brackets
        c.port = port
        return c.url
    }

    public var isValid: Bool { !host.isEmpty && (1...65535).contains(port) && key.count >= 16 && baseURL != nil }

    /// Parses `catbridge://pair?host=100.x.y.z&port=47800&key=…&name=…`.
    /// Also accepts the older web link `https://laptop.tailnet.ts.net/#k=KEY` (port 47800 on that host).
    public static func parse(_ text: String) -> Pairing? {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let comps = URLComponents(string: trimmed) else { return nil }
        if comps.scheme?.lowercased() == "catbridge" {
            let items = Dictionary((comps.queryItems ?? []).map { ($0.name.lowercased(), $0.value ?? "") }, uniquingKeysWith: { a, _ in a })
            guard let host = items["host"], !host.isEmpty, let key = items["key"], !key.isEmpty else { return nil }
            let port = Int(items["port"] ?? "") ?? 47800
            let p = Pairing(host: host, port: port, key: key, name: (items["name"]?.isEmpty == false ? items["name"]! : "Laptop"))
            return p.isValid ? p : nil
        }
        if let frag = comps.fragment, frag.hasPrefix("k="), let host = comps.host {
            let p = Pairing(host: host, port: 47800, key: String(frag.dropFirst(2)), name: host.components(separatedBy: ".").first ?? "Laptop")
            return p.isValid ? p : nil
        }
        return nil
    }
}
