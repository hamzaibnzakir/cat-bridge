import Foundation

public enum Fmt {
    public static func ago(_ date: Date, now: Date = Date()) -> String {
        let s = now.timeIntervalSince(date)
        if s < 60 { return "now" }
        if s < 3600 { return "\(Int(s / 60))m" }
        if s < 86400 { return "\(Int(s / 3600))h" }
        let f = DateFormatter(); f.dateFormat = "d MMM"
        return f.string(from: date)
    }

    public static func size(_ bytes: Int64) -> String {
        let units = ["B", "KB", "MB", "GB"]
        var v = Double(bytes), i = 0
        while v >= 1024, i < units.count - 1 { v /= 1024; i += 1 }
        return i == 0 ? "\(bytes) B" : String(format: v < 10 ? "%.1f %@" : "%.0f %@", v, units[i])
    }

    public static func timer(_ seconds: Int) -> String {
        String(format: "%d:%02d", seconds / 60, seconds % 60)
    }

    /// "$412" / "₦45,000" for a currency code from the laptop's sales totals.
    public static func money(_ amount: Double, _ code: String) -> String {
        let symbols = ["USD": "$", "EUR": "€", "GBP": "£", "NGN": "₦", "JPY": "¥", "INR": "₹", "IDR": "Rp ", "CAD": "CA$", "AUD": "A$"]
        let whole = amount.rounded() == amount || ["NGN", "IDR", "JPY", "KRW"].contains(code)
        let nf = NumberFormatter()
        nf.numberStyle = .decimal
        nf.locale = Locale(identifier: "en_US")
        nf.minimumFractionDigits = whole ? 0 : 2
        nf.maximumFractionDigits = whole ? 0 : 2
        let n = nf.string(from: NSNumber(value: amount)) ?? "\(amount)"
        if code == "?" { return n }
        return (symbols[code] ?? code + " ") + n
    }
}
