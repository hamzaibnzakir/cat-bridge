using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>Which apps are using the camera / microphone right now (same registry Windows' own privacy indicator reads).</summary>
public sealed class PrivacyService
{
    public List<string> Camera { get; private set; } = new();
    public List<string> Mic { get; private set; } = new();

    /// Returns true for each capability that just went from idle to in-use.
    public (bool camStarted, bool micStarted) Poll()
    {
        bool camWas = Camera.Count > 0, micWas = Mic.Count > 0;
        Camera = InUse("webcam");
        Mic = InUse("microphone");
        return (!camWas && Camera.Count > 0, !micWas && Mic.Count > 0);
    }

    static List<string> InUse(string cap)
    {
        var list = new List<string>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{cap}");
            if (root == null) return list;
            foreach (var name in root.GetSubKeyNames())
            {
                if (name == "NonPackaged")
                {
                    using var np = root.OpenSubKey(name);
                    if (np == null) continue;
                    foreach (var sub in np.GetSubKeyNames())
                        if (Active(np, sub)) list.Add(Path.GetFileNameWithoutExtension(sub.Replace('#', '\\')));
                }
                else if (Active(root, name))
                {
                    var pkg = name.Split('_')[0];
                    list.Add(pkg[(pkg.LastIndexOf('.') + 1)..]);
                }
            }
        }
        catch { }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool Active(RegistryKey parent, string sub)
    {
        try
        {
            using var k = parent.OpenSubKey(sub);
            return k?.GetValue("LastUsedTimeStart") is long start && start > 0
                && k.GetValue("LastUsedTimeStop") is long stop && stop == 0;
        }
        catch { return false; }
    }
}
