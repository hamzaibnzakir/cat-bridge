using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicIsland.Bridge;

public sealed record BridgeStatus(
    string Device, int Battery, bool Charging, bool HasBattery,
    string? Title, string? Artist, bool Playing, int TimerSecondsLeft, int ShelfCount);

public sealed record ShelfEntry(int Index, string Name, long Size, bool IsFolder);

/// <summary>Something the laptop sent to the iPhone (shown in the app's "From your laptop" list).</summary>
public sealed record InboxItem(string Id, string Kind, string Title, string Text, string? Url, DateTime Time);

/// <summary>What the iPhone app learns when it connects.</summary>
public sealed record HelloInfo(string Device, string Version, string? TailscaleIp, int Port, string[] Features);
/// <summary>A notification the laptop received (for the iPhone app's "Laptop alerts" list).</summary>
/// <summary>A source of screen frames for the Remote tab (ScreenGrabber on Windows).</summary>
public interface IScreenFeed : IDisposable
{
    /// The screen's size in real pixels.
    (int Width, int Height) Size();
    /// A JPEG at most maxWidth wide, or null when nothing changed since the last one.
    byte[]? Next(int maxWidth, int quality);
    /// Send the next frame even if nothing changed.
    void Reset();
}
public sealed record AlertEntry(string App, string Title, string Body, DateTime Time, bool FromPhone);

/// <summary>Everything the bridge asks the island to do. Called on thread-pool threads.</summary>
public interface IBridgeHost
{
    BridgeStatus Status();
    string? GetClipboard();
    void SetClipboard(string text);
    void OpenLink(string url);
    string ReceiveFolder { get; }
    void FileReceived(string path);
    IReadOnlyList<string> ShelfPaths();
    void Media(string action);
    void Focus(int minutes);
    void Lock();
    void Ping();

    // iPhone → laptop (Shortcuts automations)
    void PhoneNotification(string app, string title, string body);
    void PhoneStatus(int? battery, bool? charging, string? focus);
    void StopFocus();

    // laptop → iPhone (Web Push)
    string VapidPublicKey { get; }
    void AddPushSubscription(WebPush.Subscription sub);
    void TestPush();
    IReadOnlyList<InboxItem> Inbox();

    // iPhone unlock + wake (results are (status code, reply body))
    Task<(int status, object body)> UnlockChallenge();
    Task<(int status, object body)> UnlockApprove(string nonce, string signature);
    (int status, object body) UnlockRegister(string name, string publicKey);
    object UnlockRegistration(string publicKey);
    void Wake();
    object NetInfo();
    /// One remote-control command from the iPhone's trackpad/keyboard (see InputInjector). Returns a reply line or null.
    string? Input(string command);
    string InputHello();
    /// The iPhone wants to watch the screen. Returns null if allowed, otherwise the reason it isn't.
    string? ScreenStart();
    void ScreenStop();
    IScreenFeed OpenScreen();
    /// False once the person turns screen viewing off; open viewers are then closed.
    bool ScreenAllowed { get; }
    bool SessionLocked { get; }
    Task<object> Locate();
    void Alarm(bool on);
    void LostMode(bool on, string? message);
    object FocusStats();

    // native iPhone app
    HelloInfo Hello();
    IReadOnlyList<AlertEntry> Alerts();
}

/// <summary>
/// A small HTTP/1.1 server that listens on 127.0.0.1 only. Tailscale Serve puts HTTPS in front of it and
/// makes it reachable from your own tailnet devices; nothing on your Wi-Fi or the internet can reach it.
/// Every API call also needs the pairing key. Written on raw sockets (not http.sys) so the Host header
/// Tailscale forwards never matters and no admin rights are needed.
/// </summary>
public sealed class BridgeServer : IDisposable
{
    public const int DefaultPort = 47800;
    const long MaxUpload = 4L * 1024 * 1024 * 1024;

