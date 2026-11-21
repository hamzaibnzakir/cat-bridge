import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public enum BridgeError: LocalizedError, Equatable {
    case notPaired
    case unreachable(String)
    case server(Int, String)
    case badReply

    public var errorDescription: String? {
        switch self {
        case .notPaired: return "This iPhone isn't paired. Scan the code in Cat Center → iPhone again."
        case .unreachable(let why): return "Can't reach your laptop. Is Tailscale on, on both devices? (\(why))"
        case .server(_, let msg): return msg
        case .badReply: return "Your laptop sent something unexpected. Update Dynamic Island on the laptop."
        }
    }
}

/// Talks to Dynamic Island's Cat Bridge on your laptop, over Tailscale.
public final class BridgeClient: @unchecked Sendable {
    public let pairing: Pairing
    let session: URLSession

    public init(pairing: Pairing, timeout: TimeInterval = 8) {
        self.pairing = pairing
        let cfg = URLSessionConfiguration.ephemeral
        cfg.timeoutIntervalForRequest = timeout
        cfg.timeoutIntervalForResource = 60 * 30
        #if !canImport(FoundationNetworking)
        cfg.waitsForConnectivity = false
        #endif
        session = URLSession(configuration: cfg)
    }

    // ───────── API ─────────

    public func hello() async throws -> HelloInfo { try await get("/api/hello") }
    public func status() async throws -> LaptopStatus { try await get("/api/status") }
    public func shelf() async throws -> [ShelfEntry] { try await get("/api/shelf") }
    public func inbox() async throws -> [InboxItem] { try await get("/api/inbox") }
    public func alerts() async throws -> [AlertEntry] { try await get("/api/alerts") }

    public func laptopClipboard() async throws -> String {
        struct R: Decodable { var text: String }
        let r: R = try await get("/api/clipboard")
        return r.text
    }

    public func setLaptopClipboard(_ text: String) async throws { try await post("/api/clipboard", body: Data(text.utf8), type: "text/plain; charset=utf-8") }
    public func openLink(_ url: String) async throws { try await post("/api/link", body: Data(url.utf8), type: "text/plain; charset=utf-8") }
    public func media(_ action: MediaAction) async throws { try await post("/api/media?a=\(action.rawValue)") }
    public func focus(minutes: Int) async throws { try await post("/api/focus?m=\(minutes)") }
    public func stopFocus() async throws { try await post("/api/focus?m=0") }
    public func lock() async throws { try await post("/api/lock") }
    public func ping() async throws { try await post("/api/ping") }

    /// Shows a notification on the laptop's island (used by the "Send to Laptop" Shortcuts action).
    public func notify(app: String, title: String, subtitle: String = "", body: String = "") async throws {
        let json = try CatJSON.encoder.encode(["app": app, "title": title, "subtitle": subtitle, "body": body])
        try await post("/api/notify", body: json, type: "application/json")
    }

    /// Battery (0–100), charging, and Focus name ("" or "off" = no Focus). Pass nil to leave a value unchanged.
    public func phoneStatus(battery: Int?, charging: Bool?, focus: String?) async throws {
        var o: [String: String] = [:]
        if let battery { o["battery"] = String(battery) }
        if let charging { o["charging"] = charging ? "true" : "false" }
        if let focus { o["focus"] = focus }
        try await post("/api/phone", body: try CatJSON.encoder.encode(o), type: "application/json")
    }

    /// Sends a file to the laptop (it lands in Downloads\Cat Bridge and on the island's shelf).
    public func upload(fileAt url: URL, name: String? = nil) async throws {
        var req = try request("/api/files", method: "POST")
        req.setValue("application/octet-stream", forHTTPHeaderField: "Content-Type")
        req.setValue(Self.headerSafe(name ?? url.lastPathComponent), forHTTPHeaderField: "X-File-Name")
        let (data, resp) = try await run { done in self.session.uploadTask(with: req, fromFile: url, completionHandler: done) }
        try check(data, resp)
    }

    public func upload(data: Data, name: String) async throws {
        var req = try request("/api/files", method: "POST")
        req.setValue("application/octet-stream", forHTTPHeaderField: "Content-Type")
        req.setValue(Self.headerSafe(name), forHTTPHeaderField: "X-File-Name")
        let (d, resp) = try await run { done in self.session.uploadTask(with: req, from: data, completionHandler: done) }
        try check(d, resp)
    }

