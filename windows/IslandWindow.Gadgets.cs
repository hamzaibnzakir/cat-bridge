using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>AirPods / Bluetooth battery and download progress in the pill.</summary>
public partial class IslandWindow
{
    DownloadWatcher? _downloads;
    readonly DispatcherTimer _gadgetTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    int _gadgetTicks;
    public List<BluetoothBattery.Device> BluetoothDevices { get; private set; } = new();
    readonly Dictionary<string, int> _btLowWarned = new();

    void WireGadgets()
    {
        if (_s.ShowDownloads)
        {
            _downloads = new DownloadWatcher();
            _downloads.Finished += path => Dispatcher.InvokeAsync(() => DownloadFinished(path));
        }
        _gadgetTimer.Tick += async (_, _) =>
        {
            _gadgetTicks++;
            if (_downloads != null) ShowDownloadProgress(_downloads.Poll());
            if (_s.ShowBluetoothBattery && (_gadgetTicks == 3 || _gadgetTicks % 45 == 0)) await RefreshBluetooth();
        };
        _gadgetTimer.Start();
    }

    // ───────── Bluetooth ─────────

    async System.Threading.Tasks.Task RefreshBluetooth()
    {
        var now = await BluetoothBattery.Connected();
        var before = BluetoothDevices;
        BluetoothDevices = now;
        foreach (var d in now)
        {
            if (!before.Any(b => b.Name == d.Name) && _gadgetTicks > 3)
                ShowToast(new ToastSpec(d.Name, "Connected", null, "", null, d.Percent / 100.0, $"{d.Percent}%", 4, "bt-" + d.Name));
            if (d.Percent <= 15 && (!_btLowWarned.TryGetValue(d.Name, out var w) || w > 15))
                ShowToast(new ToastSpec($"{d.Name} battery low", $"{d.Percent}% left", null, "", Yellow, d.Percent / 100.0, $"{d.Percent}%", 6, "bt-" + d.Name));
            _btLowWarned[d.Name] = d.Percent;
        }
        if (_tab == "home") UpdateHome();
    }

    // ───────── downloads ─────────

    bool _downloadToastUp;

    void ShowDownloadProgress(List<DownloadWatcher.Active> active)
    {
        if (active.Count == 0) { _downloadToastUp = false; return; }
        if (_mode == Mode.Expanded) return;
        var a = active.OrderByDescending(x => x.Size).First();
        var title = active.Count == 1 ? a.Name : $"{active.Count} downloads";
        var sub = $"{DownloadWatcher.Size(a.Size)}" + (a.Speed > 1024 ? $" · {DownloadWatcher.Size(a.Speed)}/s" : "");
        ShowToast(new ToastSpec(title, sub, null, "", null, null, null, 2.2, "download"));
        _downloadToastUp = true;
    }

    void DownloadFinished(string path)
    {
        var name = Path.GetFileName(path);
        long size = 0; try { size = new FileInfo(path).Length; } catch { }
        ShowToast(new ToastSpec("Downloaded", name + (size > 0 ? " · " + DownloadWatcher.Size(size) : ""), null, "", CamGreen, null, null, 6, "download",
            OnClick: () => { try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { } }));
        if (_s.DownloadsToShelf) AddToShelf(new[] { path });
        BridgeEvent?.Invoke("file", path);
    }
}
