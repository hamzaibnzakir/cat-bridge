using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DynamicIsland.Bridge;

/// <summary>
/// Standard Web Push (RFC 8030) with message encryption (RFC 8291, aes128gcm) and VAPID (RFC 8292).
/// This is how the laptop sends notifications to the Cat Bridge app on your iPhone: the laptop encrypts the
/// message for your phone's key and hands it to Apple's push service, which delivers it. Apple can't read it.
/// </summary>
public static class WebPush
{
    public sealed record Subscription(string Endpoint, string P256dh, string Auth, DateTime Added);

    public static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] UnB64(string s)
    {
        s = s.Trim().Replace(" ", "").Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4));
    }

    // ───────── VAPID keys ─────────

    public static (string publicKey, string privateKey) NewVapidKeys()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(true);
        return (B64(Uncompressed(p.Q)), B64(p.D!));
    }

    static byte[] Uncompressed(ECPoint q) => new byte[] { 4 }.Concat(q.X!).Concat(q.Y!).ToArray();

    static ECPoint Point(byte[] raw)
    {
        if (raw.Length != 65 || raw[0] != 4) throw new ArgumentException("Expected an uncompressed P-256 public key.");
        return new ECPoint { X = raw[1..33], Y = raw[33..65] };
    }

    // ───────── RFC 8291 encryption ─────────

    /// <summary>Encrypts a push message body. salt and senderPrivate are only passed in tests (RFC 8291 §5 vectors).</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] uaPublic, byte[] authSecret, byte[]? salt = null, byte[]? senderPrivate = null)
    {
        if (plaintext.Length > 3993) throw new ArgumentException("Push messages are limited to about 4 KB.");
        salt ??= RandomNumberGenerator.GetBytes(16);

        using var sender = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        if (senderPrivate != null)
        {
            // rebuild the full key from just the private scalar (test path)
            using var tmp = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = senderPrivate });
            sender.ImportParameters(tmp.ExportParameters(true));
        }
        var asPublic = Uncompressed(sender.ExportParameters(false).Q);

        using var receiver = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = Point(uaPublic) });
        var ecdhSecret = sender.DeriveRawSecretAgreement(receiver.PublicKey);

        var prkKey = HMACSHA256.HashData(authSecret, ecdhSecret);
        var keyInfo = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(uaPublic).Concat(asPublic).ToArray();
        var ikm = HMACSHA256.HashData(prkKey, keyInfo.Append((byte)1).ToArray());

        var prk = HMACSHA256.HashData(salt, ikm);
        var cek = HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0\x01"))[..16];
        var nonce = HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0\x01"))[..12];

        var padded = plaintext.Append((byte)2).ToArray(); // single, final record
        var ct = new byte[padded.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Encrypt(nonce, padded, ct, tag);

        using var ms = new MemoryStream();
        ms.Write(salt);
        ms.Write(new byte[] { 0, 0, 0x10, 0 }); // record size 4096
        ms.WriteByte((byte)asPublic.Length);
        ms.Write(asPublic);
        ms.Write(ct);
        ms.Write(tag);
        return ms.ToArray();
    }

    // ───────── RFC 8292 VAPID ─────────

    public static string VapidHeader(string endpoint, string publicKey, string privateKey, string subject, DateTimeOffset? now = null)
    {
        var uri = new Uri(endpoint);
        var aud = $"{uri.Scheme}://{uri.Host}" + (uri.IsDefaultPort ? "" : ":" + uri.Port);
        var exp = (now ?? DateTimeOffset.UtcNow).AddHours(12).ToUnixTimeSeconds();
        var header = B64(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
        var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["aud"] = aud, ["exp"] = exp, ["sub"] = subject }));
        var signingInput = header + "." + claims;

        var pub = UnB64(publicKey);
        using var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = UnB64(privateKey), Q = Point(pub) });
        var sig = ec.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"vapid t={signingInput}.{B64(sig)}, k={publicKey}";
    }

    /// Only real push services are allowed as endpoints, so a bad subscription can't make the laptop call arbitrary URLs.
    public static bool IsAllowedEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var u) || u.Scheme != "https") return false;
        var h = u.Host.ToLowerInvariant();
        return h == "web.push.apple.com" || h.EndsWith(".push.apple.com")
            || h == "fcm.googleapis.com" || h.EndsWith(".push.services.mozilla.com") || h == "updates.push.services.mozilla.com"
            || h.EndsWith(".notify.windows.com");
    }

    public enum SendResult { Sent, Gone, Failed }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<SendResult> Send(Subscription sub, string json, string vapidPublic, string vapidPrivate, string subject, string urgency = "high", int ttlSeconds = 86400)
    {
        if (!IsAllowedEndpoint(sub.Endpoint)) return SendResult.Gone;
        var body = Encrypt(Encoding.UTF8.GetBytes(json), UnB64(sub.P256dh), UnB64(sub.Auth));
        using var req = new HttpRequestMessage(HttpMethod.Post, sub.Endpoint) { Content = new ByteArrayContent(body) };
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        req.Content.Headers.ContentEncoding.Add("aes128gcm");
        req.Headers.TryAddWithoutValidation("TTL", ttlSeconds.ToString());
        req.Headers.TryAddWithoutValidation("Urgency", urgency);
        req.Headers.TryAddWithoutValidation("Authorization", VapidHeader(sub.Endpoint, vapidPublic, vapidPrivate, subject));
        using var res = await Http.SendAsync(req);
        if (res.IsSuccessStatusCode) return SendResult.Sent;
        if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return SendResult.Gone;
        throw new HttpRequestException($"Push service said {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
    }
}
