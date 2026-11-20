using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

/// <summary>Files parked on the island (references only — nothing is copied). Persists across restarts.</summary>
public sealed class ShelfStore
{
    static string FilePath => Path.Combine(AppSettings.Dir, "shelf.json");
    public List<string> Paths { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Paths = (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new())
                    .Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        }
        catch { Paths = new(); }
    }

    public void Add(IEnumerable<string> paths)
    {
        foreach (var p in paths) { Paths.Remove(p); Paths.Insert(0, p); }
        if (Paths.Count > 30) Paths = Paths.Take(30).ToList();
        Save();
    }

    public void Remove(string p) { Paths.Remove(p); Save(); }
    public void Clear() { Paths.Clear(); Save(); }
    public void Prune() { int n = Paths.Count; Paths = Paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList(); if (n != Paths.Count) Save(); }

    void Save()
    {
        try { Directory.CreateDirectory(AppSettings.Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(Paths)); } catch { }
    }

    public static ImageSource? IconFor(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (ico == null) return null;
            var bs = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            bs.Freeze();
            return bs;
        }
        catch { return null; }
    }
}

/// <summary>Last 10 text clips, memory only (never written to disk). Skips content password managers mark as private.</summary>
public sealed class ClipboardHistory
{
    public List<string> Items { get; } = new();

    public bool Capture()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return false;
                if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore")) return false;
                if (data.GetDataPresent("CanIncludeInClipboardHistory") && data.GetData("CanIncludeInClipboardHistory") is MemoryStream ms)
                {
                    var b = ms.ToArray();
                    if (b.Length >= 4 && BitConverter.ToInt32(b, 0) == 0) return false;
                }
                if (!data.GetDataPresent(DataFormats.UnicodeText)) return false;
                var text = data.GetData(DataFormats.UnicodeText) as string;
                if (string.IsNullOrWhiteSpace(text)) return false;
                Items.Remove(text);
                Items.Insert(0, text);
                if (Items.Count > 10) Items.RemoveAt(10);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(40); } // clipboard busy
            catch { return false; }
        }
        return false;
    }
}
