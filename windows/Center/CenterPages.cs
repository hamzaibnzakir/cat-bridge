using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Bridge;
using DynamicIsland.Notify;
using DynamicIsland.Taskbar;
using QRCoder;
using static DynamicIsland.Center.Ui;

namespace DynamicIsland.Center;

public sealed partial class CenterWindow
{
    App TheApp => (App)Application.Current;
    Action? _updateHandler;

    // ═════════════════════════ HOME ═════════════════════════

    void HomePage()
    {
        var hero = new Grid();
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hero.Children.Add(new CatFace { Width = 70, Height = 58, Mood = FaceMood.Happy, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center });
        var ht = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var greet = T(Greeting() + ", " + Environment.UserName, 20, Text, FontWeights.SemiBold);
        greet.FontFamily = Display;
        ht.Children.Add(greet);
        var summary = T("", 13, Dim, null, true, new Thickness(0, 4, 0, 0));
        ht.Children.Add(summary);
        Grid.SetColumn(ht, 1);
        hero.Children.Add(ht);
        Add(Box(hero, new Thickness(22, 18, 22, 18)));
        Live(() =>
        {
            var parts = new List<string>();
            parts.Add(_island.OpenWindows.Count == 1 ? "1 app open" : $"{_island.OpenWindows.Count} apps open");
            parts.Add(_island.PhoneSeen > DateTime.Now.AddMinutes(-30) ? "iPhone connected" : "iPhone not seen lately");
            if (TaskbarHider.Hidden) parts.Add("island is your taskbar");
            summary.Text = string.Join("  ·  ", parts);
        });

        Add(Caps("At a glance"));
        var grid = new FlowGrid { MinItemWidth = 200, MaxColumns = 3, Margin = new Thickness(0, 0, -12, 0) };
        var laptop = Tile("", Green, "Laptop", out var lv, out var ls, () => _island.ShowIslandPage("system"));
        Live(() => { lv.Text = _island.HasBattery ? $"{_island.BatteryPercent}%" : "Plugged in"; ls.Text = _island.HasBattery ? (_island.BatteryCharging ? "Charging" : "On battery") : "No battery"; });
        var phone = Tile("", Blue, "iPhone", out var pv, out var ps, () => Go("iphone"));
        Live(() =>
        {
            pv.Text = _island.PhoneBattery is int b ? $"{b}%" : "—";
            ps.Text = _island.PhoneFocus != null ? _island.PhoneFocus + " focus"
                : _island.PhoneSeen == DateTime.MinValue ? "Not connected yet" : "Seen " + Ago(_island.PhoneSeen);
        });
        var notes = Tile("", Accent, "Notifications", out var nv, out var ns, () => Go("notifications"));
        Live(() =>
        {
            var today = _island.Notes.History.Count(n => n.Time.Date == DateTime.Today);
            nv.Text = today.ToString();
            var last = _island.Notes.History.FirstOrDefault();
            ns.Text = last == null ? "Nothing yet today" : "Last: " + Short(last.App) + " · " + Ago(last.Time);
        });
        var bridge = Tile("", Yellow, "Cat Bridge", out var bv, out var bs, () => Go("iphone"));
        Live(() =>
        {
            var ts = _island.BridgeBound.FirstOrDefault(Tailnet.IsTailscale);
            bv.Text = ts != null ? "Online" : _island.BridgeBound.Count > 0 ? "Local only" : "Off";
            bs.Text = ts != null ? $"{ts}:{_s.BridgePort}" : Tailnet.IPv4 == null ? "Tailscale isn't connected" : "Waiting for Tailscale";
        });
        var apps = Tile("", B2(0xBF, 0x5A, 0xF2), "Apps", out var av, out var asub, () => Go("apps"));
        Live(() => { av.Text = _island.OpenWindows.Count.ToString(); asub.Text = TaskbarHider.Hidden ? "Taskbar hidden · island in charge" : "Windows taskbar visible"; });
        var bt = Tile("\uE702", Blue, "Bluetooth", out var btv, out var bts);
        Live(() =>
        {
            var d = _island.BluetoothDevices;
            btv.Text = d.Count == 0 ? "—" : $"{d[0].Percent}%";
            bts.Text = d.Count == 0 ? "No devices with battery info" : d.Count == 1 ? d[0].Name : $"{d[0].Name} + {d.Count - 1} more";
        });
        foreach (var t in new[] { laptop, phone, notes, bridge, apps, bt }) grid.Children.Add(t);
        Add(grid);

        Add(Caps("Quick actions"));
        Add(Buttons(
            Button("Open the island", () => _island.ShowIslandPage("home"), primary: true, glyph: ""),
            Button("Send clipboard to iPhone", () => _island.SendClipboardToPhone(), glyph: ""),
            Button("Ring my iPhone", () => _island.RingPhone(), glyph: ""),
            Button(TaskbarHider.Hidden ? "Show Windows taskbar" : "Hide Windows taskbar", () => { _island.SetTaskbarHidden(!TaskbarHider.Hidden); Go("home", true); }, glyph: ""),
            Button("Try a banner", () => _island.TestBanner(), glyph: "")));

        Add(Caps("Latest notifications"));
        var recent = _island.Notes.History.Take(5).ToList();
        if (recent.Count == 0) Add(Box(T("You're all caught up.", 13, Dim)));
        else Add(Section(recent.Select(NoteRow).ToArray()));
    }

    static Brush B2(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

    static string Greeting()
    {
        int h = DateTime.Now.Hour;
        return h < 5 ? "Up late" : h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : h < 22 ? "Good evening" : "Good night";
    }

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalSeconds < 60 ? "just now" : d.TotalMinutes < 60 ? $"{(int)d.TotalMinutes} min ago" : d.TotalHours < 24 ? $"{(int)d.TotalHours} h ago" : t.ToString("d MMM");
    }

    static string Short(string s) => s.Length > 22 ? s[..22] + "…" : s;

    UIElement NoteRow(NotificationItem n)
    {
        bool phone = n.Aumid == "catbridge.iphone";
        var g = new Grid { Margin = new Thickness(16, 11, 12, 11) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = CardHi, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        icon.Child = n.Logo != null ? new Image { Source = n.Logo, Width = 22, Height = 22 } : Icon(phone ? "\uE8EA" : "\uEA8F", 14, Brushes.White);
        g.Children.Add(icon);
        var st = new StackPanel();
        var head = T((phone ? n.App.Replace("iPhone · ", "") : n.App).ToUpperInvariant() + (phone ? "  ·  FROM IPHONE" : ""), 10.5, Dim, FontWeights.SemiBold, false);
        st.Children.Add(head);
        st.Children.Add(T(n.Title, 13.5, Text, FontWeights.SemiBold, false, new Thickness(0, 2, 0, 0)));
        if (!string.IsNullOrWhiteSpace(n.Body)) st.Children.Add(T(n.Body, 12.5, Dim, null, true, new Thickness(0, 2, 0, 0)).Also(t => t.MaxHeight = 52));
        Grid.SetColumn(st, 1);
        g.Children.Add(st);
        var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        right.Children.Add(T(Ago(n.Time), 11.5, Faint, null, false).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right));
        var copy = Button("", () => { try { Clipboard.SetText(n.Title + (n.Body.Length > 0 ? "\n" + n.Body : "")); _island.ShowNotice("Copied"); } catch { } }, glyph: "");
        copy.Padding = new Thickness(9, 6, 2, 6); copy.Margin = new Thickness(0, 6, 0, 0); copy.HorizontalAlignment = HorizontalAlignment.Right; copy.ToolTip = "Copy";
        right.Children.Add(copy);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }

