import Foundation
import Security
import CatBridgeCore

/// Remembers the paired laptop. The key goes in the Keychain (readable after first unlock, so Shortcuts
/// automations still work while the phone is locked); the address goes in UserDefaults.
enum PairingStore {
    private static let defaults = UserDefaults.standard

    static func load() -> Pairing? {
        guard let host = defaults.string(forKey: "host"), let key = Keychain.get("key") else { return nil }
        let port = defaults.integer(forKey: "port")
        let p = Pairing(host: host, port: port == 0 ? 47800 : port, key: key, name: defaults.string(forKey: "name") ?? "Laptop")
        return p.isValid ? p : nil
    }

    static func save(_ p: Pairing) {
        defaults.set(p.host, forKey: "host")
        defaults.set(p.port, forKey: "port")
        defaults.set(p.name, forKey: "name")
        Keychain.set(p.key, for: "key")
    }

    static func clear() {
        defaults.removeObject(forKey: "host")
        defaults.removeObject(forKey: "port")
        defaults.removeObject(forKey: "name")
        Keychain.delete("key")
    }
}

enum Keychain {
    private static func base(_ account: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "CatBridge", kSecAttrAccount as String: account]
    }

    static func set(_ value: String, for account: String) {
        SecItemDelete(base(account) as CFDictionary)
        var add = base(account)
        add[kSecValueData as String] = Data(value.utf8)
        add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        SecItemAdd(add as CFDictionary, nil)
    }

    static func get(_ account: String) -> String? {
        var q = base(account)
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let data = out as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func delete(_ account: String) { SecItemDelete(base(account) as CFDictionary) }
}
