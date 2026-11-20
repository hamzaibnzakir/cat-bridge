using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

namespace DynamicIsland.Bridge;

/// <summary>Keeps the iPhone's push subscriptions and sends it alerts. Also keeps the "From your laptop" inbox.</summary>
public sealed class PushService
{
    readonly AppSettings _s;
    readonly object _lock = new();
    List<WebPush.Subscription> _subs = new();
    readonly List<InboxItem> _inbox = new();
    static string FilePath => Path.Combine(AppSettings.Dir, "push-subscriptions.json");

    public event Action<string>? Problem; // shown in the island when a push fails

    public PushService(AppSettings s)
    {
        _s = s;
        try { if (File.Exists(FilePath)) _subs = JsonSerializer.Deserialize<List<WebPush.Subscription>>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) { App.Log(ex); }
    }

    /// Number of ways the iPhone can be reached: Bark (if set up) plus Web Push subscriptions.
    public int Count { get { lock (_lock) return _subs.Count + (BarkReady ? 1 : 0); } }
    public bool BarkReady => !string.IsNullOrWhiteSpace(_s.BarkKey);

    void SendBark(string title, string body, string kind, string? url)
    {
        if (!BarkReady) return;
        var msg = new Bark.Message(title, body,
            Group: kind == "alert" ? "Laptop alerts" : kind is "text" or "link" ? "From laptop" : "Laptop",
            Url: kind == "link" ? url : null,
            Level: kind is "ring" or "alert" ? "timeSensitive" : "active",
            Copy: kind == "text" ? body : null,
            Sound: kind == "ring" ? "alarm" : string.IsNullOrWhiteSpace(_s.BarkSound) ? null : _s.BarkSound,
            Call: kind == "ring");
        _ = Task.Run(async () =>
        {
            try { await Bark.Send(_s.BarkServer, _s.BarkKey, msg, _s.BarkEncryptKey, _s.BarkEncryptIv); }
            catch (Exception ex) { App.Log(ex); Problem?.Invoke(ex is HttpRequestException ? ex.Message : "Couldn't reach Bark: " + ex.Message); }
        });
    }

    /// Sends a test straight to Bark and reports the result (for the Cat Center "Send test" button).
    public async Task<string?> TestBark()
    {
        try
        {
            await Bark.Send(_s.BarkServer, _s.BarkKey, new Bark.Message("Meow from your laptop", "Bark alerts from " + Environment.MachineName + " work.", Group: "Laptop"), _s.BarkEncryptKey, _s.BarkEncryptIv);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public void Add(WebPush.Subscription sub)
    {
        lock (_lock)
        {
            _subs.RemoveAll(x => x.Endpoint == sub.Endpoint);
            _subs.Add(sub);
            if (_subs.Count > 5) _subs = _subs.OrderByDescending(x => x.Added).Take(5).ToList();
            Save();
        }
    }

    void Save()
    {
        try { Directory.CreateDirectory(AppSettings.Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(_subs)); }
        catch (Exception ex) { App.Log(ex); }
    }

    public IReadOnlyList<InboxItem> Inbox() { lock (_lock) return _inbox.ToList(); }

    /// Sends to every paired iPhone. kind: info | text | link | ring. Text and links also land in the app's inbox.
    public void Send(string title, string body, string kind = "info", string? url = null, string? tag = null)
    {
        string id = Guid.NewGuid().ToString("N")[..10];
        string openUrl = "/";
        if (kind is "text" or "link")
        {
            lock (_lock)
            {
                _inbox.Insert(0, new InboxItem(id, kind, title, body, url, DateTime.Now));
                if (_inbox.Count > 30) _inbox.RemoveAt(_inbox.Count - 1);
            }
            openUrl = "/#inbox";
        }
        if (kind == "link" && url != null) openUrl = url;

        SendBark(title, body, kind, url);

        List<WebPush.Subscription> subs;
        lock (_lock) subs = _subs.ToList();
        if (subs.Count == 0) return;

        var preview = body.Length > 180 ? body[..180] + "…" : body;
        var json = JsonSerializer.Serialize(new { title, body = preview, tag = tag ?? kind + "-" + id, url = openUrl, kind });
        _ = Task.Run(async () =>
        {
            foreach (var sub in subs)
            {
                try
                {
                    var r = await WebPush.Send(sub, json, _s.VapidPublicKey, _s.VapidPrivateKey, _s.PushSubject,
                        urgency: kind is "ring" or "alert" ? "high" : "normal");
                    if (r == WebPush.SendResult.Gone)
                        lock (_lock) { _subs.RemoveAll(x => x.Endpoint == sub.Endpoint); Save(); }
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                    Problem?.Invoke(ex.Message.StartsWith("Push service said") ? ex.Message : "Couldn't reach Apple's push service. Is the laptop online?");
                }
            }
        });
    }
}
