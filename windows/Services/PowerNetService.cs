using System;
using System.Collections.Generic;
using Windows.Networking.Connectivity;

namespace DynamicIsland;

public sealed class PowerNetService
{
    public bool HasBattery, OnAC, Charging;
    public int Percent = -1;
    public TimeSpan? Remaining;
    public bool Wifi, Wired, Online;
    public string? Ssid;
    public int Bars; // 0-4

    bool _first = true, _low20, _low10;

    /// <summary>Polls battery + network. Returns event names: plug, unplug, low20, low10, full, wifi-on, wifi-off.</summary>
    public List<string> Poll()
    {
        var ev = new List<string>();
        bool wasAC = OnAC, wasWifi = Wifi, wasOnline = Online; string? wasSsid = Ssid; int wasPct = Percent;

        if (Native.GetSystemPowerStatus(out var st))
        {
            HasBattery = st.BatteryFlag != 128 && st.BatteryFlag != 255;
            OnAC = st.ACLineStatus == 1;
            Percent = st.BatteryLifePercent == 255 ? -1 : st.BatteryLifePercent;
            Charging = HasBattery && ((st.BatteryFlag & 8) != 0 || (OnAC && Percent < 100));
            Remaining = (!OnAC && st.BatteryLifeTime > 0) ? TimeSpan.FromSeconds(st.BatteryLifeTime) : null;
        }

        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            Online = p != null && p.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            if (p != null && p.IsWlanConnectionProfile)
            {
                Wifi = true; Wired = false;
                Ssid = p.WlanConnectionProfileDetails.GetConnectedSsid();
                var b = p.GetSignalBars() ?? 0;            // Windows reports 0-5
                Bars = (int)Math.Ceiling(b * 4 / 5.0);
            }
            else { Wifi = false; Ssid = null; Bars = 0; Wired = p != null; }
        }
        catch { }

        if (_first) { _first = false; return ev; }
        if (HasBattery)
        {
            if (OnAC && !wasAC) ev.Add("plug");
            if (!OnAC && wasAC) ev.Add("unplug");
            if (!OnAC && Percent <= 20 && Percent > 10 && !_low20) { _low20 = true; ev.Add("low20"); }
            if (!OnAC && Percent <= 10 && !_low10) { _low10 = true; ev.Add("low10"); }
            if (OnAC) { _low20 = _low10 = false; }
            if (OnAC && Percent == 100 && wasPct is >= 0 and < 100) ev.Add("full");
        }
        if (Wifi && (!wasWifi || wasSsid != Ssid)) ev.Add("wifi-on");
        if (!Online && wasOnline) ev.Add("wifi-off");
        return ev;
    }
}
