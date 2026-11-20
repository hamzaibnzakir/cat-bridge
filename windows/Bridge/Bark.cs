using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DynamicIsland.Bridge;

/// <summary>
/// Sends alerts to the free Bark app on your iPhone (github.com/Finb/Bark). Bark owns the Apple push
/// certificate, so your sideloaded Cat Bridge app doesn't need a paid developer account for pop-ups.
/// With encryption on (recommended), the message is AES-CBC encrypted with the key and IV you set in Bark,
/// so the Bark server only relays ciphertext. You can also run your own bark-server (e.g. on your VPS).
/// </summary>
public static class Bark
{
    public sealed record Message(string Title, string Body, string Group = "Laptop", string? Url = null,
        string Level = "active", string? Copy = null, string? Sound = null, bool Call = false, string? Icon = null);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static Dictionary<string, object> Fields(Message m)
    {
        var d = new Dictionary<string, object> { ["title"] = m.Title, ["body"] = m.Body.Length == 0 ? " " : m.Body, ["group"] = m.Group, ["level"] = m.Level };
        if (!string.IsNullOrEmpty(m.Url)) d["url"] = m.Url;
        if (!string.IsNullOrEmpty(m.Copy)) d["copy"] = m.Copy;
        if (!string.IsNullOrEmpty(m.Sound)) d["sound"] = m.Sound;
        if (!string.IsNullOrEmpty(m.Icon)) d["icon"] = m.Icon;
        if (m.Call) d["call"] = "1";
        return d;
    }

    /// AES-CBC with PKCS7 padding, key and IV taken as the literal text typed in Bark's settings (16/24/32 and 16 characters).
    public static string Encrypt(string json, string key, string iv)
    {
        var k = Encoding.UTF8.GetBytes(key);
        var v = Encoding.UTF8.GetBytes(iv);
        if (k.Length is not (16 or 24 or 32)) throw new ArgumentException("The Bark encryption key must be 16, 24 or 32 characters.");
        if (v.Length != 16) throw new ArgumentException("The Bark IV must be 16 characters.");
        using var aes = Aes.Create();
        aes.Key = k;
        return Convert.ToBase64String(aes.EncryptCbc(Encoding.UTF8.GetBytes(json), v, PaddingMode.PKCS7));
    }

    /// server: e.g. https://api.day.app (default) or your own bark-server. deviceKey: the key Bark shows you.
    public static async Task Send(string server, string deviceKey, Message m, string? encKey = null, string? encIv = null)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) throw new InvalidOperationException("Bark isn't set up yet.");
        server = (string.IsNullOrWhiteSpace(server) ? "https://api.day.app" : server.Trim()).TrimEnd('/');
        if (!Uri.TryCreate(server, UriKind.Absolute, out var su) || (su.Scheme != "https" && su.Scheme != "http"))
            throw new InvalidOperationException("The Bark server address isn't a web address.");
        var url = $"{server}/{Uri.EscapeDataString(deviceKey.Trim())}";

        HttpContent content;
        if (!string.IsNullOrEmpty(encKey))
        {
            var fields = Fields(m);
            var cipher = Encrypt(JsonSerializer.Serialize(fields), encKey, encIv ?? "");
            content = new FormUrlEncodedContent(new Dictionary<string, string> { ["ciphertext"] = cipher, ["iv"] = encIv ?? "" });
        }
        else
        {
            var f = Fields(m);
            content = new StringContent(JsonSerializer.Serialize(f), Encoding.UTF8, "application/json");
        }
        using var res = await Http.PostAsync(url, content);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Bark said {(int)res.StatusCode}: {(text.Length > 160 ? text[..160] : text)}");
        // bark-server answers {"code":200,...}; anything else is a problem worth showing
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("code", out var c) && c.TryGetInt32(out int code) && code != 200)
                throw new HttpRequestException("Bark said: " + (doc.RootElement.TryGetProperty("message", out var msg) ? msg.GetString() : code.ToString()));
        }
        catch (JsonException) { }
    }
}
