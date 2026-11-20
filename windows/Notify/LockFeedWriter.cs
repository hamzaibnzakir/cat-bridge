using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DynamicIsland.Notify;

/// <summary>
/// Writes the one-line summary the Cat Face sign-in tile shows ("Shopify: 4 orders · $312 today · 3 new notifications").
/// Counts reset when you unlock. "details" also shows the latest senders; "off" clears it.
/// </summary>
public static class LockFeedWriter
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DynamicIsland", "LockScreen", "feed.txt");

    static int _sinceUnlock;
    static readonly List<string> _recent = new();

    public static void Unlocked() { _sinceUnlock = 0; _recent.Clear(); }

    public static void Noted(string app, string title)
    {
        _sinceUnlock++;
        _recent.Insert(0, string.IsNullOrWhiteSpace(app) ? title : app);
        if (_recent.Count > 6) _recent.RemoveAt(_recent.Count - 1);
    }

    public static string Compose(AppSettings s, SalesTally sales)
    {
        if (s.LockScreenFeed == "off") return "";
        var parts = new List<string>();
        if (sales.Orders > 0) parts.Add("Shopify: " + sales.Summary + " today");
        if (_sinceUnlock > 0)
        {
            var n = $"{_sinceUnlock} new notification{(_sinceUnlock == 1 ? "" : "s")}";
            if (s.LockScreenFeed == "details")
            {
                var who = _recent.Distinct().Take(3).ToList();
                if (who.Count > 0) n += " (" + string.Join(", ", who) + ")";
            }
            parts.Add(n);
        }
        return string.Join("  ·  ", parts);
    }

    public static void Write(AppSettings s, SalesTally sales)
    {
        try
        {
            var text = Compose(s, sales);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && File.ReadAllText(FilePath, Encoding.UTF8) == text) { File.SetLastWriteTimeUtc(FilePath, DateTime.UtcNow); return; }
            File.WriteAllText(FilePath, text, new UTF8Encoding(false));
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
