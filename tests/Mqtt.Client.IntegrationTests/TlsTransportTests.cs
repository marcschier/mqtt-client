// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// Validates the TLS transport (Pipelines.Sockets.Unofficial StreamConnection over SslStream) by
// round-tripping a large, multi-segment payload through the real TlsTransportFactory against a
// loopback TLS echo server using a self-signed certificate.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Mqtt.Client.IntegrationTests;

public class TlsTransportTests
{
    [Test]
    [Timeout(20_000)]
    public async Task Tls_LargePayload_RoundTrip(CancellationToken ct)
    {
        using var cert = CreateSelfSignedCertificate();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = RunTlsEchoServerAsync(listener, cert, ct);

        var expectedThumbprint = cert.Thumbprint;
        var tlsOptions = new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            // Pin the self-signed test certificate (avoids blindly trusting any server cert).
            RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate?.GetCertHashString() == expectedThumbprint,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };
        var factory = new TlsTransportFactory("localhost", port, tlsOptions);
        await using var transport = await factory.ConnectAsync(ct);

        // Larger than the 8 KB pipe segment so the payload spans multiple segments over TLS.
        var payload = new byte[200_000];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)((i * 31) + 7);
        }

        // Write on a background task so a large echo can't deadlock the read below.
        var writeTask = Task.Run(async () =>
        {
            await transport.Output.WriteAsync(payload, ct);
            await transport.Output.FlushAsync(ct);
        }, ct);

        var received = new byte[payload.Length];
        var got = 0;
        while (got < received.Length)
        {
            var result = await transport.Input.ReadAsync(ct);
            var buffer = result.Buffer;
            var consumed = 0L;
            foreach (var segment in buffer)
            {
                if (got >= received.Length) break;
                var take = Math.Min(segment.Length, received.Length - got);
                segment.Span.Slice(0, take).CopyTo(received.AsSpan(got));
                got += take;
                consumed += take;
            }
            transport.Input.AdvanceTo(buffer.GetPosition(consumed));
            if (result.IsCompleted && got < received.Length) break;
        }

        await writeTask;
        await Assert.That(got).IsEqualTo(payload.Length);
        await Assert.That(received.AsSpan().SequenceEqual(payload)).IsTrue();
    }

    private static async Task RunTlsEchoServerAsync(
        TcpListener listener,
        X509Certificate2 cert,
        CancellationToken ct)
    {
        using var socket = await listener.AcceptSocketAsync(ct);
        await using var ssl = new SslStream(new NetworkStream(socket, ownsSocket: true), false);
        await ssl.AuthenticateAsServerAsync(
            cert,
            clientCertificateRequired: false,
            SslProtocols.None,
            checkCertificateRevocation: false);
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await ssl.ReadAsync(buffer, ct)) > 0)
        {
            await ssl.WriteAsync(buffer.AsMemory(0, n), ct);
            await ssl.FlushAsync(ct);
        }
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
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
        // Round-trip through PFX so the private key is reliably usable by the server SslStream.
        var pfx = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }
}
