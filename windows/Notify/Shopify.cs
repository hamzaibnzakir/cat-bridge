using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland.Notify;

/// <summary>Spots Shopify order notifications (from your iPhone or Windows) and keeps today's sales tally.</summary>
public static class Shopify
{
    public sealed record Order(string? Number, decimal? Amount, string? Currency, string? Customer, string? Store);

    public static bool IsShopify(string app, string title, string body)
        => app.Contains("Shopify", StringComparison.OrdinalIgnoreCase)
        || title.Contains("Shopify", StringComparison.OrdinalIgnoreCase)
        || (Regex.IsMatch(title + " " + body, @"\b(new )?order\b", RegexOptions.IgnoreCase) && Money(title + " " + body).amount != null
            && app.Contains("shop", StringComparison.OrdinalIgnoreCase));

    static readonly (string sym, string code)[] Symbols =
    {
        ("US$", "USD"), ("CA$", "CAD"), ("A$", "AUD"), ("NZ$", "NZD"), ("HK$", "HKD"), ("S$", "SGD"), ("R$", "BRL"),
        ("$", "USD"), ("€", "EUR"), ("£", "GBP"), ("₦", "NGN"), ("¥", "JPY"), ("₹", "INR"), ("₱", "PHP"), ("₩", "KRW"),
        ("Rp", "IDR"), ("R ", "ZAR"), ("KSh", "KES"), ("GH₵", "GHS"), ("₵", "GHS"), ("CHF", "CHF"), ("AED", "AED"),
    };

    /// Finds the first money amount, e.g. "$1,249.50", "₦45,000", "Rp 350.000", "120.00 USD".
    public static (decimal? amount, string? currency) Money(string text)
    {
        // code after number: 120.00 USD
        var m = Regex.Match(text, @"(?<n>\d{1,3}(?:[.,\s]\d{3})*(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)\s?(?<c>[A-Z]{3})\b");
        var m2 = Regex.Match(text, @"(?<c>US\$|CA\$|A\$|NZ\$|HK\$|S\$|R\$|GH₵|KSh|Rp|[$€£₦¥₹₱₩₵]|\b[A-Z]{3}\b)\s?(?<n>\d{1,3}(?:[.,\s]\d{3})*(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)");
        Match? pick = null; string? cur = null;
        if (m2.Success && (!m.Success || m2.Index <= m.Index))
        {
            pick = m2; var c = m2.Groups["c"].Value;
            cur = Symbols.FirstOrDefault(s => s.sym.Trim() == c).code ?? (c.Length == 3 && c.All(char.IsUpper) ? c : null);
            if (cur == null) pick = null;
        }
        if (pick == null && m.Success && IsCurrencyCode(m.Groups["c"].Value)) { pick = m; cur = m.Groups["c"].Value; }
        if (pick == null)
        {
            // symbol after number: 120,00 €
            var m3 = Regex.Match(text, @"(?<n>\d{1,3}(?:[.,\s]\d{3})*(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)\s?(?<c>[€£₦¥₹₱₩₵])");
            if (m3.Success) { pick = m3; cur = Symbols.First(x => x.sym == m3.Groups["c"].Value).code; }
        }
        if (pick == null) return (null, null);
        var amount = ParseNumber(pick.Groups["n"].Value);
        // a trailing ISO code after a symbol wins: "$45.00 CAD"
        var after = text[(pick.Index + pick.Length)..];
        var code = Regex.Match(after, @"^\s?([A-Z]{3})\b");
        if (code.Success && IsCurrencyCode(code.Groups[1].Value)) cur = code.Groups[1].Value;
        return (amount, cur);
    }

    static bool IsCurrencyCode(string c) => c is "USD" or "EUR" or "GBP" or "NGN" or "CAD" or "AUD" or "NZD" or "JPY" or "INR" or "IDR" or "ZAR"
        or "KES" or "GHS" or "PHP" or "KRW" or "BRL" or "SGD" or "HKD" or "CHF" or "AED" or "MXN" or "SEK" or "NOK" or "DKK" or "PLN" or "TRY" or "EGP" or "MAD" or "XOF" or "XAF";

