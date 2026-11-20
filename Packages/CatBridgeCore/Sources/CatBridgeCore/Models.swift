import Foundation

/// What the laptop reports (GET /api/status).
public struct LaptopStatus: Codable, Equatable, Sendable {
    public var device: String
    public var battery: Int
    public var charging: Bool
    public var hasBattery: Bool
    public var title: String?
    public var artist: String?
    public var playing: Bool
    public var timerSecondsLeft: Int
    public var shelfCount: Int

    public init(device: String, battery: Int, charging: Bool, hasBattery: Bool, title: String?, artist: String?, playing: Bool, timerSecondsLeft: Int, shelfCount: Int) {
        self.device = device; self.battery = battery; self.charging = charging; self.hasBattery = hasBattery
        self.title = title; self.artist = artist; self.playing = playing; self.timerSecondsLeft = timerSecondsLeft; self.shelfCount = shelfCount
    }
}

/// GET /api/hello
public struct HelloInfo: Codable, Equatable, Sendable {
    public var device: String
    public var version: String
    public var tailscaleIp: String?
    public var port: Int
    public var features: [String]
}

/// A file on the laptop's shelf (GET /api/shelf).
public struct ShelfEntry: Codable, Equatable, Identifiable, Sendable {
    public var index: Int
    public var name: String
    public var size: Int64
    public var isFolder: Bool
    public var id: Int { index }
}

/// Something the laptop sent to this iPhone (GET /api/inbox).
public struct InboxItem: Codable, Equatable, Identifiable, Sendable {
    public var id: String
    public var kind: String
    public var title: String
    public var text: String
    public var url: String?
    public var time: Date
}

/// A notification the laptop received (GET /api/alerts).
public struct AlertEntry: Codable, Equatable, Identifiable, Sendable {
    public var app: String
    public var title: String
    public var body: String
    public var time: Date
    public var fromPhone: Bool
    public var id: String { "\(time.timeIntervalSince1970)|\(app)|\(title)" }
}

/// Today's Shopify orders seen by the laptop (GET /api/sales).
public struct SalesInfo: Codable, Equatable, Sendable {
    public var orders: Int
    public var summary: String
    public var totals: [String: Double]
}

/// The laptop's error replies look like {"error": "…"}.
struct ErrorReply: Decodable { var error: String }