    /// Downloads a shelf file into a temporary folder and returns where it is.
    public func download(_ entry: ShelfEntry) async throws -> URL {
        let req = try request("/api/shelf/file?i=\(entry.index)", method: "GET")
        let (data, resp) = try await run { done in self.session.dataTask(with: req, completionHandler: done) }
        try check(data, resp)
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("CatBridge", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let dest = dir.appendingPathComponent(entry.name.replacingOccurrences(of: "/", with: "_"))
        try? FileManager.default.removeItem(at: dest)
        try data.write(to: dest)
        return dest
    }

    // ───────── unlock with Face ID, wake, find ─────────

    public func unlockChallenge() async throws -> UnlockChallenge { try await get("/api/unlock/challenge") }

    public func unlockApprove(nonce: String, signature: Data) async throws {
        let body = try CatJSON.encoder.encode(["nonce": nonce, "signature": signature.base64EncodedString()])
        try await post("/api/unlock/approve", body: body, type: "application/json")
    }

    /// Asks the laptop to trust this iPhone's Face ID key (you then press Allow on the laptop).
    public func unlockRegister(name: String, publicKey: Data) async throws -> UnlockRegistration {
        let body = try CatJSON.encoder.encode(["name": name, "publicKey": publicKey.base64EncodedString()])
        var req = try request("/api/unlock/register", method: "POST")
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let (data, resp) = try await run { done in self.session.uploadTask(with: req, from: body, completionHandler: done) }
        try check(data, resp)
        do { return try CatJSON.decoder.decode(UnlockRegistration.self, from: data) } catch { throw BridgeError.badReply }
    }

    public func unlockRegistration(publicKey: Data) async throws -> UnlockRegistration {
        let k = publicKey.base64EncodedString().addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? ""
        return try await get("/api/unlock/register?key=\(k)")
    }

    public func wake() async throws { try await post("/api/wake") }
    public func netInfo() async throws -> NetInfo { try await get("/api/netinfo") }
    public func focusStats() async throws -> FocusStats { try await get("/api/focus/stats") }
    public func locate() async throws -> LaptopLocation { try await get("/api/locate") }
    public func alarm(_ on: Bool) async throws { try await post("/api/alarm?on=\(on ? 1 : 0)") }
    public func lostMode(_ on: Bool, message: String?) async throws {
        var o = ["on": on ? "true" : "false"]
        if let message { o["message"] = message }
        try await post("/api/lost", body: try CatJSON.encoder.encode(o), type: "application/json")
    }

    /// ws://… URL for the live trackpad/keyboard channel.
    public var inputURL: URL? {
        guard let base = pairing.baseURL, var c = URLComponents(url: base, resolvingAgainstBaseURL: false) else { return nil }
        c.scheme = "ws"
        c.path = "/api/input"
        c.queryItems = [URLQueryItem(name: "k", value: pairing.key)]
        return c.url
    }

    public enum MediaAction: String, Sendable { case toggle, next, prev }

    // ───────── plumbing ─────────

    /// Header values must be ASCII: percent-encode names like "Photo café.jpg" (the laptop decodes them).
    static func headerSafe(_ s: String) -> String {
        s.addingPercentEncoding(withAllowedCharacters: .alphanumerics.union(CharacterSet(charactersIn: "-_.~ ()"))) ?? "file"
    }

    func request(_ path: String, method: String) throws -> URLRequest {
        guard let base = pairing.baseURL, let url = URL(string: path, relativeTo: base) else { throw BridgeError.notPaired }
        var r = URLRequest(url: url)
        r.httpMethod = method
        r.setValue(pairing.key, forHTTPHeaderField: "X-Cat-Key")
        r.setValue("CatBridge-iOS", forHTTPHeaderField: "User-Agent")
        return r
    }

    func get<T: Decodable>(_ path: String) async throws -> T {
        let req = try request(path, method: "GET")
        let (data, resp) = try await run { done in self.session.dataTask(with: req, completionHandler: done) }
        try check(data, resp)
        do { return try CatJSON.decoder.decode(T.self, from: data) } catch { throw BridgeError.badReply }
    }

    func post(_ path: String, body: Data? = nil, type: String? = nil) async throws {
        var req = try request(path, method: "POST")
        if let type { req.setValue(type, forHTTPHeaderField: "Content-Type") }
        let payload = body ?? Data()
        let (data, resp) = try await run { done in self.session.uploadTask(with: req, from: payload, completionHandler: done) }
        try check(data, resp)
    }

    func check(_ data: Data, _ resp: URLResponse) throws {
        guard let http = resp as? HTTPURLResponse else { throw BridgeError.badReply }
        if (200..<300).contains(http.statusCode) { return }
        if http.statusCode == 401 { throw BridgeError.notPaired }
        let msg = (try? CatJSON.decoder.decode(ErrorReply.self, from: data))?.error ?? "Your laptop said \(http.statusCode)."
        throw BridgeError.server(http.statusCode, msg)
    }

    /// Completion-handler tasks wrapped as async (works on iOS and on Linux, where tests run).
    func run(_ make: @escaping (@escaping @Sendable (Data?, URLResponse?, Error?) -> Void) -> URLSessionTask) async throws -> (Data, URLResponse) {
        try await withCheckedThrowingContinuation { (cont: CheckedContinuation<(Data, URLResponse), Error>) in
            let task = make { data, resp, err in
                if let err {
                    cont.resume(throwing: BridgeError.unreachable((err as NSError).localizedDescription))
                } else if let resp {
                    cont.resume(returning: (data ?? Data(), resp))
                } else {
                    cont.resume(throwing: BridgeError.badReply)
                }
            }
            task.resume()
        }
    }
}
