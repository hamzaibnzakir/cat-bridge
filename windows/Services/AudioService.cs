using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;

namespace DynamicIsland;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

public sealed class AppAudio
{
    public string Key = "", Name = "";
    public ImageSource? Icon;
    public float Volume;
    public bool Muted;
    internal readonly List<AudioSessionControl> Sessions = new();
}

/// <summary>
/// Default speaker: peak level (visualizer), master volume + OSD events, per-app mixer, output switching.
/// </summary>
public sealed class AudioService : IDisposable
{
    readonly MMDeviceEnumerator _en = new();
    MMDevice? _dev;
    string? _id;
    List<AppAudio> _apps = new();
    readonly Dictionary<uint, (string key, string name, ImageSource? icon)> _procCache = new();

    public event Action<float, bool>? VolumeChanged; // raised on a background thread

    public float Peak
    {
        get { try { return _dev?.AudioMeterInformation.MasterPeakValue ?? 0f; } catch { return 0f; } }
    }

    public float MasterVolume
    {
        get { try { return _dev?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f; } catch { return 0f; } }
        set { try { if (_dev != null) _dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); } catch { } }
    }

    public bool MasterMuted
    {
        get { try { return _dev?.AudioEndpointVolume.Mute ?? false; } catch { return false; } }
        set { try { if (_dev != null) _dev.AudioEndpointVolume.Mute = value; } catch { } }
    }

    public string? DeviceName { get { try { return _dev?.FriendlyName; } catch { return null; } } }

    public void EnsureDevice()
    {
        try
        {
            var d = _en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (d.ID == _id) { d.Dispose(); return; }
            Release();
            _dev = d; _id = d.ID;
            _dev.AudioEndpointVolume.OnVolumeNotification += OnVol;
        }
        catch { Release(); }
    }

    void OnVol(AudioVolumeNotificationData d) => VolumeChanged?.Invoke(d.MasterVolume, d.Muted);

    // ── output devices ──

    public List<AudioDeviceInfo> Devices()
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            foreach (var d in _en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                list.Add(new AudioDeviceInfo(d.ID, d.FriendlyName, d.ID == _id));
                d.Dispose();
            }
        }
        catch { }
        return list;
    }

    /// Uses the (undocumented but long-stable) IPolicyConfig interface — same approach as SoundSwitch / EarTrumpet.
    public bool SetDefaultDevice(string id)
    {
        try
        {
            var pc = (IPolicyConfig)new CPolicyConfigClient();
            for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role); // console, multimedia, communications
            Marshal.ReleaseComObject(pc);
            EnsureDevice();
            return true;
        }
        catch (Exception ex) { App.Log(ex); return false; }
    }

    // ── per-app mixer ──

    public IReadOnlyList<AppAudio> RefreshApps()
    {
        foreach (var a in _apps) foreach (var s in a.Sessions) { try { s.Dispose(); } catch { } }
        var groups = new Dictionary<string, AppAudio>();
        try
        {
            if (_dev == null) { _apps = new(); return _apps; }
            var mgr = _dev.AudioSessionManager;
            mgr.RefreshSessions();
            var sessions = mgr.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                try
                {
                    if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) { s.Dispose(); continue; }
                    (string key, string name, ImageSource? icon) info = s.IsSystemSoundsSession
                        ? ("system", "System sounds", null)
                        : Proc(s.GetProcessID, s.DisplayName);
                    if (!groups.TryGetValue(info.key, out var g))
                    {
                        g = new AppAudio { Key = info.key, Name = info.name, Icon = info.icon, Volume = s.SimpleAudioVolume.Volume, Muted = s.SimpleAudioVolume.Mute };
                        groups[info.key] = g;
                    }
                    g.Sessions.Add(s);
                }
                catch { try { s.Dispose(); } catch { } }
            }
        }
        catch (Exception ex) { App.Log(ex); }
        _apps = groups.Values.OrderBy(a => a.Key == "system").ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return _apps;
    }

    public void SetAppVolume(AppAudio a, float v)
    {
        a.Volume = Math.Clamp(v, 0f, 1f);
        foreach (var s in a.Sessions) { try { s.SimpleAudioVolume.Volume = a.Volume; } catch { } }
    }

    public void SetAppMute(AppAudio a, bool m)
    {
        a.Muted = m;
        foreach (var s in a.Sessions) { try { s.SimpleAudioVolume.Mute = m; } catch { } }
    }

    (string key, string name, ImageSource? icon) Proc(uint pid, string? display)
    {
        if (_procCache.TryGetValue(pid, out var c)) return c;
        string key = $"pid{pid}", name = string.IsNullOrWhiteSpace(display) || display.StartsWith('@') ? $"Process {pid}" : display;
        ImageSource? icon = null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            key = p.ProcessName.ToLowerInvariant();
            name = p.ProcessName;
            try
            {
                var path = p.MainModule?.FileName;
                if (path != null)
                {
                    var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (!string.IsNullOrWhiteSpace(desc)) name = desc!;
                    using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (ico != null)
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                        bs.Freeze();
                        icon = bs;
                    }
                }
            }
            catch { } // elevated / protected processes
        }
        catch { }
        if (name.Length > 28) name = name[..28];
        return _procCache[pid] = (key, name, icon);
    }

    void Release()
    {
        if (_dev == null) return;
        try { _dev.AudioEndpointVolume.OnVolumeNotification -= OnVol; } catch { }
        _dev.Dispose(); _dev = null; _id = null;
    }

    public void Dispose() { Release(); _en.Dispose(); }

    // ── COM interop for switching the default endpoint ──
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class CPolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr fmt);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] bool def, IntPtr fmt);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFmt, IntPtr mixFmt);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] bool def, IntPtr defPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }
}
