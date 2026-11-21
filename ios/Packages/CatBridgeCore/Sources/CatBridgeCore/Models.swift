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

/// GET /api/unlock/challenge
public struct UnlockChallenge: Codable, Equatable, Sendable {
    public var nonce: String
    public var machine: String
    /// The exact bytes the iPhone signs (must match the laptop's PhoneTrust.Message).
    public var message: Data { Data("catbridge-unlock|v1|\(machine.uppercased())|\(nonce)".utf8) }
}

public struct UnlockRegistration: Codable, Equatable, Sendable {
    public var state: String      // none | pending | trusted | denied
    public var code: String?
}

/// GET /api/netinfo: what the iPhone needs to send a Wake-on-LAN packet.
public struct NetInfo: Codable, Equatable, Sendable {
    public struct Adapter: Codable, Equatable, Sendable {
        public var name: String
        public var wifi: Bool
        public var up: Bool
        public var mac: String
        public var ips: [String]
        public var broadcasts: [String]
    }
    public var machine: String
    public var adapters: [Adapter]
}

/// GET /api/focus/stats
public struct FocusStats: Codable, Equatable, Sendable {
    public var todayMinutes: Double
    public var todaySessions: Int
    public var week: [Double]
    public var weekLabels: [String]
    public var weekMinutes: Double
    public var streak: Int
    public var completed: Int
    public var active: Bool
    public var secondsLeft: Int
    public var held: Int
}

/// GET /api/locate
public struct LaptopLocation: Codable, Equatable, Sendable {
    public var latitude: Double?
    public var longitude: Double?
    public var accuracy: Double?
    public var source: String?
    public var problem: String?
    public var publicIp: String?
    public var wifi: String?
    public var battery: Int?
    public var charging: Bool?
    public var locked: Bool?
    public var lost: Bool?
    public var time: Date
}

/// The laptop's error replies look like {"error": "…"}.
struct ErrorReply: Decodable { var error: String }
