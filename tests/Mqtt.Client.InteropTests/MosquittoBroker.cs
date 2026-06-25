// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Mqtt.Client.InteropTests;

/// <summary>
/// Launches a real Eclipse Mosquitto broker (the C MQTT server) as a child process for interop
/// tests, with anonymous plain-TCP and TLS listeners on ephemeral ports. Use
/// <see cref="IsAvailable"/> to skip when Mosquitto is not installed.
/// </summary>
internal sealed class MosquittoBroker : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _dir;

    private MosquittoBroker(Process process, string dir, int port, int tlsPort, string thumbprint)
    {
        _process = process;
        _dir = dir;
        Host = "127.0.0.1";
        Port = port;
        TlsPort = tlsPort;
        TlsThumbprint = thumbprint;
    }

    public string Host { get; }

    /// <summary>Plain-TCP listener port.</summary>
    public int Port { get; }

    /// <summary>TLS listener port.</summary>
    public int TlsPort { get; }

    /// <summary>SHA-1 thumbprint of the broker's self-signed TLS certificate (for pinning).</summary>
    public string TlsThumbprint { get; }

    /// <summary>True when the mosquitto broker binary is on PATH.</summary>
    public static bool IsAvailable => MosquittoTools.Find("mosquitto") is not null;

    public static async Task<MosquittoBroker> StartAsync(CancellationToken ct = default)
    {
        var exe = MosquittoTools.Find("mosquitto")
            ?? throw new InvalidOperationException("mosquitto is not on PATH.");
        var dir = Directory.CreateTempSubdirectory("mqtt-interop-").FullName;
        var port = GetEphemeralPort();
        var tlsPort = GetEphemeralPort();

        var (certPem, keyPem, thumbprint) = CreateServerCertificatePem();
        var certPath = Path.Combine(dir, "server.crt");
        var keyPath = Path.Combine(dir, "server.key");
        await File.WriteAllTextAsync(certPath, certPem, ct);
        await File.WriteAllTextAsync(keyPath, keyPem, ct);

        var conf = string.Join('\n',
            "allow_anonymous true",
            $"listener {port} 127.0.0.1",
            $"listener {tlsPort} 127.0.0.1",
            $"certfile {certPath}",
            $"keyfile {keyPath}",
            "tls_version tlsv1.2",
            "");
        var confPath = Path.Combine(dir, "mosquitto.conf");
        await File.WriteAllTextAsync(confPath, conf, ct);

        // Mosquitto drops privileges to the 'mosquitto' user after start, so the temp dir + cert/key
        // must be world-traversable/readable or it fails to load the TLS key ("Permission denied").
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode dirMode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;     // 755
            const UnixFileMode fileMode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead;        // 644
            File.SetUnixFileMode(dir, dirMode);
            File.SetUnixFileMode(certPath, fileMode);
            File.SetUnixFileMode(keyPath, fileMode);
            File.SetUnixFileMode(confPath, fileMode);
        }

        var psi = new ProcessStartInfo(exe, $"-c \"{confPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start mosquitto.");
        // Drain output so the broker's pipes never fill and block it.
        _ = process.StandardOutput.ReadToEndAsync(ct);
        _ = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await WaitForPortAsync(port, TimeSpan.FromSeconds(15), ct);
            await WaitForPortAsync(tlsPort, TimeSpan.FromSeconds(15), ct);
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }
        return new MosquittoBroker(process, dir, port, tlsPort, thumbprint);
    }

    private static (string CertPem, string KeyPem, string Thumbprint) CreateServerCertificatePem()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return (cert.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem(), cert.Thumbprint);
    }

    private static async Task WaitForPortAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, ct);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100, ct);
            }
        }
        throw new TimeoutException($"Mosquitto did not open port {port} in time.");
    }

    private static int GetEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        try { _process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        try { await _process.WaitForExitAsync(); } catch { /* best effort */ }
        _process.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
