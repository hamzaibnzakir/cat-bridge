using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Bridge;
using DynamicIsland.Notify;
using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>Mac-style banners and the sign-in screen summary.</summary>
public partial class IslandWindow
{
    BannerHost? _banners;
    public event Action? NotificationsChanged; // Cat Center refreshes from this
    public NotificationService Notes => _notes;

    BannerHost Banners => _banners ??= new BannerHost(_s, IslandScreenRect);

    Rect? IslandScreenRect()
    {
        try
        {
            var src = PresentationSource.FromVisual(Island);
            if (src?.CompositionTarget == null) return null;
            var tl = src.CompositionTarget.TransformFromDevice.Transform(Island.PointToScreen(new Point(0, 0)));
            var br = src.CompositionTarget.TransformFromDevice.Transform(Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight)));
            return new Rect(tl, br);
        }
        catch { return null; }
    }

    void WireNotifyExtras()
    {
        SystemEvents.SessionSwitch += (_, e) => Dispatcher.InvokeAsync(() =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect) LockFeedWriter.Write(_s);
            else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon) { LockFeedWriter.Unlocked(); LockFeedWriter.Write(_s); }
        });
        LockFeedWriter.Write(_s);
    }

    /// Every incoming notification (iPhone or Windows) goes through here. Returns true if a banner was shown.
    bool Present(NotificationItem n, bool fromPhone, string app)
    {
        LockFeedWriter.Noted(app, n.Title);
        LockFeedWriter.Write(_s);
        NotificationsChanged?.Invoke();

        if (fromPhone ? !_s.BannersForPhone : !_s.BannersForWindows) return false;
        if (FocusSilencing) { FocusHeld++; return true; } // quiet during focus; they're all in Alerts afterwards
        var spec = new BannerSpec
        {
            App = app, Title = n.Title, Body = n.Body, Logo = n.Logo, Source = fromPhone ? "from iPhone" : null,
            OnClick = fromPhone ? () => SelectTab("alerts") : () => NotificationService.OpenApp(n),
        };
        var copyable = (n.Title + " " + n.Body);
        var code = System.Text.RegularExpressions.Regex.Match(copyable, @"\b(\d{4,8})\b(?=.*\b(code|otp|verification|pin)\b)|\b(code|otp|verification|pin)\b\D{0,20}(\d{4,8})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var actions = new List<(string, Action)>();
        if (code.Success)
        {
            var digits = code.Groups[1].Success && code.Groups[1].Value.Length > 0 ? code.Groups[1].Value : code.Groups[4].Value;
            actions.Add(($"Copy code {digits}", () => { try { Clipboard.SetText(digits); } catch { } }));
        }
        actions.Add(("Copy", () => { try { Clipboard.SetText(n.Title + (n.Body.Length > 0 ? "\n" + n.Body : "")); } catch { } }));
        if (!fromPhone) actions.Add(("Open", () => NotificationService.OpenApp(n)));
        spec.Actions = actions.ToArray();
        Banners.Show(spec);
        return true;
    }

    static void OpenUrl(string u) { try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch (Exception ex) { App.Log(ex); } }

    public void TestBanner()
    {
        var n = new NotificationItem { App = "WhatsApp", Title = "Cat", Body = "Meow! This is how banners look. Your code is 482913.", Time = DateTime.Now };
        if (!Present(n, true, n.App))
            Banners.Show(new BannerSpec { App = n.App, Title = n.Title, Body = n.Body, Source = "test" });
    }
}
