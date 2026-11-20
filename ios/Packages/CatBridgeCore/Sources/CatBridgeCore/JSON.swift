import Foundation

/// Decoding that understands the laptop's .NET timestamps ("2026-11-20T11:14:00.0882953+01:00").
public enum CatJSON {
    public static let decoder: JSONDecoder = {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .custom { dec in
            let c = try dec.singleValueContainer()
            let s = try c.decode(String.self)
            if let date = parseDate(s) { return date }
            throw DecodingError.dataCorruptedError(in: c, debugDescription: "Unrecognised date: \(s)")
        }
        return d
    }()

    public static let encoder = JSONEncoder()

    /// Accepts any number of fractional-second digits and "Z" or ±hh:mm offsets.
    public static func parseDate(_ raw: String) -> Date? {
        var s = raw.trimmingCharacters(in: .whitespaces)
        // normalise the fraction to exactly 3 digits
        if let dot = s.firstIndex(of: ".") {
            var end = s.index(after: dot)
            while end < s.endIndex, s[end].isNumber { end = s.index(after: end) }
            var digits = String(s[s.index(after: dot)..<end])
            if digits.count > 3 { digits = String(digits.prefix(3)) }
            while digits.count < 3 { digits += "0" }
            s = String(s[..<dot]) + "." + digits + String(s[end...])
        }
        // a timestamp without a zone is local time
        let hasZone = s.hasSuffix("Z") || s.range(of: #"[+-]\d{2}:?\d{2}$"#, options: .regularExpression) != nil
        let f = ISO8601DateFormatter()
        f.formatOptions = s.contains(".") ? [.withInternetDateTime, .withFractionalSeconds] : [.withInternetDateTime]
        if !hasZone {
            f.timeZone = .current
            f.formatOptions.remove(.withTimeZone)
        }
        return f.date(from: s)
    }
}