    // ═════════════════════════ APPS + TASKBAR ═════════════════════════

    void AppsPage()
    {
        Add(Section(
            Row("Hide the Windows taskbar", "The island becomes your taskbar: hover it for your apps, or press Ctrl+Alt+A to search. The taskbar comes back if Dynamic Island closes.",
                Toggle(TaskbarHider.Hidden, v => _island.SetTaskbarHidden(v)), ""),
            Row("Apps dock on the island", "Pinned and running apps at the top of the island's home.", Toggle(_s.DockOnHome, v => { _s.DockOnHome = v; Save(true); }), ""),
            Row("Live previews", "Hover an app to see its window, like the taskbar.", Toggle(_s.DockPreviews, v => { _s.DockPreviews = v; Save(); }), ""),
            Row("Show the real taskbar for a moment", "For its tray icons (Wi-Fi, OneDrive…). Windows only lets Explorer show those.", Button("Show tray icons", () => TaskbarHider.Peek(10), glyph: ""), "")));

        Add(Caps("Pinned to the island"));
        var pins = AppCatalog.Pinned.ToList();
        var pinRows = new List<UIElement>();
        foreach (var p in pins)
        {
            var row = new Grid { Margin = new Thickness(16, 9, 12, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Image { Source = ShellIcons.Get(p.IconPath), Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Left });
            var name = T(p.Name, 13.5, Text, FontWeights.SemiBold, false); name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1); row.Children.Add(name);
            var id = p.Id;
            var btns = Buttons(Button("", () => { AppCatalog.MovePin(id, -1); Go("apps", true); }, glyph: ""), Button("", () => { AppCatalog.MovePin(id, 1); Go("apps", true); }, glyph: ""),
                Button("Unpin", () => { AppCatalog.Unpin(id); Go("apps", true); }));
            btns.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetColumn(btns, 2); row.Children.Add(btns);
            pinRows.Add(row);
        }
        if (pinRows.Count == 0) pinRows.Add(Pad(T("Nothing pinned yet. Search below, or right-click an app in the island's dock.", 13, Dim)));
        Add(Section(pinRows.ToArray()));

        Add(Caps("Find an app to pin"));
        var results = new StackPanel();
        var search = Input("", q =>
        {
            results.Children.Clear();
            foreach (var e in AppCatalog.Search(q, 6))
            {
                var r = new Grid { Margin = new Thickness(16, 8, 12, 8) };
                r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                r.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                r.Children.Add(new Image { Source = ShellIcons.Get(e.Launch), Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Left });
                var t = T(e.Name, 13.5, Text, FontWeights.SemiBold, false); t.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(t, 1); r.Children.Add(t);
                var entry = e;
                var bs = Buttons(Button("Open", () => WindowList.Launch(entry.Launch)),
                    Button(AppCatalog.IsPinned(entry.ParsingPath.ToLowerInvariant()) ? "Pinned" : "Pin", () => { AppCatalog.Pin(AppCatalog.From(entry)); Go("apps", true); }, primary: true));
                bs.Margin = new Thickness(0, 8, 0, 0);
                Grid.SetColumn(bs, 2); r.Children.Add(bs);
                results.Children.Add(r);
            }
        }, placeholder: AppCatalog.All.Count == 0 ? "Loading your apps…" : $"Search {AppCatalog.All.Count} apps");
        Add(Box(new StackPanel { Children = { search, results } }, new Thickness(12)));

        Add(Caps($"Open windows · {_island.OpenWindows.Count}"));
        var winRows = _island.OpenWindows.Select(w =>
        {
            var r = new Grid { Margin = new Thickness(16, 9, 12, 9) };
            r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            r.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            r.Children.Add(new Image { Source = w.Icon, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
            var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(T(w.Title, 13.5, Text, FontWeights.SemiBold, false));
            st.Children.Add(T(w.AppName + (w.Minimized ? " · minimised" : ""), 12, Dim, null, false));
            Grid.SetColumn(st, 1); r.Children.Add(st);
            var hw = w.Hwnd;
            var bs = Buttons(Button("Switch to", () => WindowList.Activate(hw)), Button("Minimise", () => WindowList.Minimize(hw)), Button("Close", () => WindowList.Close(hw), danger: true));
            bs.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetColumn(bs, 2); r.Children.Add(bs);
            return (UIElement)r;
        }).ToArray();
        if (winRows.Length > 0) Add(Section(winRows));

        Add(Caps("Good to know"));
        Add(Box(new StackPanel
        {
            Children =
            {
                T("• Hover the island to see your dock. Click an app to switch to it; click it again to minimise. Right-click for its windows, pin, close.", 13, Dim),
                T("• Start, Task view, Show desktop, notifications and quick settings are on the island's Apps page. The Windows key still opens Start.", 13, Dim, null, true, new Thickness(0, 6, 0, 0)),
                T("• If the taskbar ever stays hidden after a crash, run \"Restore taskbar.cmd\" from the app folder.", 13, Dim, null, true, new Thickness(0, 6, 0, 0)),
            }
        }));
    }

    // ═════════════════════════ NOTIFICATIONS ═════════════════════════

    string _noteFilter = "all", _noteQuery = "";

    void NotificationsPage()
    {
        var access = _island.Notes.Status;
        if (access != "allowed")
            Add(Section(Row("Windows notifications aren't readable yet", access == "denied" ? "Allow notification access for apps in Windows Settings, then restart Dynamic Island." : "This Windows version doesn't let desktop apps read notifications.",
                Button("Open settings", NotificationService.OpenSettings), "")));

        Add(Caps("Banners"));
        Add(Section(
            Row("iPhone notifications", "Drop down like a Mac when your iPhone sends one.", Toggle(_s.BannersForPhone, v => { _s.BannersForPhone = v; Save(); }), ""),
            Row("Windows notifications", "Show Windows' own notifications as Mac-style banners too. Tip: in Windows Settings → Notifications, turn off \"Show notification banners\" for apps so you don't see them twice.",
                Toggle(_s.BannersForWindows, v => { _s.BannersForWindows = v; Save(); }), ""),
            Row("Where", null, Segmented(new[] { ("right", "Top right, like a Mac"), ("center", "Under the island") }, _s.BannerPosition, v => { _s.BannerPosition = v; Save(); }), ""),
            Row("How long", null, Segmented(new[] { ("4", "4 s"), ("6", "6 s"), ("10", "10 s"), ("20", "20 s") }, _s.BannerSeconds.ToString(), v => { _s.BannerSeconds = int.Parse(v); Save(); }), ""),
            Pad(Buttons(Button("Show a test banner", () => _island.TestBanner(), primary: true)))));

        Add(Caps("Sign-in screen"));
        var preview = T("", 12.5, Dim, null, true);
        void Upd() { var t = LockFeedWriter.Compose(_s); preview.Text = t.Length == 0 ? "(nothing to show right now)" : "Preview: " + t; }
        Upd();
        Add(Section(
            Row("Show a summary on the Cat Face sign-in tile", "Counts are safe for shared spaces. Details also shows which apps messaged you.",
                Segmented(new[] { ("off", "Off"), ("counts", "Counts"), ("details", "Details") }, _s.LockScreenFeed, v => { _s.LockScreenFeed = v; Save(); LockFeedWriter.Write(_s); Upd(); }), ""),
            Pad(preview)));

        Add(Caps("Send to your iPhone while you're away"));
        Add(Section(
            Row("Windows notifications", "Forwarded to your iPhone when you haven't touched the laptop for a while.", Toggle(_s.PushForwardNotifications, v => { _s.PushForwardNotifications = v; Save(); }), ""),
            Row("Away after", null, Segmented(new[] { ("1", "1 min"), ("2", "2 min"), ("5", "5 min"), ("15", "15 min") }, _s.PushAwayMinutes.ToString(), v => { _s.PushAwayMinutes = int.Parse(v); Save(); }), ""),
            Row("Focus timer finished", null, Toggle(_s.PushTimer, v => { _s.PushTimer = v; Save(); }), ""),
            Row("Laptop battery low or full", null, Toggle(_s.PushBattery, v => { _s.PushBattery = v; Save(); }), ""),
            Row("Server down or back up", "Always sent, even when you're at the laptop.", Toggle(_s.PushServers, v => { _s.PushServers = v; Save(); }), "")));

        Add(Caps("History"));
        var filter = new WrapPanel();
        filter.Children.Add(Segmented(new[] { ("all", "All"), ("iphone", "iPhone"), ("windows", "Windows") }, _noteFilter, v => { _noteFilter = v; Go("notifications", true); }));
        var q = Input(_noteQuery, null, 220, "Search");
        q.Margin = new Thickness(10, 0, 0, 0);
        q.TextChanged += (_, _) => { _noteQuery = q.Text; FillNotes(); };
        filter.Children.Add(q);
        filter.Children.Add(Button("Clear all", () => { _island.ClearNotifications(); Go("notifications", true); }).Also(b => b.Margin = new Thickness(10, 0, 0, 0)));
        Add(filter);
        _noteList = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        Add(_noteList);
        FillNotes();
    }

    StackPanel? _noteList;

    void FillNotes()
    {
        if (_noteList == null) return;
        _noteList.Children.Clear();
        IEnumerable<NotificationItem> items = _island.Notes.History;
        items = _noteFilter switch
        {
            "iphone" => items.Where(n => n.Aumid == "catbridge.iphone"),
            "windows" => items.Where(n => n.Aumid != "catbridge.iphone"),
            _ => items,
        };
        if (_noteQuery.Trim().Length > 0)
            items = items.Where(n => (n.App + " " + n.Title + " " + n.Body).Contains(_noteQuery.Trim(), StringComparison.CurrentCultureIgnoreCase));
        var list = items.Take(50).ToList();
        if (list.Count == 0) { _noteList.Children.Add(Box(T("No notifications here.", 13, Dim))); return; }
        _noteList.Children.Add(Section(list.Select(NoteRow).ToArray()));
    }

    static UIElement Steps(params string[] steps)
    {
        var sp = new StackPanel();
        for (int i = 0; i < steps.Length; i++)
        {
            var g = new Grid { Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = CardHi, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = T((i + 1).ToString(), 12, Accent, FontWeights.Bold, false).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }) });
            var t = T(steps[i], 13, Text, null, true, new Thickness(0, 3, 0, 0));
            Grid.SetColumn(t, 1); g.Children.Add(t);
            sp.Children.Add(g);
        }
        return sp;
    }

    static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { App.Log(ex); } }

    // ═════════════════════════ IPHONE ═════════════════════════

    void IPhonePage()
    {
        var ip = Tailnet.IPv4;
        var bound = _island.BridgeBound;
        bool online = bound.Any(Tailnet.IsTailscale);

        Add(Section(
            Row(online ? "Cat Bridge is online" : ip == null ? "Tailscale isn't connected" : "Not listening on Tailscale yet",
                online ? $"Your iPhone reaches this laptop at {ip}:{_s.BridgePort} over Tailscale. Only your own Tailscale devices can, and every request needs your pairing key."
                       : ip == null ? "Open Tailscale on this laptop and connect. Cat Bridge picks the address up by itself." : "Press Restart, or check that the port isn't used by another app.",
                Pill(online ? "Online" : "Offline", online ? Green : Red), ""),
            Row("Tailscale address", null, T(ip?.ToString() ?? "—", 13.5, Text, FontWeights.SemiBold, false).Also(t => t.FontFamily = new FontFamily("Cascadia Mono, Consolas")), ""),
            Row("Listen on Tailscale", "Off = only this laptop can reach Cat Bridge.", Toggle(_s.BridgeListenTailscale, v => { _s.BridgeListenTailscale = v; Save(); _island.RestartBridge(); Go("iphone", true); }), ""),
            Row("Port", "Change only if 47800 is taken.", PortBox(), ""),
            Row("Windows Firewall", "Allows only Tailscale addresses (100.x) to reach the bridge port. Needs one admin prompt.",
                Button("Allow Tailscale devices", async () =>
                {
                    var (ok, msg) = await Tailnet.AllowThroughFirewall(_s.BridgePort);
                    _island.ShowNotice(ok ? "Firewall updated" : "Firewall not changed", msg);
                }), ""),
            Row("Stay reachable while plugged in", "Stops Windows from sleeping on power so your iPhone can always wake and unlock it. The screen still turns off.",
                Toggle(_s.StayAwakeOnPower, v => { _s.StayAwakeOnPower = v; _s.Save(); _island.ApplyStayAwake(); }), "\uE945"),
            Row("Let my iPhone see the screen", "Shows this laptop's screen in the iPhone's Remote tab, so it works like a small laptop. A purple dot on the island means it's being watched.",
                Toggle(_s.ScreenShare, v => { _s.ScreenShare = v; _s.Save(); }), "\uE7F4"),
            Pad(Buttons(Button("Restart bridge", () => { _island.RestartBridge(); Go("iphone", true); }), Button("Open Tailscale admin", () => Open("https://login.tailscale.com/admin/machines"))))));

        // find my laptop
        Add(Caps("Find my laptop"));
        var lostMsg = Input(_s.LostMessage, v => { _s.LostMessage = v; _s.Save(); }, placeholder: "e.g. Please call 0803 000 0000. Reward if found.");
        Add(Section(
            Row("Location", "The iPhone app shows where the laptop is. Allow location for desktop apps in Windows Settings → Privacy & security → Location.",
                Button("Location settings", () => Open("ms-settings:privacy-location")), "\uE707"),
            Row("Lost mode", "Locks the laptop and shows your message on screen and on the sign-in tile. You can turn it on from the iPhone too.",
                Toggle(_s.LostMode, v => _island.SetLostMode(v, null)), "\uE72E"),
            Stacked("Message", null, lostMsg),
            Pad(Buttons(Button("Test the alarm sound", () => _island.StartAlarm()), Button("Stop", () => _island.StopAlarm())))));

        // pairing
        Add(Caps("Pair the Cat Bridge iPhone app"));
        var pairGrid = new Grid();
        pairGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pairGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var link = PairLink(ip?.ToString());
        var qrBox = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(16), Padding = new Thickness(10), Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Top };
        if (link != null) { var img = new Image { Source = Qr(link), Width = 188, Height = 188 }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor); qrBox.Child = img; }
        else qrBox.Child = new Border { Width = 188, Height = 188, Child = T("Connect Tailscale to show the pairing code", 12.5, Brushes.Black, FontWeights.SemiBold).Also(t => { t.TextAlignment = TextAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(14); }) };
        pairGrid.Children.Add(qrBox);
        var steps = new StackPanel();
        steps.Children.Add(Steps(
            "Install Tailscale on your iPhone and sign in with the same account as this laptop.",
            "Install the Cat Bridge app (the .ipa from your GitHub build, with Sideloadly).",
            "Point the iPhone Camera at this code and tap the link, or scan it inside Cat Bridge.",
            "That's it. The app remembers this laptop."));
        steps.Children.Add(Buttons(
            Button("Copy pairing link", () => { if (link != null) { Clipboard.SetText(link); _island.ShowNotice("Pairing link copied", "Send it to yourself, then open it on the iPhone"); } }),
            Button("New key", () =>
            {
                if (MessageBox.Show(this, "Make a new pairing key? Every paired iPhone will need to scan again.", "Cat Bridge", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                _s.BridgeKey = BridgeServer.NewKey(); Save(); _island.RestartBridge(); Go("iphone", true);
            }, danger: true)).Also(w => w.Margin = new Thickness(0, 14, 0, 0)));
        steps.Children.Add(T("Anyone who scans this code can pair, so don't share screenshots of it.", 12, Faint, null, true, new Thickness(0, 4, 0, 0)));
        Grid.SetColumn(steps, 1);
        pairGrid.Children.Add(steps);
        Add(Box(pairGrid, new Thickness(20)));
        Live(() => { }); // keeps page in live mode

        // Bark
        Add(Caps("Alerts on your iPhone (Bark)"));
        var barkResult = T("", 12.5, Dim, null, true);
        Add(Section(
            Pad(Steps(
                "Install the free Bark app from the App Store and allow notifications.",
                "In Bark, copy your key: it's the part after api.day.app/ in the example link.",
                "Optional, recommended: in Bark → Settings → Encryption, pick AES-128/CBC, make a key and IV, and paste them below. Then only your iPhone can read the alerts.")),
            Pad(Buttons(Button("Get Bark on the App Store", () => Open("https://apps.apple.com/app/bark/id1403753865"))), 16, 0, 16, 4),
            Stacked("Bark key", null, Input(_s.BarkKey, v => { _s.BarkKey = v.Trim(); _s.Save(); }, placeholder: "e.g. ynJ5Ft4atkMkWeo2PAvFhF", mono: true)),
            Stacked("Bark server", "Leave as is, or use your own bark-server (for example on your VPS over Tailscale).", Input(_s.BarkServer, v => { _s.BarkServer = v.Trim(); _s.Save(); }, mono: true)),
            Stacked("Encryption key (16, 24 or 32 characters)", null, Secret(_s.BarkEncryptKey, v => { _s.BarkEncryptKey = v; _s.Save(); }, "Optional")),
            Stacked("Encryption IV (16 characters)", null, Secret(_s.BarkEncryptIv, v => { _s.BarkEncryptIv = v; _s.Save(); }, "Optional")),
            Pad(new StackPanel
            {
                Children =
                {
                    Buttons(
                        Button("Send a test", async () => { barkResult.Text = "Sending…"; var err = await _island.Push.TestBark(); barkResult.Text = err == null ? "Sent. Check your iPhone." : err; barkResult.Foreground = err == null ? Green : Red; }, primary: true),
                        Button("Ring my iPhone", () => _island.RingPhone()),
                        Button("Send clipboard", () => _island.SendClipboardToPhone())),
                    barkResult,
                }
            })));

        // iPhone → laptop
        Add(Caps("Your iPhone's notifications on this laptop"));
        Add(Box(Steps(
            "Shortcuts → Automation → + → App notification → pick an app (WhatsApp, Shopify, Messages, Mail…) → Run Immediately.",
            "Add \"Send to Laptop\" from Cat Bridge. Fill App, Title and Body from the notification.",
            "Repeat for each app you want. iOS lets each automation watch one app.",
            "Battery and Focus: make automations for Battery Level, Charger and Focus that run \"Update Laptop\" from Cat Bridge. The iPhone widget on the island shows them.")));

        Add(Caps("iPhone status"));
        var status = T("", 13, Text);
        Live(() => status.Text = _island.PhoneSeen == DateTime.MinValue ? "Nothing received from your iPhone yet."
            : $"Battery {(_island.PhoneBattery is int b ? b + "%" : "unknown")}{(_island.PhoneCharging == true ? " (charging)" : "")}" +
              $"  ·  Focus {(_island.PhoneFocus ?? "off")}  ·  Last contact {Ago(_island.PhoneSeen)}" +
              (_island.PhoneLastApp != null ? $"  ·  Last notification from {_island.PhoneLastApp}, {Ago(_island.PhoneLastTime)}" : ""));
        Add(Box(status));
    }

    UIElement PortBox()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var tb = Input(_s.BridgePort.ToString(), null, 90, mono: true);
        sp.Children.Add(tb);
        sp.Children.Add(Button("Apply", () =>
        {
            if (int.TryParse(tb.Text.Trim(), out int p) && p is >= 1024 and <= 65535) { _s.BridgePort = p; Save(); _island.RestartBridge(); Go("iphone", true); }
            else _island.ShowNotice("Pick a port between 1024 and 65535");
        }).Also(b => b.Margin = new Thickness(8, 0, 0, 0)));
        return sp;
    }

    string? PairLink(string? host)
    {
        if (host == null) return null;
        string E(string s) => Uri.EscapeDataString(s);
        return $"catbridge://pair?host={E(host)}&port={_s.BridgePort}&key={E(_s.BridgeKey)}&name={E(Environment.MachineName)}";
    }

    static ImageSource Qr(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        var bi = new BitmapImage();
        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(png); bi.EndInit(); bi.Freeze();
        return bi;
    }

    // ═════════════════════════ FOCUS ═════════════════════════

    void FocusPage()
    {
        var st = _island.GetFocusStats();
        var grid = new FlowGrid { MinItemWidth = 190, MaxColumns = 4, Margin = new Thickness(0, 0, -12, 0) };
        var t1 = Tile("\uE916", Accent, "Today", out var v1, out var s1); v1.Text = $"{st.TodayMinutes:0} min"; s1.Text = $"{st.TodaySessions} session{(st.TodaySessions == 1 ? "" : "s")}";
        var t2 = Tile("\uE787", Blue, "This week", out var v2, out var s2); v2.Text = st.WeekMinutes >= 60 ? $"{st.WeekMinutes / 60:0.#} h" : $"{st.WeekMinutes:0} min"; s2.Text = $"{st.Completed} finished in full";
        var t3 = Tile("\uE945", Yellow, "Streak", out var v3, out var s3); v3.Text = $"{st.Streak} day{(st.Streak == 1 ? "" : "s")}"; s3.Text = st.Streak > 0 ? "Keep it going" : "Start one today";
        foreach (var t in new[] { t1, t2, t3 }) grid.Children.Add(t);
        Add(grid);

        // weekly bars
        var bars = new Grid { Height = 170, Margin = new Thickness(0, 4, 0, 0) };
        double max = Math.Max(30, st.Week.Max());
        for (int i = 0; i < 7; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var col = new Grid();
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var h = st.Week[i] / max * 120;
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            stack.Children.Add(T(st.Week[i] > 0 ? $"{st.Week[i]:0}" : "", 11.5, Dim, null, false).Also(x => x.HorizontalAlignment = HorizontalAlignment.Center));
            stack.Children.Add(new Border { Height = Math.Max(4, h), Width = 26, CornerRadius = new CornerRadius(7, 7, 3, 3), Margin = new Thickness(0, 4, 0, 0),
                Background = i == 6 ? Accent : new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xB3, 0x7A)), HorizontalAlignment = HorizontalAlignment.Center });
            col.Children.Add(stack);
            var lab = T(st.WeekLabels[i], 11.5, i == 6 ? Text : Dim, null, false, new Thickness(0, 6, 0, 0)); lab.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(lab, 1); col.Children.Add(lab);
            Grid.SetColumn(col, i); bars.Children.Add(col);
        }
        Add(Box(new StackPanel { Children = { T("Minutes of focus, last 7 days", 13, Dim), bars } }));

        Add(Caps("Start"));
        Add(Buttons(
            Button($"Focus {_s.PomodoroMinutes} min", () => { _island.StartFocus(_s.PomodoroMinutes); Go("focus", true); }, primary: true, glyph: "\uE708"),
            Button("50 min", () => { _island.StartFocus(50); Go("focus", true); }),
            Button("90 min", () => { _island.StartFocus(90); Go("focus", true); }),
            Button("Stop", () => { _island.EndFocus(false); Go("focus", true); }, danger: true)));

        Add(Caps("On the laptop"));
        Add(Section(
            Row("Hold notifications during focus", "Banners and pop-ups wait quietly in Alerts; you get one summary when you're done.", Toggle(_s.FocusSilence, v => { _s.FocusSilence = v; _s.Save(); }), "\uE7ED"),
            Row("Pomodoro length", null, Segmented(new[] { ("15", "15"), ("25", "25"), ("45", "45"), ("60", "60") }, Nearest(_s.PomodoroMinutes, 15, 25, 45, 60), v => { _s.PomodoroMinutes = int.Parse(v); _s.Save(); }), "\uE916")));

        Add(Caps("With your iPhone"));
        Add(Section(
            Row("Follow my iPhone's Focus", "When Focus turns on on your iPhone (via the Update Laptop automation), the laptop goes quiet too.", Toggle(_s.FollowPhoneFocus, v => { _s.FollowPhoneFocus = v; _s.Save(); }), "\uE8EA"),
            Row("...and start a Pomodoro", null, Toggle(_s.PhoneFocusStartsTimer, v => { _s.PhoneFocusStartsTimer = v; _s.Save(); }), "\uE916"),
            Row("Tap my iPhone when focus starts here", "iOS doesn't let apps switch Focus, so the laptop sends a Bark alert; tap it to run your Shortcut.", Toggle(_s.FocusTapsPhone, v => { _s.FocusTapsPhone = v; _s.Save(); }), "\uEA8F"),
            Stacked("Shortcut that turns Focus on", "Make it in Shortcuts: one action, Set Focus → Work → On.", Input(_s.FocusShortcutOn, v => { _s.FocusShortcutOn = v.Trim(); _s.Save(); })),
            Stacked("Shortcut that turns Focus off", null, Input(_s.FocusShortcutOff, v => { _s.FocusShortcutOff = v.Trim(); _s.Save(); }))));
    }

    // ═════════════════════════ ISLAND ═════════════════════════

    static readonly (string id, string name, string glyph)[] WidgetNames =
    {
        ("media", "Now playing (with lyrics)", ""), ("iphone", "iPhone", ""), ("system", "CPU, RAM and GPU", ""),
        ("battery", "Battery and Wi-Fi", ""), ("sound", "Sound and brightness", ""), ("alerts", "Notifications", ""),
        ("timer", "Focus timer", ""), ("shelf", "Shelf and clipboard", ""), ("servers", "Servers", ""),
    };

    void IslandPage()
    {
        Add(Caps("Widgets on the island's home"));
        var rows = new List<UIElement>();
        var order = _s.Widgets.Where(w => WidgetNames.Any(n => n.id == w)).ToList();
        foreach (var (id, name, glyph) in WidgetNames.OrderBy(w => order.Contains(w.id) ? order.IndexOf(w.id) : 100))
        {
            var on = _s.Widgets.Contains(id);
            var ctl = new StackPanel { Orientation = Orientation.Horizontal };
            if (on)
            {
                ctl.Children.Add(Button("", () => { Move(id, -1); }, glyph: "").Also(b => { b.Margin = new Thickness(0, 0, 6, 0); b.ToolTip = "Move up"; }));
                ctl.Children.Add(Button("", () => { Move(id, 1); }, glyph: "").Also(b => { b.Margin = new Thickness(0, 0, 12, 0); b.ToolTip = "Move down"; }));
            }
            ctl.Children.Add(Toggle(on, v => { if (v) _s.Widgets.Add(id); else _s.Widgets.Remove(id); Save(true); Go("island", true); }));
            rows.Add(Row(name, null, ctl, glyph));
        }
        Add(Section(rows.ToArray()));

        Add(Caps("Look"));
        Add(Section(
            Row("Accent colour", null, Swatches(new[] { "#FFB37A", "#FF7A90", "#7AD7FF", "#A78BFA", "#7CE38B", "#FFD166" }, _s.Accent, v => { _s.Accent = v; Save(true); }), ""),
            Row("Distance from the top", null, Segmented(new[] { ("0", "Flush"), ("6", "Normal"), ("12", "Lower") }, _s.TopOffset.ToString(), v => { _s.TopOffset = int.Parse(v); Save(); }), ""),
            Row("Lyrics in the pill", "Shows the current line of the song in the small island.", Toggle(_s.LyricsInPill, v => { _s.LyricsInPill = v; Save(); }), ""),
            Row("Volume pop-up", null, Toggle(_s.ShowVolumeOsd, v => { _s.ShowVolumeOsd = v; Save(); }), ""),
            Row("Brightness pop-up", null, Toggle(_s.ShowBrightnessOsd, v => { _s.ShowBrightnessOsd = v; Save(); }), ""),
            Row("Camera and microphone dots", null, Toggle(_s.PrivacyDots, v => { _s.PrivacyDots = v; Save(); }), ""),
            Row("Hide during full-screen apps", null, Toggle(_s.HideInFullscreen, v => { _s.HideInFullscreen = v; Save(); }), "")));

        Add(Caps("Timing"));
        Add(Section(
            Row("Open after hovering", null, Segmented(new[] { ("80", "Instant"), ("180", "Quick"), ("350", "Relaxed") }, Nearest(_s.HoverDelayMs, 80, 180, 350), v => { _s.HoverDelayMs = int.Parse(v); Save(); }), ""),
            Row("Close after leaving", null, Segmented(new[] { ("200", "Fast"), ("300", "Normal"), ("700", "Slow") }, Nearest(_s.CollapseDelayMs, 200, 300, 700), v => { _s.CollapseDelayMs = int.Parse(v); Save(); }), ""),
            Row("A pinned island lets go after", null, Segmented(new[] { ("30", "30 s"), ("60", "1 min"), ("300", "5 min"), ("0", "Never") }, Nearest(_s.PinTimeoutSeconds, 30, 60, 300, 0), v => { _s.PinTimeoutSeconds = int.Parse(v); Save(); }), ""),
            Row("Touchpad gestures", "Swipe on the island to change songs and volume.", Toggle(_s.Gestures, v => { _s.Gestures = v; Save(); }), "")));
    }

    void Move(string id, int d)
    {
        int i = _s.Widgets.IndexOf(id), j = i + d;
        if (i < 0 || j < 0 || j >= _s.Widgets.Count) return;
        (_s.Widgets[i], _s.Widgets[j]) = (_s.Widgets[j], _s.Widgets[i]);
        Save(true); Go("island", true);
    }

    static string Nearest(int v, params int[] options) => options.OrderBy(o => Math.Abs(o - v)).First().ToString();

    static UIElement Swatches(string[] colors, string selected, Action<string> pick)
    {
        var w = new WrapPanel();
        var rings = new List<Border>();
        foreach (var c in colors)
        {
            var col = (Color)ColorConverter.ConvertFromString(c);
            var ring = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Padding = new Thickness(3),
                BorderBrush = string.Equals(c, selected, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent,
                Child = new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(col) }, Tag = c };
            ring.MouseLeftButtonUp += (_, _) => { foreach (var r in rings) r.BorderBrush = Brushes.Transparent; ring.BorderBrush = Brushes.White; pick(c); };
            rings.Add(ring); w.Children.Add(ring);
        }
        return w;
    }

    // ═════════════════════════ CAT ═════════════════════════

    void CatPage()
    {
        Add(Section(
            Row("Show the desktop cat", "It walks on your taskbar line, naps when you're away, and never gets in the way of clicks.", Toggle(TheApp.PetVisible, _ => TheApp.TogglePet()), ""),
            Row("Coat", null, Segmented(new[] { ("ginger", "Ginger"), ("black", "Black"), ("white", "White"), ("grey", "Grey") }, _s.PetColor, v => TheApp.SetPetColor(v)), ""),
            Row("Stretch reminder", "The cat nudges you to take a break after this much continuous work.", Segmented(new[] { ("0", "Off"), ("30", "30 min"), ("50", "50 min"), ("90", "90 min") }, Nearest(_s.BreakReminderMinutes, 0, 30, 50, 90), v => { _s.BreakReminderMinutes = int.Parse(v); Save(); }), "")));
        Add(Caps("Things the cat does"));
        Add(Box(new StackPanel
        {
            Children =
            {
                T("• Click it for a menu: focus timer, play/pause, open the island, find my iPhone.", 13, Dim),
                T("• Drag it anywhere. Drop files on it to put them on the shelf.", 13, Dim, null, true, new Thickness(0, 6, 0, 0)),
                T("• It looks up for notifications, and gets sleepy when your battery is low.", 13, Dim, null, true, new Thickness(0, 6, 0, 0)),
            }
        }));
    }

    // ═════════════════════════ FILES ═════════════════════════

    void FilesPage()
    {
        var recv = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Cat Bridge");
        Add(Section(
            Row("Files from your iPhone", recv, Button("Open folder", () => { Directory.CreateDirectory(recv); Open(recv); }), ""),
            Row("Clipboard history", "Keeps what you copy so you can paste it again from the island.", Toggle(_s.ClipboardHistory, v => { _s.ClipboardHistory = v; Save(); }), ""),
            Row("Paste when picked", "Clicking a clipboard item pastes it straight into your app.", Toggle(_s.ClipboardAutoPaste, v => { _s.ClipboardAutoPaste = v; Save(); }), "")));

        Add(Caps("Screen"));
        Add(Section(
            Row("Copy text from anything on screen", "Press Ctrl + Alt + T, then drag over the text: a picture, a video, a PDF, an app. Works offline.", Button("Try it", () => _island.CaptureText(), primary: true), "\uE8C8"),
            Row("Screenshot to the shelf", "Ctrl + Alt + S, then drag. Saved to Pictures\\Screenshots\\Cat Bridge and copied.", Button("Take one", () => _island.CaptureScreenshot()), "\uE722"),
            Row("Put my other screenshots on the shelf", "Win + Shift + S and Print Screen shots are saved and shelved too.", Toggle(_s.ScreenshotShelf, v => { _s.ScreenshotShelf = v; _s.Save(); }), "\uE7B8")));

        Add(Caps("Downloads"));
        Add(Section(
            Row("Show downloads in the pill", "Edge, Chrome, Firefox, Brave and Opera downloads, with speed; a pop-up when they finish.", Toggle(_s.ShowDownloads, v => { _s.ShowDownloads = v; _s.Save(); _island.ShowNotice("Restart Cat Bridge to apply"); }), "\uE896"),
            Row("Put finished downloads on the shelf", null, Toggle(_s.DownloadsToShelf, v => { _s.DownloadsToShelf = v; _s.Save(); }), "\uE8B7")));

        Add(Caps($"On the shelf · {_island.Shelf.Paths.Count}"));
        var rows = _island.Shelf.Paths.Select(p =>
        {
            var r = new Grid { Margin = new Thickness(16, 9, 12, 9) };
            r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            r.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            r.Children.Add(new Image { Source = ShelfStore.IconFor(p), Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Left });
            var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(T(Path.GetFileName(p.TrimEnd('\\')), 13.5, Text, FontWeights.SemiBold, false));
            st.Children.Add(T(Path.GetDirectoryName(p) ?? "", 12, Dim, null, false));
            Grid.SetColumn(st, 1); r.Children.Add(st);
            var path = p;
            var bs = Buttons(Button("Open", () => Open(path)), Button("Show", () => { try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { } }),
                Button("Remove", () => { _island.Shelf.Remove(path); Go("files", true); }, danger: true));
            bs.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetColumn(bs, 2); r.Children.Add(bs);
            return (UIElement)r;
        }).ToArray();
        if (rows.Length == 0) Add(Box(T("Nothing on the shelf. Drop files on the island or the cat.", 13, Dim)));
        else { Add(Section(rows)); Add(Buttons(Button("Clear shelf", () => { _island.Shelf.Clear(); Go("files", true); }, danger: true)).Also(w => w.Margin = new Thickness(0, 12, 0, 0))); }
    }

    // ═════════════════════════ FACE ═════════════════════════

    void FacePage()
    {
        bool enrolled = FaceId.FaceStore.Exists, signIn = FaceId.FaceSignInWindow.Installed;
        Add(Section(
            Row("Your face", enrolled ? "Saved, encrypted, on this PC only." : "Not set up yet.", Pill(enrolled ? "Set up" : "Not set up", enrolled ? Green : Yellow), ""),
            Row("On the Windows sign-in screen", signIn ? "Cat Face is on the sign-in screen, even after a restart." : "Not on the sign-in screen yet.", Pill(signIn ? "On" : "Off", signIn ? Green : Dim), ""),
            Pad(Buttons(
                Button(enrolled ? "Set up again" : "Set up my face", () => new FaceId.FaceWindow(_s, FaceId.FaceMode.Enroll).Show(), primary: !enrolled),
                Button("Test it", () => new FaceId.FaceWindow(_s, FaceId.FaceMode.Test).Show()).Also(b => b.IsEnabled = enrolled),
                Button(signIn ? "Sign-in screen settings" : "Use on sign-in screen", () => new FaceId.FaceSignInWindow(_s).Show(), primary: enrolled && !signIn),
                Button("Delete my face", () =>
                {
                    if (MessageBox.Show(this, "Delete your saved face? You can set it up again any time.", "Face unlock", MessageBoxButton.OKCancel) == MessageBoxResult.OK) { FaceId.FaceStore.Delete(); Go("face", true); }
                }, danger: true)))));
        // ── unlock with iPhone ──
        Add(Caps("Unlock with your iPhone"));
        var phones = Shared.PhoneTrust.Load();
        var rows = new List<UIElement>();
        if (_island.PendingUnlockPhone is { } req)
        {
            var result = T("", 12.5, Dim);
            var box = new StackPanel();
            box.Children.Add(T($"{req.Name} wants to unlock this PC", 15, Text, FontWeights.SemiBold));
            box.Children.Add(T("Only allow it if your iPhone shows the same code:", 13, Dim, null, true, new Thickness(0, 4, 0, 10)));
            box.Children.Add(T(req.Code[..3] + " " + req.Code[3..], 34, Accent, FontWeights.Bold, false).Also(t => t.FontFamily = new FontFamily("Cascadia Mono, Consolas")));
            box.Children.Add(Buttons(
                Button("Allow", async () => { result.Text = "Waiting for the admin prompt…"; var (ok, msg) = await _island.AllowUnlockPhone(); result.Text = msg; result.Foreground = ok ? Green : Red; if (ok) Go("face", true); }, primary: true, glyph: "\uE73E"),
                Button("Don't allow", () => _island.DenyUnlockPhone(), danger: true)).Also(w => w.Margin = new Thickness(0, 14, 0, 0)));
            box.Children.Add(result);
            Add(Box(box, new Thickness(20)).Also(b => { b.BorderBrush = Accent; b.Margin = new Thickness(0, 0, 0, 12); }));
        }
        foreach (var p in phones)
        {
            var ph = p;
            rows.Add(Row(p.Name, "Can unlock this PC with Face ID · added " + (DateTime.TryParse(p.Added, out var d) ? d.ToLocalTime().ToString("d MMM yyyy") : p.Added),
                Button("Remove", async () => { var (ok, msg) = await _island.RemoveUnlockPhone(ph.Id); _island.ShowNotice(ok ? "Removed " + ph.Name : "Not removed", ok ? null : msg); Go("face", true); }, danger: true), "\uE8EA"));
        }
        if (phones.Count == 0)
            rows.Add(Pad(Steps(
                "Turn on Cat Face on the sign-in screen above (your iPhone unlocks through it).",
                "In the Cat Bridge iPhone app: Settings → Unlock laptop with Face ID → Set up.",
                "Check the code on the iPhone matches the one shown here, then press Allow.",
                "Lock the laptop and tap Unlock laptop on the iPhone, or press \"Unlock with my iPhone\" on the lock screen.")));
        rows.Add(Pad(T("Your iPhone keeps a key in its Secure Enclave that only works after Face ID. The lock screen checks its signature itself, so nothing else on this PC can fake an unlock. It works while you're signed in and the PC is locked; after a restart, sign in once with Cat Face or your password.", 12.5, Dim)));
        Add(Section(rows.ToArray()));

        Add(Caps("How it keeps you safe"));
        Add(Box(new StackPanel
        {
            Children =
            {
                T("• It checks that you're really there: turn your head when asked, then look back.", 13, Dim),
                T("• Two failed tries and it asks for your password instead.", 13, Dim, null, true, new Thickness(0, 6, 0, 0)),
                T("• Your password, PIN and other sign-in options always stay available.", 13, Dim, null, true, new Thickness(0, 6, 0, 0)),
            }
        }));
    }

    // ═════════════════════════ SERVERS ═════════════════════════

    void ServersPage()
    {
        var rows = new List<UIElement>();
        foreach (var m in _s.Monitors.ToList())
        {
            var mon = m;
            var g = new Grid { Margin = new Thickness(16, 10, 12, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Input(mon.Name, v => { mon.Name = v; _s.Save(); }, placeholder: "Name");
            var host = Input(mon.Host, v => { mon.Host = v.Trim(); _s.Save(); }, placeholder: "Host or IP", mono: true); host.Margin = new Thickness(8, 0, 0, 0);
            var port = Input(mon.Port > 0 ? mon.Port.ToString() : "", v => { if (int.TryParse(v, out int p)) { mon.Port = p; _s.Save(); } }, placeholder: "Port", mono: true); port.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(host, 1); Grid.SetColumn(port, 2);
            var del = Button("", () => { _s.Monitors.Remove(mon); _s.Save(); Go("servers", true); }, glyph: "", danger: true);
            del.Margin = new Thickness(8, 0, 0, 0); del.ToolTip = "Remove";
            Grid.SetColumn(del, 3);
            g.Children.Add(name); g.Children.Add(host); g.Children.Add(port); g.Children.Add(del);
            rows.Add(g);
        }
        if (rows.Count == 0) rows.Add(Pad(T("No servers yet.", 13, Dim)));
        Add(Section(rows.ToArray()));
        Add(Buttons(Button("Add a server", () => { _s.Monitors.Add(new MonitorTarget { Name = "New server", Type = "tcp", Port = 22 }); _s.Save(); Go("servers", true); }, primary: true, glyph: ""))
            .Also(w => w.Margin = new Thickness(0, 12, 0, 0)));
        Add(Section(Row("Check every", "Down and back-up alerts go to the island and your iPhone.", Segmented(new[] { ("15", "15 s"), ("30", "30 s"), ("60", "1 min"), ("300", "5 min") }, Nearest(_s.MonitorIntervalSeconds, 15, 30, 60, 300), v => { _s.MonitorIntervalSeconds = int.Parse(v); Save(); }), ""))
            .Also(b => b.Margin = new Thickness(0, 8, 0, 0)));
    }

    // ═════════════════════════ GENERAL ═════════════════════════

    void GeneralPage()
    {
        Add(Caps("Updates"));
        var status = T("", 13, Dim, null, true);
        var notes = T("", 12.5, Dim, null, true, new Thickness(0, 8, 0, 0));
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var bar = new ProgressBar { Height = 4, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 10, 0, 0), Foreground = Accent, Background = CardHi, BorderThickness = new Thickness(0), Visibility = Visibility.Collapsed };
        void Refresh()
        {
            status.Text = Updater.Status + (Updater.LastCheck > DateTime.MinValue ? $"  ·  checked {Ago(Updater.LastCheck)}" : "");
            bar.Visibility = Updater.Progress is double p ? Visibility.Visible : Visibility.Collapsed;
            if (Updater.Progress is double pp) bar.Value = pp;
            actions.Children.Clear();
            actions.Children.Add(Button("Check now", () => _ = Updater.CheckAsync(userAsked: true)).Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
            if (Updater.Available is { } r)
            {
                actions.Children.Add(Button(Updater.Installed ? $"Install {r.Version.ToString(3)}" : "Download", () => _ = Updater.InstallAsync(quiet: false), primary: true).Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
                actions.Children.Add(Button("What's new", () => Open(r.PageUrl)).Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
                actions.Children.Add(Button("Skip this version", () => { Updater.Skip(); _island.ShowNotice("Skipped " + r.Tag); }).Also(b => b.Margin = new Thickness(0, 0, 8, 8)));
                var n = r.Notes.Replace("\r", "").Trim();
                notes.Text = n.Length > 700 ? n[..700] + "…" : n;
                notes.Visibility = n.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else notes.Visibility = Visibility.Collapsed;
        }
        if (_updateHandler != null) Updater.Changed -= _updateHandler;
        _updateHandler = () => Dispatcher.InvokeAsync(Refresh);
        Updater.Changed += _updateHandler;
        Refresh();
        Add(Section(
            Row($"Cat Bridge {Updater.CurrentVersion.ToString(3)}", Updater.Installed ? "Installed with the Cat Bridge installer." : "Running from a folder: updates open the download page instead of installing themselves.", null, "\uE896"),
            Row("Updates", "Automatic installs new versions quietly (the app restarts by itself). Ask me shows a pop-up first.",
                Segmented(new[] { ("auto", "Automatic"), ("notify", "Ask me"), ("off", "Off") }, _s.UpdateMode, v => { _s.UpdateMode = v; _s.Save(); }), "\uE895"),
            Pad(new StackPanel { Children = { status, bar, notes, actions } })));

        Add(Section(
            Row("Start with Windows", null, Toggle(Autostart.IsEnabled, v => Autostart.Set(v)), ""),
            Row("Settings file", AppSettings.FilePath, Button("Open", () => { try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppSettings.FilePath}\"") { UseShellExecute = true }); } catch { } }), ""),
            Row("Log and data folder", AppSettings.Dir, Button("Open", () => Open(AppSettings.Dir)), ""),
            Row("Reload settings from the file", "Use after editing settings.json by hand.", Button("Reload", () => { _island.ReloadSettings(); Go("general", true); }), "")));

        Add(Caps("Keyboard shortcuts"));
        Add(Section(
            Row("Open or pin the island", null, Pill("Ctrl + Alt + Space", Text), ""),
            Row("Search and open apps", null, Pill("Ctrl + Alt + A", Text), ""),
            Row("Open Cat Center", null, Pill("Ctrl + Alt + C", Text), ""),
            Row("Hide or show the island", null, Pill("Ctrl + Alt + H", Text), ""),
            Row("Copy text from the screen", null, Pill("Ctrl + Alt + T", Text), "\uE8C8"),
            Row("Screenshot to the shelf", null, Pill("Ctrl + Alt + S", Text), "\uE722")));

        var v = typeof(App).Assembly.GetName().Version;
        Add(Caps("About"));
        Add(Box(new StackPanel
        {
            Children =
            {
                T($"Dynamic Island {v?.Major}.{v?.Minor}", 15, Text, FontWeights.SemiBold),
                T("Island, desktop cat, Cat Face sign-in, Cat Bridge for iPhone, Mac-style notifications and the island taskbar.", 13, Dim, null, true, new Thickness(0, 4, 0, 0)),
            }
        }));
    }
}