    readonly IBridgeHost _host;
    readonly byte[] _key;
    readonly int _port;
    readonly List<TcpListener> _listeners = new();
    CancellationTokenSource? _cts;
    public Action<Exception>? Log;
    /// Addresses being listened on right now (always includes 127.0.0.1).
    public IReadOnlyList<IPAddress> Bound => _listeners.Select(l => ((IPEndPoint)l.LocalEndpoint).Address).ToList();

    public BridgeServer(IBridgeHost host, string key, int port = DefaultPort)
    {
        _host = host; _key = Encoding.UTF8.GetBytes(key); _port = port;
    }

    public static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    /// Listens on 127.0.0.1 plus the given addresses (the laptop's Tailscale IPs). Never on 0.0.0.0, so your
    /// Wi-Fi or the internet can't reach it.
    public void Start(IEnumerable<IPAddress>? extra = null)
    {
        _cts = new CancellationTokenSource();
        Listen(IPAddress.Loopback, required: true);
        foreach (var a in extra ?? Array.Empty<IPAddress>()) Listen(a, required: false);
    }

    /// Adds a newly appeared address (e.g. Tailscale connected after the app started).
    public bool Listen(IPAddress a, bool required = false)
    {
        if (_cts == null || _listeners.Any(l => ((IPEndPoint)l.LocalEndpoint).Address.Equals(a))) return false;
        var l = new TcpListener(a, _port);
        try { l.Start(); }
        catch (Exception ex) { if (required) throw; Log?.Invoke(ex); return false; }
        lock (_listeners) _listeners.Add(l);
        _ = AcceptLoop(l, _cts.Token);
        return true;
    }

