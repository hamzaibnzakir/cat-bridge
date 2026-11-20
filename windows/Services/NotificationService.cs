using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland;

public sealed class NotificationItem
{
    public uint Id;
    public string App = "", Title = "", Body = "", Aumid = "";
    public ImageSource? Logo;
    public DateTime Time;
}

/// <summary>
/// Reads Windows toast notifications (the same listener PILLAR uses). Subscribes to change events when Windows allows it,
/// otherwise polls — unpackaged apps often get "Element not found" on the event.
/// </summary>
public sealed class NotificationService
{
    readonly Dispatcher _ui;
    UserNotificationListener? _l;
    readonly HashSet<uint> _seen = new();
    bool _polling;
    public string Status { get; private set; } = "off"; // off | allowed | denied | unsupported
    public bool EventsOk { get; private set; }
    public List<NotificationItem> History { get; } = new();
    public event Action<NotificationItem>? Added;

    public NotificationService(Dispatcher ui) => _ui = ui;

    public async Task InitAsync()
    {
        try
        {
            _l = UserNotificationListener.Current;
            var st = await _l.RequestAccessAsync();
            if (st != UserNotificationListenerAccessStatus.Allowed) { Status = "denied"; return; }

            // Existing notifications go to history quietly.
            foreach (var n in await _l.GetNotificationsAsync(NotificationKinds.Toast))
            {
                _seen.Add(n.Id);
                var item = await Extract(n);
                if (item != null) History.Add(item);
            }
            History.Sort((a, b) => b.Time.CompareTo(a.Time));
            Status = "allowed"; // only after seeding, so old notifications never pop up as new

            for (int i = 0; i < 3 && !EventsOk; i++)
            {
                try
                {
                    _l.NotificationChanged += (sender, e) =>
                    {
                        if (e.ChangeKind == UserNotificationChangedKind.Added) _ui.InvokeAsync(() => _ = PollAsync());
                    };
                    EventsOk = true;
                }
                catch { await Task.Delay(500); }
            }
        }
        catch (Exception ex) { Status = "unsupported"; App.Log(ex); }
    }

    public async Task PollAsync(bool suppressNative = false)
    {
        if (_l == null || Status != "allowed" || _polling) return;
        _polling = true;
        try
        {
            var list = await _l.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in list.OrderBy(n => n.CreationTime))
            {
                if (!_seen.Add(n.Id)) continue;
                var item = await Extract(n);
                if (item == null) continue;
                History.Insert(0, item);
                if (History.Count > 50) History.RemoveAt(History.Count - 1);
                Added?.Invoke(item);
                if (suppressNative) { try { _l.RemoveNotification(n.Id); } catch { } }
            }
        }
        catch (Exception ex) { App.Log(ex); }
        finally { _polling = false; }
    }

    static async Task<NotificationItem?> Extract(UserNotification n)
    {
        try
        {
            var item = new NotificationItem { Id = n.Id, Time = n.CreationTime.LocalDateTime };
            try { item.App = n.AppInfo.DisplayInfo.DisplayName; item.Aumid = n.AppInfo.AppUserModelId; } catch { }
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new();
            item.Title = texts.FirstOrDefault() ?? item.App;
            item.Body = string.Join(" · ", texts.Skip(1));
            try
            {
                var stream = await n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(48, 48)).OpenReadAsync();
                using var s = stream.AsStreamForRead();
                var ms = new MemoryStream();
                await s.CopyToAsync(ms); ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 48; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
                item.Logo = bi;
            }
            catch { }
            return item;
        }
        catch { return null; }
    }

    public void Dismiss(NotificationItem item)
    {
        History.Remove(item);
        try { _l?.RemoveNotification(item.Id); } catch { }
    }

    public void ClearHistory() => History.Clear();

    public static void OpenApp(NotificationItem item)
    {
        if (string.IsNullOrEmpty(item.Aumid)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{item.Aumid}") { UseShellExecute = true }); } catch { }
    }

    public static void OpenSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true }); } catch { }
    }
}
