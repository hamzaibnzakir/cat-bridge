using System;
using Microsoft.Win32;

namespace DynamicIsland;

static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "DynamicIsland";

    public static bool IsEnabled
    {
        get { try { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue(Name) != null; } catch { return false; } }
    }

    /// After an update or a move, point "start with Windows" at this copy of the app.
    public static void Repair()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, true);
            if (k?.GetValue(Name) is string v && !v.Contains(Environment.ProcessPath ?? "\u0000", StringComparison.OrdinalIgnoreCase))
                k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        }
        catch { }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue(Name, false);
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
