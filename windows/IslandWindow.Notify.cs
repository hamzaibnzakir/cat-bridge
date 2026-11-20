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

/// <summary>Mac-style banners, Shopify sales, and the sign-in screen summary.</summary>
public partial class IslandWindow
{
    BannerHost? _banners;
    public SalesTally Sales { get; } = new();
    public event Action? NotificationsChanged; // Cat Center refreshes from this
    public NotificationService Notes => _notes;
    public event Action<decimal?, string?>? Sale; // the desktop cat celebrates

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
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect) LockFeedWriter.Write(_s, Sales);
            else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon) { LockFeedWriter.Unlocked(); LockFeedWriter.Write(_s, Sales); }
        });
        Sales.Changed += () => { if (_tab == "home") UpdateHome(); };
        LockFeedWriter.Write(_s, Sales);
    }

    /// Every incoming notification (iPhone or Windows) goes through here. Returns true if a banner was shown.
    bool Present(NotificationItem n, bool fromPhone, string app)
    {
        LockFeedWriter.Noted(app, n.Title);
        LockFeedWriter.Write(_s, Sales);
        NotificationsChanged?.Invoke();

        if (_s.ShopifyCards && Shopify.IsShopify(app, n.Title, n.Body) && Shopify.LooksLikeOrder(n.Title, n.Body))
        {
            var order = Shopify.Parse(app, n.Title, n.Body);
            if (!Sales.Add(order)) return true; // same order already counted (arrived twice): stay quiet
            Sale?.Invoke(order.Amount, order.Currency);
            Banners.Show(new BannerSpec
            {
                App = "Shopify", Title = n.Title, Body = n.Body, Order = order, Source = fromPhone ? "from iPhone" : null,
                Footer = "Today: " + Sales.Summary,
                OnClick = () => OpenUrl("https://admin.shopify.com/"),
                Actions = new (string, Action)[] { ("Open Shopify admin", () => OpenUrl("https://admin.shopify.com/")) },
            });
            return true;
        }

        if (fromPhone ? !_s.BannersForPhone : !_s.BannersForWindows) return false;
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
        if (fromPhone) spec.Glyph = null;
        Banners.Show(spec);
        return true;
    }

    static void OpenUrl(string u) { try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch (Exception ex) { App.Log(ex); } }

    public void TestBanner(bool shopify)
    {
        var n = shopify
            ? new NotificationItem { App = "Shopify", Title = "Your store", Body = $"Order #{Random.Shared.Next(1000, 9999)} for $49.00 from Test Customer", Time = DateTime.Now }
            : new NotificationItem { App = "WhatsApp", Title = "Cat", Body = "Meow! This is how banners look. Your code is 482913.", Time = DateTime.Now };
        if (!Present(n, true, n.App))
            Banners.Show(new BannerSpec { App = n.App, Title = n.Title, Body = n.Body, Source = "test" });
    }

    // ───────── island home: Shopify sales widget ─────────

    Border SalesWidget()
    {
        var lead = Bubble("", out var icon);
        icon.Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0xBF, 0x47));
        var card = Small("alerts", lead, "Shopify today", out var sub, out var title);
        _homeUpdaters.Add(() =>
        {
            title.Text = Sales.Orders == 0 ? "Shopify today" : $"{Sales.Orders} order{(Sales.Orders == 1 ? "" : "s")} today";
            var totals = Sales.Totals.OrderByDescending(kv => kv.Value).Select(kv => Shopify.Format(kv.Value, kv.Key == "?" ? null : kv.Key)).ToList();
            sub.Text = Sales.Orders == 0 ? "No orders yet" : totals.Count > 0 ? string.Join(" + ", totals) : "Amounts not shown";
        });
        return card;
    }
}
