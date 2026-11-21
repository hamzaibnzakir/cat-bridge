using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace DynamicIsland;

/// <summary>Battery levels of Bluetooth devices (AirPods, headphones, mice, keyboards…) as Windows reports them.</summary>
public static class BluetoothBattery
{
    public sealed record Device(string Name, int Percent);

    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";   // DEVPKEY_Bluetooth_Battery
    const string BtClassic = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}", BtLe = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";

    /// Connected Bluetooth devices that report a battery level.
    public static async Task<List<Device>> Connected()
    {
        var result = new List<Device>();
        try
        {
            // which Bluetooth devices are connected right now
            var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var proto in new[] { BtClassic, BtLe })
            {
                var aeps = await DeviceInformation.FindAllAsync($"System.Devices.Aep.ProtocolId:=\"{proto}\" AND System.Devices.Aep.IsConnected:=System.StructuredQueryType.Boolean#True",
                    new[] { "System.Devices.Aep.IsConnected" }, DeviceInformationKind.AssociationEndpoint);
                foreach (var a in aeps) if (!string.IsNullOrWhiteSpace(a.Name)) connected.Add(a.Name.Trim());
            }
            if (connected.Count == 0) return result;

            // their battery levels live on the device nodes (several per headset: hands-free, stereo…)
            var nodes = await DeviceInformation.FindAllAsync("System.Devices.Present:=System.StructuredQueryType.Boolean#True", new[] { BatteryKey }, DeviceInformationKind.Device);
            foreach (var n in nodes)
            {
                if (!n.Properties.TryGetValue(BatteryKey, out var v) || v == null) continue;
                int pct = Convert.ToInt32(v);
                if (pct is < 0 or > 100) continue;
                var name = connected.FirstOrDefault(c => n.Name.Contains(c, StringComparison.OrdinalIgnoreCase) || c.Contains(n.Name, StringComparison.OrdinalIgnoreCase));
                if (name != null && !result.Any(r => r.Name == name)) result.Add(new Device(name, pct));
            }
        }
        catch (Exception ex) { App.Log(ex); }
        return result;
    }
}

/// <summary>Watches your Downloads folder for downloads in progress (Edge, Chrome, Firefox, Brave, Opera) and when they finish.</summary>
public sealed class DownloadWatcher : IDisposable
{
    public sealed class Active { public string Temp = "", Name = ""; public long Size, LastSize; public DateTime LastTime = DateTime.Now; public double Speed; }

    static readonly string[] TempExt = { ".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp" };
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    readonly FileSystemWatcher? _fsw;
    readonly Dictionary<string, Active> _active = new(StringComparer.OrdinalIgnoreCase);
    readonly object _lock = new();
    public event Action<string>? Finished; // final path

    public DownloadWatcher()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            _fsw = new FileSystemWatcher(Folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size, EnableRaisingEvents = true };
            _fsw.Created += (_, e) => Track(e.FullPath);
            _fsw.Renamed += (_, e) =>
            {
                if (IsTemp(e.OldFullPath) && !IsTemp(e.FullPath)) Done(e.OldFullPath, e.FullPath);
                else if (IsTemp(e.FullPath)) Track(e.FullPath);
            };
            _fsw.Deleted += (_, e) =>
            {
                if (!IsTemp(e.FullPath)) return;
                // Firefox: the .part goes away and the real file is already there
                var final = StripTemp(e.FullPath);
                Task.Delay(400).ContinueWith(_ => { if (File.Exists(final) && new FileInfo(final).Length > 0) Done(e.FullPath, final); else Forget(e.FullPath); });
            };
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static bool IsTemp(string p) => TempExt.Any(x => p.EndsWith(x, StringComparison.OrdinalIgnoreCase)) && !Path.GetFileName(p).StartsWith("~");
    static string StripTemp(string p) { foreach (var x in TempExt) if (p.EndsWith(x, StringComparison.OrdinalIgnoreCase)) return p[..^x.Length]; return p; }

    void Track(string temp)
    {
        if (!IsTemp(temp)) return;
        var name = Path.GetFileName(StripTemp(temp));
        if (name.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase)) name = "Download";
        lock (_lock) if (!_active.ContainsKey(temp)) _active[temp] = new Active { Temp = temp, Name = name };
    }

    void Forget(string temp) { lock (_lock) _active.Remove(temp); }

    void Done(string temp, string final)
    {
        bool known;
        lock (_lock) known = _active.Remove(temp);
        if (known || File.Exists(final)) Finished?.Invoke(final);
    }

    /// Refreshes sizes and speeds; returns what's still downloading.
    public List<Active> Poll()
    {
        lock (_lock)
        {
            foreach (var a in _active.Values.ToList())
            {
                try
                {
                    var fi = new FileInfo(a.Temp);
                    if (!fi.Exists) { if (DateTime.Now - a.LastTime > TimeSpan.FromSeconds(5)) _active.Remove(a.Temp); continue; }
                    var now = DateTime.Now; var dt = (now - a.LastTime).TotalSeconds;
                    a.Size = fi.Length;
                    if (dt >= 0.8) { a.Speed = Math.Max(0, (a.Size - a.LastSize) / dt) * 0.6 + a.Speed * 0.4; a.LastSize = a.Size; a.LastTime = now; }
                    if (DateTime.Now - fi.LastWriteTime > TimeSpan.FromMinutes(10)) _active.Remove(a.Temp); // stalled or abandoned
                }
                catch { }
            }
            return _active.Values.ToList();
        }
    }

    public void Dispose() => _fsw?.Dispose();

    public static string Size(double b) => b >= 1 << 30 ? $"{b / (1 << 30):0.0} GB" : b >= 1 << 20 ? $"{b / (1 << 20):0.0} MB" : $"{b / 1024:0} KB";
}
