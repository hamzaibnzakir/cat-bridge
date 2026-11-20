import XCTest
@testable import CatBridgeCore

final class CoreTests: XCTestCase {
    func testPairingLink() throws {
        let p = try XCTUnwrap(Pairing.parse("catbridge://pair?host=100.101.102.103&port=47800&key=abc123abc123abc123abc123&name=DESKTOP-24F6UUC"))
        XCTAssertEqual(p.host, "100.101.102.103")
        XCTAssertEqual(p.port, 47800)
        XCTAssertEqual(p.key, "abc123abc123abc123abc123")
        XCTAssertEqual(p.name, "DESKTOP-24F6UUC")
        XCTAssertEqual(p.baseURL?.absoluteString, "http://100.101.102.103:47800")
    }

    func testPairingDefaultsAndRejects() {
        XCTAssertEqual(Pairing.parse("catbridge://pair?host=laptop.tail1234.ts.net&key=abc123abc123abc123abc123")?.port, 47800)
        XCTAssertNil(Pairing.parse("catbridge://pair?host=1.2.3.4&key=short"))
        XCTAssertNil(Pairing.parse("catbridge://pair?key=abc123abc123abc123abc123"))
        XCTAssertNil(Pairing.parse("https://example.com/"))
        XCTAssertNil(Pairing.parse("hello"))
    }

    func testIPv6Host() {
        let p = Pairing(host: "fd7a:115c:a1e0::1", port: 47800, key: "abc123abc123abc123abc123")
        XCTAssertEqual(p.baseURL?.absoluteString, "http://[fd7a:115c:a1e0::1]:47800")
    }

    func testLegacyWebLink() {
        let p = Pairing.parse("https://desktop.tail1234.ts.net/#k=abc123abc123abc123abc123")
        XCTAssertEqual(p?.host, "desktop.tail1234.ts.net")
        XCTAssertEqual(p?.key, "abc123abc123abc123abc123")
    }

    func testDotNetDates() throws {
        let d = try XCTUnwrap(CatJSON.parseDate("2026-11-20T11:14:00.0882953+01:00"))
        XCTAssertEqual(d.timeIntervalSince1970, 1_795_169_640.088, accuracy: 0.002)
        XCTAssertNotNil(CatJSON.parseDate("2026-11-20T10:14:00Z"))
        XCTAssertNotNil(CatJSON.parseDate("2026-11-20T10:14:00.5Z"))
        XCTAssertNotNil(CatJSON.parseDate("2026-11-20T10:14:00"))
    }

    func testDecodeAlerts() throws {
        let json = #"[{"app":"Shopify","title":"Queen Luxury Hair","body":"Order #1032 for $245.50","time":"2026-11-20T11:14:00.0882953+01:00","fromPhone":true}]"#
        let a = try CatJSON.decoder.decode([AlertEntry].self, from: Data(json.utf8))
        XCTAssertEqual(a.first?.app, "Shopify")
        XCTAssertTrue(a.first?.fromPhone ?? false)
    }

    func testFormatting() {
        XCTAssertEqual(Fmt.money(412, "USD"), "$412")
        XCTAssertEqual(Fmt.money(45000, "NGN"), "₦45,000")
        XCTAssertEqual(Fmt.money(245.5, "USD"), "$245.50")
        XCTAssertEqual(Fmt.size(1536), "1.5 KB")
        XCTAssertEqual(Fmt.timer(1490), "24:50")
        XCTAssertEqual(BridgeClient.headerSafe("Photo café.jpg"), "Photo caf%C3%A9.jpg")
    }
}

/// Runs against a real Dynamic Island bridge when CATBRIDGE_LIVE is set, e.g.
/// CATBRIDGE_LIVE="catbridge://pair?host=127.0.0.1&port=47801&key=…" swift test
final class LiveBridgeTests: XCTestCase {
    var client: BridgeClient!

    override func setUpWithError() throws {
        guard let link = ProcessInfo.processInfo.environment["CATBRIDGE_LIVE"], let p = Pairing.parse(link) else {
            throw XCTSkip("Set CATBRIDGE_LIVE to a pairing link to run live tests.")
        }
        client = BridgeClient(pairing: p)
    }

    func testHelloStatusAndLists() async throws {
        let h = try await client.hello()
        XCTAssertFalse(h.device.isEmpty)
        let s = try await client.status()
        XCTAssertFalse(s.device.isEmpty)
        _ = try await client.shelf()
        _ = try await client.inbox()
        let alerts = try await client.alerts()
        XCTAssertFalse(alerts.isEmpty)
        let sales = try await client.sales()
        XCTAssertGreaterThanOrEqual(sales.orders, 0)
    }

    func testActions() async throws {
        try await client.setLaptopClipboard("from swift ✓")
        let clip = try await client.laptopClipboard()
        XCTAssertEqual(clip, "from swift ✓")
        try await client.notify(app: "Shopify", title: "Queen Luxury Hair", subtitle: "", body: "Order #2001 for $10.00")
        try await client.phoneStatus(battery: 54, charging: true, focus: "Work")
        try await client.media(.toggle)
        try await client.focus(minutes: 25)
        try await client.stopFocus()
        try await client.ping()
        try await client.openLink("https://example.com/swift")
    }

    func testUploadAndDownload() async throws {
        let payload = Data((0..<200_000).map { UInt8($0 % 251) })
        try await client.upload(data: payload, name: "swift test é.bin")
        let shelf = try await client.shelf()
        let entry = try XCTUnwrap(shelf.first { $0.name.hasPrefix("swift test") })
        let url = try await client.download(entry)
        XCTAssertEqual(try Data(contentsOf: url), payload)
    }

    func testBadKey() async throws {
        let bad = BridgeClient(pairing: Pairing(host: client.pairing.host, port: client.pairing.port, key: "wrongwrongwrongwrongwrong"))
        do { _ = try await bad.status(); XCTFail("should fail") } catch let e as BridgeError { XCTAssertEqual(e, .notPaired) }
    }

    func testUnreachable() async throws {
        let off = BridgeClient(pairing: Pairing(host: "127.0.0.1", port: 1, key: "abc123abc123abc123abc123"), timeout: 2)
        do { _ = try await off.status(); XCTFail("should fail") } catch let e as BridgeError {
            if case .unreachable = e {} else { XCTFail("expected unreachable, got \(e)") }
        }
    }
}
