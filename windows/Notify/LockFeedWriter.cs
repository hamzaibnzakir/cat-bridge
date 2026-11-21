using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DynamicIsland.Notify;

/// <summary>
/// Writes the one-line summary the Cat Face sign-in tile shows ("3 new notifications (WhatsApp, Mail)").
/// Counts reset when you unlock. "details" also shows the latest senders; "off" clears it.
/// </summary>
public static class LockFeedWriter
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DynamicIsland", "LockScreen", "feed.txt");

    static int _sinceUnlock;
    /// Set by lost mode: shown on the sign-in tile instead of the summary.
    public static string? LostMessage;
    static readonly List<string> _recent = new();

    public static void Unlocked() { _sinceUnlock = 0; _recent.Clear(); }

    public static void Noted(string app, string title)
    {
        _sinceUnlock++;
        _recent.Insert(0, string.IsNullOrWhiteSpace(app) ? title : app);
        if (_recent.Count > 6) _recent.RemoveAt(_recent.Count - 1);
    }

    public static string Compose(AppSettings s)
    {
        if (s.LockScreenFeed == "off") return "";
        var parts = new List<string>();
        if (LostMessage is { Length: > 0 } lost) return lost; // lost mode wins
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

    public static void Write(AppSettings s)
    {
        try
        {
            var text = Compose(s);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && File.ReadAllText(FilePath, Encoding.UTF8) == text) { File.SetLastWriteTimeUtc(FilePath, DateTime.UtcNow); return; }
            File.WriteAllText(FilePath, text, new UTF8Encoding(false));
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