    async Task AcceptLoop(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) when (!listener.Server.IsBound) { lock (_listeners) _listeners.Remove(listener); return; } // address went away
            catch (Exception ex) { Log?.Invoke(ex); await Task.Delay(200, CancellationToken.None); continue; }
            _ = Task.Run(() => Handle(c), CancellationToken.None);
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        lock (_listeners) { foreach (var l in _listeners) { try { l.Stop(); } catch { } } _listeners.Clear(); }
    }

    // ───────── HTTP plumbing ─────────

    sealed class Req
    {
        public string Method = "", Path = "", Query = "";
        public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
        public Stream Body = Stream.Null;
        public long Length = -1;
        public string Q(string k)
        {
            foreach (var part in Query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int i = part.IndexOf('=');
                var name = Uri.UnescapeDataString((i < 0 ? part : part[..i]).Replace('+', ' '));
                if (name == k) return i < 0 ? "" : Uri.UnescapeDataString(part[(i + 1)..].Replace('+', ' '));
            }
            return "";
        }
    }

    async Task Handle(TcpClient client)
    {
        using var _ = client;
        client.ReceiveTimeout = client.SendTimeout = 120_000;
        var net = client.GetStream();
        try
        {
            var req = await ReadHead(net);
            if (req == null) return;
            await Route(req, net);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { }
        catch (Exception ex)
        {
            Log?.Invoke(ex);
            try { await Json(net, 500, new { error = "Something went wrong on the laptop." }); } catch { }
        }
    }

    static async Task<Req?> ReadHead(NetworkStream s)
    {
        // read until \r\n\r\n (headers capped at 32 KB), keep any extra bytes as the start of the body
        var buf = new byte[32 * 1024];
        int len = 0, end = -1;
        while (end < 0)
        {
            if (len == buf.Length) return null;
            int n = await s.ReadAsync(buf.AsMemory(len, buf.Length - len));
            if (n == 0) return null;
            len += n;
            for (int i = Math.Max(0, len - n - 3); i <= len - 4; i++)
                if (buf[i] == '\r' && buf[i + 1] == '\n' && buf[i + 2] == '\r' && buf[i + 3] == '\n') { end = i; break; }
        }
        var head = Encoding.ASCII.GetString(buf, 0, end).Split("\r\n");
        var first = head[0].Split(' ');
        if (first.Length < 2) return null;
        var r = new Req { Method = first[0].ToUpperInvariant() };
        var target = first[1];
        int q = target.IndexOf('?');
        r.Path = Uri.UnescapeDataString(q < 0 ? target : target[..q]);
        r.Query = q < 0 ? "" : target[(q + 1)..];
        foreach (var line in head.Skip(1))
        {
            int c = line.IndexOf(':');
            if (c > 0) r.Headers[line[..c].Trim()] = line[(c + 1)..].Trim();
        }
        var leftover = new MemoryStream(buf, end + 4, len - end - 4, writable: false);
        Stream body = new PrefixStream(leftover, s);
        if (r.Headers.TryGetValue("Transfer-Encoding", out var te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            body = new ChunkedStream(body);
        else if (r.Headers.TryGetValue("Content-Length", out var cl) && long.TryParse(cl, out var l))
        {
            r.Length = l;
            body = new LimitStream(body, l);
        }
        else body = Stream.Null;
        r.Body = body;
        return r;
    }

    static async Task Send(Stream s, int code, string type, byte[] body, string? extraHeaders = null)
    {
        var reason = code switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 405 => "Method Not Allowed", 413 => "Payload Too Large", _ => "Error" };
        var h = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\n" +
                "X-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n" + (extraHeaders ?? "") + "\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(h));
        await s.WriteAsync(body);
        await s.FlushAsync();
    }

    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static Task Json(Stream s, int code, object o) => Send(s, code, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(o, JsonOpts));

    bool Authorized(Req r)
    {
        var given = r.Headers.TryGetValue("X-Cat-Key", out var h) ? h : r.Q("k");
        var g = Encoding.UTF8.GetBytes(given ?? "");
        return g.Length == _key.Length && CryptographicOperations.FixedTimeEquals(g, _key);
    }

    static async Task<System.Net.WebSockets.WebSocket?> AcceptWebSocket(Req r, NetworkStream s)
    {
        if (!r.Headers.TryGetValue("Sec-WebSocket-Key", out var wsKey) || wsKey.Length > 100) { await Json(s, 400, new { error = "Bad WebSocket request." }); return null; }
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(wsKey.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n");
        await s.WriteAsync(head);
        return System.Net.WebSockets.WebSocket.CreateFromStream(s, new System.Net.WebSockets.WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(15) });
    }

    /// <summary>
    /// Live view of the laptop screen for the iPhone's Remote tab. The phone pulls frames: each "next" it sends
    /// allows one more JPEG (binary message), so a slow connection never builds up a backlog and the picture stays live.
    /// Frames are only sent when the screen changed. Text messages from the laptop: "screen W H" (real pixels, also sent
    /// when the resolution changes), "locked" / "unlocked", "off reason". From the phone: "next", "q maxWidth quality fps".
    /// </summary>
    async Task RunScreenSocket(Req r, NetworkStream s)
    {
        using var ws = await AcceptWebSocket(r, s);
        if (ws == null) return;
        var sendLock = new SemaphoreSlim(1, 1);
        async Task Say(string text)
        {
            await sendLock.WaitAsync();
            try { await ws.SendAsync(Encoding.UTF8.GetBytes(text), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None); }
            finally { sendLock.Release(); }
        }

        var why = _host.ScreenStart();
        if (why != null)
        {
            await Say("off " + why);
            try { await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "off", CancellationToken.None); } catch { }
            return;
        }
        try
        {
            int credits = 0, maxWidth = 1280, quality = 60, fps = 15;
            using var wake = new SemaphoreSlim(0);
            using var stop = new CancellationTokenSource();

            var reader = Task.Run(async () =>
            {
                var buf = new byte[1024];
                try
                {
                    while (ws.State == System.Net.WebSockets.WebSocketState.Open)
                    {
                        var res = await ws.ReceiveAsync(buf, stop.Token);
                        if (res.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                        foreach (var line in Encoding.UTF8.GetString(buf, 0, res.Count).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var a = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (a.Length == 0) continue;
                            if (a[0] == "next") { if (Interlocked.Increment(ref credits) > 3) Interlocked.Exchange(ref credits, 3); try { wake.Release(); } catch { } }
                            else if (a[0] == "q" && a.Length >= 4 && int.TryParse(a[1], out var w) && int.TryParse(a[2], out var q) && int.TryParse(a[3], out var f))
                            { maxWidth = Math.Clamp(w, 320, 3840); quality = Math.Clamp(q, 20, 95); fps = Math.Clamp(f, 1, 30); }
                        }
                    }
                }
                catch { }
                finally { try { stop.Cancel(); } catch { } }
            });

            using var grab = _host.OpenScreen();
            var (sw, sh) = grab.Size();
            await Say($"screen {sw} {sh}");
            bool lockedSaid = false;
            var lastSent = DateTime.MinValue;
            int lastW = maxWidth, lastQ = quality;

            while (!stop.IsCancellationRequested && ws.State == System.Net.WebSockets.WebSocketState.Open)
            {
                if (Volatile.Read(ref credits) <= 0) { try { await wake.WaitAsync(1000, stop.Token); } catch { break; } continue; }

                if (!_host.ScreenAllowed) { await Say("off Screen viewing was turned off on the laptop."); break; }
                if (_host.SessionLocked)
                {
                    if (!lockedSaid) { await Say("locked"); lockedSaid = true; }
                    try { await Task.Delay(500, stop.Token); } catch { break; }
                    continue;
                }
                if (lockedSaid) { await Say("unlocked"); lockedSaid = false; grab.Reset(); }

                var gap = TimeSpan.FromMilliseconds(1000.0 / fps) - (DateTime.UtcNow - lastSent);
                if (gap > TimeSpan.Zero) { try { await Task.Delay(gap, stop.Token); } catch { break; } }

                var size = grab.Size();
                if (size != (sw, sh)) { (sw, sh) = size; await Say($"screen {sw} {sh}"); grab.Reset(); }
                if (maxWidth != lastW || quality != lastQ) { lastW = maxWidth; lastQ = quality; grab.Reset(); }

                byte[]? jpg = null;
                try { jpg = grab.Next(maxWidth, quality); } catch (Exception ex) { Log?.Invoke(ex); }
                if (jpg == null) { try { await Task.Delay(35, stop.Token); } catch { break; } continue; }

                await sendLock.WaitAsync();
                try { await ws.SendAsync(jpg, System.Net.WebSockets.WebSocketMessageType.Binary, true, CancellationToken.None); }
                finally { sendLock.Release(); }
                Interlocked.Decrement(ref credits);
                lastSent = DateTime.UtcNow;
            }
            if (ws.State == System.Net.WebSockets.WebSocketState.Open)
                try { await ws.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
            try { await reader.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        }
        catch (Exception ex) when (ex is IOException or System.Net.WebSockets.WebSocketException or ObjectDisposedException) { }
        finally { _host.ScreenStop(); }
    }

    async Task RunInputSocket(Req r, NetworkStream s)
    {
        using var ws = await AcceptWebSocket(r, s);
        if (ws == null) return;
        async Task Say(string text) => await ws.SendAsync(Encoding.UTF8.GetBytes(text), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        await Say(_host.InputHello());
        var buf = new byte[8192];
        while (ws.State == System.Net.WebSockets.WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            System.Net.WebSockets.WebSocketReceiveResult res;
            do
            {
                res = await ws.ReceiveAsync(buf, CancellationToken.None);
                if (res.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) { try { await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { } return; }
                ms.Write(buf, 0, res.Count);
                if (ms.Length > 64 * 1024) return;
            } while (!res.EndOfMessage);
            foreach (var line in Encoding.UTF8.GetString(ms.ToArray()).Split('\n'))
            {
                if (line.Length == 0) continue;
                string? reply = null;
                try { reply = _host.Input(line); } catch (Exception ex) { Log?.Invoke(ex); }
                if (reply != null) await Say(reply);
            }
        }
    }

    static async Task<string> ReadText(Req r, int max = 1_000_000)
    {
        if (r.Length > max) throw new InvalidDataException("too big");
        using var ms = new MemoryStream();
        var buf = new byte[16384];
        int n;
        while ((n = await r.Body.ReadAsync(buf)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > max) throw new InvalidDataException("too big");
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ───────── routes ─────────

    async Task Route(Req r, NetworkStream s)
    {
        // the web app itself (no key needed to load the page; the key lives in the phone's browser)
        if (r.Method == "GET" && !r.Path.StartsWith("/api/", StringComparison.Ordinal))
        {
            var (name, type) = r.Path switch
            {
                "/" or "/index.html" => ("index.html", "text/html; charset=utf-8"),
                "/manifest.json" => ("manifest.json", "application/manifest+json"),
                "/icon-180.png" => ("icon-180.png", "image/png"),
                "/icon-512.png" => ("icon-512.png", "image/png"),
                "/sw.js" => ("sw.js", "application/javascript; charset=utf-8"),
                _ => ("", ""),
            };
            var bytes = name.Length == 0 ? null : Asset(name);
            if (bytes == null) { await Send(s, 404, "text/plain", Encoding.UTF8.GetBytes("Not found")); return; }
            await Send(s, 200, type, bytes,
                name == "index.html" ? "Content-Security-Policy: default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'; worker-src 'self'\r\n" : name == "sw.js" ? "Service-Worker-Allowed: /\r\n" : null);
            return;
        }

        if (!Authorized(r)) { await Json(s, 401, new { error = "This phone isn't paired. Scan the QR code on your laptop again." }); return; }

        // live trackpad + keyboard: a WebSocket that stays open while the Remote screen is showing
        if (r.Method == "GET" && r.Path == "/api/input" && r.Headers.TryGetValue("Upgrade", out var up) && up.Equals("websocket", StringComparison.OrdinalIgnoreCase))
        {
            await RunInputSocket(r, s);
            return;
        }

        // live view of the screen (Remote tab, top half)
        if (r.Method == "GET" && r.Path == "/api/screen" && r.Headers.TryGetValue("Upgrade", out var up2) && up2.Equals("websocket", StringComparison.OrdinalIgnoreCase))
        {
            await RunScreenSocket(r, s);
            return;
        }

        switch (r.Method, r.Path)
        {
            case ("GET", "/api/status"):
                await Json(s, 200, _host.Status());
                return;

            case ("GET", "/api/clipboard"):
                await Json(s, 200, new { text = _host.GetClipboard() ?? "" });
                return;

            case ("POST", "/api/clipboard"):
            {
                var text = await ReadText(r);
                if (text.Length == 0) { await Json(s, 400, new { error = "Nothing to copy." }); return; }
                _host.SetClipboard(text);
                await Json(s, 200, new { ok = true });
                return;
            }

            case ("POST", "/api/link"):
            {
                var url = (await ReadText(r, 8192)).Trim();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
                {
                    await Json(s, 400, new { error = "That doesn't look like a web link." });
                    return;
                }
                _host.OpenLink(u.AbsoluteUri);
                await Json(s, 200, new { ok = true });
                return;
            }

            case ("POST", "/api/files"):
            {
                var name = SafeName(r.Headers.TryGetValue("X-File-Name", out var fn) ? Uri.UnescapeDataString(fn) : r.Q("name"));
                if (r.Length > MaxUpload) { await Json(s, 413, new { error = "That file is bigger than 4 GB." }); return; }
                Directory.CreateDirectory(_host.ReceiveFolder);
                var path = Unique(Path.Combine(_host.ReceiveFolder, name));
                var tmp = path + ".part";
                long total = 0;
                try
                {
                    await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, true))
                    {
                        var buf = new byte[1 << 16];
                        int n;
                        while ((n = await r.Body.ReadAsync(buf)) > 0)
                        {
                            total += n;
                            if (total > MaxUpload) throw new InvalidDataException("too big");
                            await fs.WriteAsync(buf.AsMemory(0, n));
                        }
                    }
                    if (r.Length >= 0 && total != r.Length) throw new IOException("upload was cut off");
                    File.Move(tmp, path);
                }
                catch { try { File.Delete(tmp); } catch { } throw; }
                _host.FileReceived(path);
                await Json(s, 200, new { ok = true, name = Path.GetFileName(path), size = total });
                return;
            }

            case ("GET", "/api/shelf"):
            {
                var list = _host.ShelfPaths().Select((p, i) =>
                {
                    bool dir = Directory.Exists(p);
                    long size = !dir && File.Exists(p) ? new FileInfo(p).Length : 0;
                    return new ShelfEntry(i, Path.GetFileName(p.TrimEnd('\\', '/')), size, dir);
                }).ToList();
                await Json(s, 200, list);
                return;
            }

            case ("GET", "/api/shelf/file"):
            {
                var paths = _host.ShelfPaths();
                if (!int.TryParse(r.Q("i"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) || i < 0 || i >= paths.Count || !File.Exists(paths[i]))
                {
                    await Json(s, 404, new { error = "That file isn't on the shelf any more." });
                    return;
                }
                await SendFile(s, paths[i]);
                return;
            }

            case ("POST", "/api/media"):
            {
                var action = r.Q("a");
                if (action is not ("toggle" or "next" or "prev")) { await Json(s, 400, new { error = "Unknown action." }); return; }
                _host.Media(action);
                await Json(s, 200, new { ok = true });
                return;
            }

            case ("POST", "/api/focus"):
            {
                if (r.Q("m") == "0") { _host.StopFocus(); await Json(s, 200, new { ok = true }); return; }
                if (!int.TryParse(r.Q("m"), out int m) || m < 1 || m > 240) m = 25;
                _host.Focus(m);
                await Json(s, 200, new { ok = true });
                return;
            }

            case ("POST", "/api/lock"):
                _host.Lock();
                await Json(s, 200, new { ok = true });
                return;

            case ("POST", "/api/ping"):
                _host.Ping();
                await Json(s, 200, new { ok = true });
                return;

            case ("POST", "/api/notify"):
            {
                var o = await ReadJsonObject(r);
                string app = Str(o, "app", "App", "application") ?? r.Q("app");
                string title = Str(o, "title", "Title") ?? "";
                string sub = Str(o, "subtitle", "Subtitle") ?? "";
                string body = Str(o, "body", "Body", "text", "message") ?? "";
                if (o == null && title.Length == 0) title = (await ReadText(r, 8192)).Trim();
                if (sub.Length > 0) body = body.Length > 0 ? sub + " · " + body : sub;
                if (title.Length == 0 && body.Length == 0) { await Json(s, 400, new { error = "Send a title or body." }); return; }
                _host.PhoneNotification(Clip(app, 40), Clip(title, 200), Clip(body, 600));
                await Json(s, 200, new { ok = true });
                return;
            }

            case ("GET", "/api/unlock/challenge"):
            {
                var (code, body) = await _host.UnlockChallenge();
                await Json(s, code, body);
                return;
            }

            case ("POST", "/api/unlock/approve"):
            {
                var o = await ReadJsonObject(r);
                var nonce = Str(o, "nonce"); var sig = Str(o, "signature");
                if (nonce == null || sig == null || nonce.Length > 200 || sig.Length > 400) { await Json(s, 400, new { error = "Send nonce and signature." }); return; }
                var (code, body) = await _host.UnlockApprove(nonce, sig);
                await Json(s, code, body);
                return;
            }

            case ("POST", "/api/unlock/register"):
            {
                var o = await ReadJsonObject(r);
                var key = Str(o, "publicKey"); var name = Str(o, "name") ?? "iPhone";
                if (key == null || key.Length > 200) { await Json(s, 400, new { error = "Send publicKey." }); return; }
                var (code, body) = _host.UnlockRegister(Clip(name, 60), key);
                await Json(s, code, body);
                return;
            }

            case ("GET", "/api/unlock/register"):
                await Json(s, 200, _host.UnlockRegistration(r.Q("key") ?? ""));
                return;

            case ("POST", "/api/wake"):
                _host.Wake();
                await Json(s, 200, new { ok = true });
                return;

            case ("GET", "/api/locate"):
                await Json(s, 200, await _host.Locate());
                return;

            case ("POST", "/api/alarm"):
                _host.Alarm(r.Q("on") != "0");
                await Json(s, 200, new { ok = true });
                return;

            case ("POST", "/api/lost"):
            {
                var o = await ReadJsonObject(r);
                var on = (Str(o, "on") ?? "true").Trim().ToLowerInvariant() is "true" or "1" or "yes";
                _host.LostMode(on, Str(o, "message") is string m ? Clip(m, 200) : null);
                await Json(s, 200, new { ok = true });
                return;
            }

            case ("GET", "/api/focus/stats"):
                await Json(s, 200, _host.FocusStats());
                return;

            case ("GET", "/api/netinfo"):
                await Json(s, 200, _host.NetInfo());
                return;

            case ("POST", "/api/phone"):
            {
                var o = await ReadJsonObject(r);
                int? battery = null;
                var b = Str(o, "battery", "Battery", "level");
                if (b != null && double.TryParse(b.Replace("%", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var bv))
                    battery = (int)Math.Round(Math.Clamp(bv <= 1 && b.Contains('.') ? bv * 100 : bv, 0, 100));
                bool? charging = Str(o, "charging", "Charging") is string c
                    ? c.Trim().ToLowerInvariant() is "true" or "yes" or "1" or "on" or "charging" : null;
                var focus = Str(o, "focus", "Focus");
                _host.PhoneStatus(battery, charging, focus == null ? null : Clip(focus, 40));
                await Json(s, 200, new { ok = true });
                return;
            }

            case ("GET", "/api/push/key"):
                await Json(s, 200, new { key = _host.VapidPublicKey });
                return;

            case ("POST", "/api/push/subscribe"):
            {
                var o = await ReadJsonObject(r);
                string? endpoint = Str(o, "endpoint");
                string? p256 = null, auth = null;
                if (o is JsonElement e && e.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Object)
                {
                    p256 = Str(keys, "p256dh"); auth = Str(keys, "auth");
                }
                bool valid = endpoint != null && WebPush.IsAllowedEndpoint(endpoint) && p256 != null && auth != null;
                try { valid = valid && WebPush.UnB64(p256!).Length == 65 && WebPush.UnB64(auth!).Length == 16; } catch { valid = false; }
                if (!valid) { await Json(s, 400, new { error = "That notification subscription doesn't look right." }); return; }
                _host.AddPushSubscription(new WebPush.Subscription(endpoint!, p256!, auth!, DateTime.Now));
                await Json(s, 200, new { ok = true });
                return;
            }

            case ("POST", "/api/push/test"):
                _host.TestPush();
                await Json(s, 200, new { ok = true });
                return;

            case ("GET", "/api/inbox"):
                await Json(s, 200, _host.Inbox());
                return;

            case ("GET", "/api/hello"):
                await Json(s, 200, _host.Hello());
                return;

            case ("GET", "/api/alerts"):
                await Json(s, 200, _host.Alerts());
                return;
        }
        await Json(s, 404, new { error = "Unknown request." });
    }

    static string Clip(string? v, int max) { v = (v ?? "").Trim(); return v.Length > max ? v[..max] + "…" : v; }

    static async Task<JsonElement?> ReadJsonObject(Req r)
    {
        var text = await ReadText(r, 64 * 1024);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException) { r.Body = new MemoryStream(Encoding.UTF8.GetBytes(text)); return null; }
    }

    /// Shortcuts sends values as strings, numbers or booleans depending on the action, so accept any of them.
    static string? Str(JsonElement? o, params string[] names)
    {
        if (o is not JsonElement e || e.ValueKind != JsonValueKind.Object) return null;
        foreach (var n in names)
            if (e.TryGetProperty(n, out var v))
                return v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null,
                };
        return null;
    }

    static async Task SendFile(Stream s, string path)
    {
        var fi = new FileInfo(path);
        var name = fi.Name;
        var ascii = new string(name.Select(ch => ch < 32 || ch > 126 || ch == '"' || ch == '\\' ? '_' : ch).ToArray());
        var h = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n" +
                $"Content-Length: {fi.Length}\r\nContent-Disposition: attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}\r\n" +
                "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(h));
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, true);
        await fs.CopyToAsync(s, 1 << 16);
        await s.FlushAsync();
    }

    public static string SafeName(string? name)
    {
        name = Path.GetFileName((name ?? "").Replace('\\', '/').Split('/').Last()).Trim().Trim('.');
        foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '|', '?', '*' })) name = name.Replace(c, '_');
        name = new string(name.Where(ch => ch >= 32).ToArray());
        var stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))) name = "_" + name;
        if (name.Length > 150) name = name[..100] + Path.GetExtension(name)[..Math.Min(20, Path.GetExtension(name).Length)];
        return name.Length == 0 ? $"iPhone-{DateTime.Now:yyyyMMdd-HHmmss}" : name;
    }

    static string Unique(string path)
    {
        if (!File.Exists(path) && !File.Exists(path + ".part")) return path;
        var dir = Path.GetDirectoryName(path)!; var stem = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p) && !File.Exists(p + ".part")) return p;
        }
    }

    static readonly Dictionary<string, byte[]?> Assets = new();
    static byte[]? Asset(string name)
    {
        lock (Assets)
        {
            if (Assets.TryGetValue(name, out var b)) return b;
            using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("bridge/" + name);
            if (st == null) return Assets[name] = null;
            using var ms = new MemoryStream();
            st.CopyTo(ms);
            return Assets[name] = ms.ToArray();
        }
    }

    // ───────── body streams ─────────

    sealed class PrefixStream : Stream
    {
        readonly Stream _a, _b;
        public PrefixStream(Stream a, Stream b) { _a = a; _b = b; }
        public override int Read(byte[] buffer, int offset, int count) { int n = _a.Read(buffer, offset, count); return n > 0 ? n : _b.Read(buffer, offset, count); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { int n = await _a.ReadAsync(buffer, ct); return n > 0 ? n : await _b.ReadAsync(buffer, ct); }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    sealed class LimitStream : Stream
    {
        readonly Stream _s; long _left;
        public LimitStream(Stream s, long n) { _s = s; _left = n; }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_left <= 0) return 0;
            int n = await _s.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _left)], ct);
            _left -= n;
            return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    sealed class ChunkedStream : Stream
    {
        readonly Stream _s; long _left; bool _done;
        public ChunkedStream(Stream s) { _s = s; }
        async Task<string> Line(CancellationToken ct)
        {
            var sb = new StringBuilder(); var one = new byte[1];
            while (true)
            {
                if (await _s.ReadAsync(one, ct) == 0) throw new IOException("chunked body ended early");
                if (one[0] == '\n') return sb.ToString().TrimEnd('\r');
                if (sb.Length > 1024) throw new IOException("bad chunk header");
                sb.Append((char)one[0]);
            }
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_done) return 0;
            if (_left == 0)
            {
                var size = (await Line(ct)).Split(';')[0].Trim();
                _left = long.Parse(size, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (_left == 0) { while ((await Line(ct)).Length > 0) { } _done = true; return 0; }
            }
            int n = await _s.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _left)], ct);
            if (n == 0) throw new IOException("chunked body ended early");
            _left -= n;
            if (_left == 0) await Line(ct);
            return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
