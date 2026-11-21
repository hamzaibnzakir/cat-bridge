import Foundation
import Security
import CatBridgeCore

/// Remembers the paired laptop in the Keychain (readable after first unlock, so Shortcuts automations still work while
/// the phone is locked). It's also written to a shared Keychain group so the "Send to Laptop" share sheet can use it;
/// if the signing setup doesn't allow sharing, the share sheet asks for the pairing link once instead.
enum PairingStore {
    private static let account = "pairing.v2"

    static func load() -> Pairing? {
        if let data = Keychain.get(account), let p = try? JSONDecoder().decode(Pairing.self, from: data), p.isValid { return p }
        // v2.0 stored the address in UserDefaults and only the key in the Keychain
        let d = UserDefaults.standard
        if let host = d.string(forKey: "host"), let keyData = Keychain.get("key"), let key = String(data: keyData, encoding: .utf8) {
            let port = d.integer(forKey: "port")
            let p = Pairing(host: host, port: port == 0 ? 47800 : port, key: key, name: d.string(forKey: "name") ?? "Laptop")
            if p.isValid { save(p); return p }
        }
        return nil
    }

    static func save(_ p: Pairing) {
        if let data = try? JSONEncoder().encode(p) { Keychain.set(data, for: account) }
    }

    static func clear() {
        Keychain.delete(account)
        Keychain.delete("key")
        for k in ["host", "port", "name"] { UserDefaults.standard.removeObject(forKey: k) }
    }
}

enum Keychain {
    private static let service = "CatBridge"

    /// "TEAMID.app.catbridge.shared", worked out from this app's own default access group.
    static let sharedGroup: String? = {
        let probe = "catbridge.probe"
        var add: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: probe,
                                  kSecValueData as String: Data([1]), kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock]
        SecItemAdd(add as CFDictionary, nil)
        add.removeValue(forKey: kSecValueData as String)
        add.removeValue(forKey: kSecAttrAccessible as String)
        var q = add
        q[kSecReturnAttributes as String] = true
        var out: AnyObject?
        defer { SecItemDelete(add as CFDictionary) }
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess,
              let attrs = out as? [String: Any], let group = attrs[kSecAttrAccessGroup as String] as? String,
              let dot = group.firstIndex(of: ".") else { return nil }
        return String(group[..<dot]) + ".app.catbridge.shared"
    }()

    private static func base(_ account: String, group: String?) -> [String: Any] {
        var q: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
        if let group { q[kSecAttrAccessGroup as String] = group }
        return q
    }

    @discardableResult
    private static func write(_ data: Data, _ account: String, group: String?) -> Bool {
        SecItemDelete(base(account, group: group) as CFDictionary)
        var add = base(account, group: group)
        add[kSecValueData as String] = data
        add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        return SecItemAdd(add as CFDictionary, nil) == errSecSuccess
    }

    private static func read(_ account: String, group: String?) -> Data? {
        var q = base(account, group: group)
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess else { return nil }
        return out as? Data
    }

    static func set(_ data: Data, for account: String) {
        write(data, account, group: nil)
        if let g = sharedGroup { write(data, account, group: g) }
    }

    static func get(_ account: String) -> Data? {
        if let g = sharedGroup, let d = read(account, group: g) { return d }
        return read(account, group: nil)
    }

    static func delete(_ account: String) {
        SecItemDelete(base(account, group: nil) as CFDictionary)
        if let g = sharedGroup { SecItemDelete(base(account, group: g) as CFDictionary) }
    }
}
