import Foundation
import Security
import LocalAuthentication

/// The key that unlocks your laptop. It's created inside the iPhone's Secure Enclave, never leaves it,
/// and can only sign after Face ID (or Touch ID) — and stops working if someone adds a new face.
enum UnlockKey {
    private static let tag = Data("app.catbridge.unlock.v1".utf8)

    static var exists: Bool { find(context: nil) != nil }

    private static func find(context: LAContext?) -> SecKey? {
        var q: [String: Any] = [kSecClass as String: kSecClassKey, kSecAttrApplicationTag as String: tag,
                                kSecAttrKeyType as String: kSecAttrKeyTypeECSECPrimeRandom, kSecReturnRef as String: true]
        if let context { q[kSecUseAuthenticationContext as String] = context }
        var item: CFTypeRef?
        guard SecItemCopyMatching(q as CFDictionary, &item) == errSecSuccess, let item else { return nil }
        return (item as! SecKey)
    }

    /// Makes a new Secure Enclave key and returns its public half (65 bytes, 04‖X‖Y).
    static func create() throws -> Data {
        delete()
        var err: Unmanaged<CFError>?
        guard let access = SecAccessControlCreateWithFlags(nil, kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly, [.privateKeyUsage, .biometryCurrentSet], &err) else {
            throw err!.takeRetainedValue() as Error
        }
        let attrs: [String: Any] = [
            kSecAttrKeyType as String: kSecAttrKeyTypeECSECPrimeRandom,
            kSecAttrKeySizeInBits as String: 256,
            kSecAttrTokenID as String: kSecAttrTokenIDSecureEnclave,
            kSecPrivateKeyAttrs as String: [
                kSecAttrIsPermanent as String: true,
                kSecAttrApplicationTag as String: tag,
                kSecAttrAccessControl as String: access,
            ] as [String: Any],
        ]
        guard let key = SecKeyCreateRandomKey(attrs as CFDictionary, &err) else { throw err!.takeRetainedValue() as Error }
        guard let pub = SecKeyCopyPublicKey(key), let data = SecKeyCopyExternalRepresentation(pub, nil) as Data? else {
            throw NSError(domain: "CatBridge", code: 1, userInfo: [NSLocalizedDescriptionKey: "Couldn't read the new key."])
        }
        return data
    }

    static func publicKey() -> Data? {
        guard let key = find(context: nil), let pub = SecKeyCopyPublicKey(key) else { return nil }
        return SecKeyCopyExternalRepresentation(pub, nil) as Data?
    }

    /// Asks for Face ID, then signs. Call off the main thread (it waits for Face ID).
    static func sign(_ message: Data, reason: String) throws -> Data {
        let ctx = LAContext()
        ctx.localizedReason = reason
        guard let key = find(context: ctx) else {
            throw NSError(domain: "CatBridge", code: 2, userInfo: [NSLocalizedDescriptionKey: "Unlock isn't set up on this iPhone. Settings → Unlock laptop with Face ID."])
        }
        var err: Unmanaged<CFError>?
        guard let sig = SecKeyCreateSignature(key, .ecdsaSignatureMessageX962SHA256, message as CFData, &err) as Data? else {
            let e = err!.takeRetainedValue() as Error
            if (e as NSError).code == errSecUserCanceled || (e as NSError).code == -128 {
                throw NSError(domain: "CatBridge", code: 3, userInfo: [NSLocalizedDescriptionKey: "Cancelled."])
            }
            throw e
        }
        return sig
    }

    static func delete() {
        let q: [String: Any] = [kSecClass as String: kSecClassKey, kSecAttrApplicationTag as String: tag, kSecAttrKeyType as String: kSecAttrKeyTypeECSECPrimeRandom]
        SecItemDelete(q as CFDictionary)
    }
}