    public static decimal? ParseNumber(string n)
    {
        n = n.Replace(" ", "").Replace(" ", "");
        int lastDot = n.LastIndexOf('.'), lastComma = n.LastIndexOf(',');
        int sep = Math.Max(lastDot, lastComma);
        // decimal separator only if followed by 1–2 digits
        if (sep >= 0 && n.Length - sep - 1 is 1 or 2)
        {
            var whole = new string(n[..sep].Where(char.IsDigit).ToArray());
            var frac = n[(sep + 1)..];
            n = (whole.Length == 0 ? "0" : whole) + "." + frac;
        }
        else n = new string(n.Where(char.IsDigit).ToArray());
        return decimal.TryParse(n, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static Order Parse(string app, string title, string body)
    {
        var all = title + " \n " + body;
        var num = Regex.Match(all, @"#\s?(\d{3,})");
        var (amt, cur) = Money(all);
        var cust = Regex.Match(all, @"\b(?:from|by)\s+(?<n>[\p{L}][\p{L}'\-]+(?:\s[\p{L}][\p{L}'\-]+)?)", RegexOptions.IgnoreCase);
        string? store = title.Contains("order", StringComparison.OrdinalIgnoreCase) || title.Contains('#') ? null : title.Trim();
        if (store != null && (store.Equals("Shopify", StringComparison.OrdinalIgnoreCase) || store.Length > 40)) store = null;
        return new Order(num.Success ? "#" + num.Groups[1].Value : null, amt, cur, cust.Success ? cust.Groups["n"].Value : null, store);
    }

    public static bool LooksLikeOrder(string title, string body)
        => Regex.IsMatch(title + " " + body, @"\border\b|#\d{3,}|\bsale\b|\bpurchase", RegexOptions.IgnoreCase);

    public static string Format(decimal amount, string? currency)
    {
        var sym = currency switch { "USD" => "$", "EUR" => "€", "GBP" => "£", "NGN" => "₦", "JPY" => "¥", "INR" => "₹", "IDR" => "Rp ", "CAD" => "CA$", "AUD" => "A$", _ => currency == null ? "" : currency + " " };
        var digits = amount == Math.Floor(amount) || currency is "NGN" or "IDR" or "JPY" or "KRW" ? "N0" : "N2";
        return sym + amount.ToString(digits, CultureInfo.InvariantCulture);
    }
}

/// <summary>Today's Shopify orders, kept in %AppData%\DynamicIsland\shopify-today.json and reset at midnight.</summary>
public sealed class SalesTally
{
    public sealed class Day
    {
        public string Date { get; set; } = "";
        public int Orders { get; set; }
        public Dictionary<string, decimal> Totals { get; set; } = new();
        public List<string> Seen { get; set; } = new(); // order numbers already counted
    }

    static string FilePath => Path.Combine(AppSettings.Dir, "shopify-today.json");
    Day _d = new();
    public event Action? Changed;

    public SalesTally()
    {
        try { if (File.Exists(FilePath)) _d = JsonSerializer.Deserialize<Day>(File.ReadAllText(FilePath)) ?? new(); } catch { }
        Roll();
    }

    void Roll()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (_d.Date != today) _d = new Day { Date = today };
    }

    public int Orders { get { Roll(); return _d.Orders; } }

    /// "4 orders · $312.50" (or "No orders yet").
    public string Summary
    {
        get
        {
            Roll();
            if (_d.Orders == 0) return "No orders yet today";
            var money = string.Join(" + ", _d.Totals.OrderByDescending(kv => kv.Value).Select(kv => Shopify.Format(kv.Value, kv.Key == "?" ? null : kv.Key)));
            return $"{_d.Orders} order{(_d.Orders == 1 ? "" : "s")}" + (money.Length > 0 ? " · " + money : "");
        }
    }

    public IReadOnlyDictionary<string, decimal> Totals { get { Roll(); return _d.Totals; } }

    /// Returns false if this order number was already counted today (Phone Link + Cat Bridge both delivering it).
    public bool Add(Shopify.Order o)
    {
        Roll();
        if (o.Number != null)
        {
            if (_d.Seen.Contains(o.Number)) return false;
            _d.Seen.Add(o.Number);
            if (_d.Seen.Count > 500) _d.Seen.RemoveAt(0);
        }
        _d.Orders++;
        if (o.Amount is decimal a) _d.Totals[o.Currency ?? "?"] = (_d.Totals.TryGetValue(o.Currency ?? "?", out var t) ? t : 0) + a;
        Save();
        Changed?.Invoke();
        return true;
    }

    public void Reset() { _d = new Day { Date = DateTime.Now.ToString("yyyy-MM-dd") }; Save(); Changed?.Invoke(); }

    void Save()
    {
        try { Directory.CreateDirectory(AppSettings.Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(_d)); } catch (Exception ex) { App.Log(ex); }
    }
}
