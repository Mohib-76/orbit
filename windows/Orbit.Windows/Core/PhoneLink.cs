using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

namespace Orbit.Core;

/// <summary>
/// Wire format (borrowed from KDE Connect): one JSON object per line, {"type": "...", "body": {...}}.
/// The phone is the TLS server; this PC always dials out (no inbound firewall rules needed).
/// Both sides present self-signed certificates that are pinned at pairing.
/// </summary>
public static class Proto
{
    /// <summary>Bump for incompatible wire changes. Sent as "proto" in orbit.hello / orbit.pair.</summary>
    public const int Version = 1;
    public static readonly string AppVersion =
        typeof(Proto).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static JsonObject WithVersion(JsonObject body)
    {
        body["proto"] = Version;
        body["app"] = AppVersion;
        return body;
    }

    public const int PhonePort = 47800;
    public const int DiscoveryPort = 47801;
    public const int UsbForwardPort = 47810;

    public static string Encode(string type, JsonObject? body = null) =>
        new JsonObject { ["type"] = type, ["body"] = body ?? new JsonObject() }.ToJsonString() + "\n";

    public static (string type, JsonObject body) Decode(string line)
    {
        var o = JsonNode.Parse(line)!.AsObject();
        return (o["type"]?.GetValue<string>() ?? "", o["body"] as JsonObject ?? new JsonObject());
    }

    public static string Hmac(string token, params string[] parts)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(string.Join("|", parts)));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    public static string Nonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Open a TLS connection to the phone, presenting our certificate.</summary>
    public static async Task<(TcpClient tcp, SslStream ssl, string fp)> DialAsync(
        string host, int port, Func<string, bool> acceptFingerprint, int timeoutMs, CancellationToken ct)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await tcp.ConnectAsync(host, port, cts.Token);
            string fp = "";
            var ssl = new SslStream(tcp.GetStream(), false);
            var opts = new SslClientAuthenticationOptions
            {
                TargetHost = "orbit-phone",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificates = new X509CertificateCollection { Identity.Certificate },
                LocalCertificateSelectionCallback = (_, _, _, _, _) => Identity.Certificate,
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                {
                    if (cert == null) return false;
                    fp = Identity.FingerprintOf(cert);
                    return acceptFingerprint(fp);
                }
            };
            await ssl.AuthenticateAsClientAsync(opts, cts.Token);
            return (tcp, ssl, fp);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }
}

/// <summary>The long-lived control channel to the phone.</summary>
public sealed class PhoneLink : IDisposable
{
    public string Host { get; }
    public int Port { get; }
    public string Kind { get; }

    readonly TcpClient _tcp;
    readonly SslStream _ssl;
    readonly StreamReader _reader;
    readonly SemaphoreSlim _writeLock = new(1, 1);
    public DateTime LastHeard { get; private set; } = DateTime.Now;
    bool _disposed;

    public PhoneLink(TcpClient tcp, SslStream ssl, string host, int port, string kind)
    {
        _tcp = tcp; _ssl = ssl; Host = host; Port = port; Kind = kind;
        _reader = new StreamReader(ssl, new UTF8Encoding(false), false, 64 * 1024, leaveOpen: true);
    }

    public async Task SendAsync(string type, JsonObject? body = null)
    {
        var bytes = Encoding.UTF8.GetBytes(Proto.Encode(type, body));
        await _writeLock.WaitAsync();
        try
        {
            await _ssl.WriteAsync(bytes);
            await _ssl.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    public void Post(string type, JsonObject? body = null) =>
        _ = SendAsync(type, body).ContinueWith(t => { if (t.IsFaulted) Dispose(); }, TaskScheduler.Default);

    public async Task<(string type, JsonObject body)?> ReadAsync(CancellationToken ct)
    {
        var line = await _reader.ReadLineAsync(ct);
        if (line == null) return null;
        LastHeard = DateTime.Now;
        return line.Length == 0 ? ("", new JsonObject()) : Proto.Decode(line);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _ssl.Dispose(); } catch { }
        try { _tcp.Dispose(); } catch { }
    }
}
