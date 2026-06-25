// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Mqtt.Client.ChaosTests;

/// <summary>
/// A controllable in-process MQTT broker that listens on a TLS endpoint using a self-signed
/// certificate generated at runtime. The chaos client trusts the certificate via a permissive
/// validation callback (test-only). Network faults are injected below TLS by the proxy, so a
/// corrupted byte tears down the TLS session and the client must recover.
/// </summary>
public sealed class TlsBrokerHarness : IChaosBroker
{
    private readonly X509Certificate2 _certificate;
    private MqttServer _server;

    private TlsBrokerHarness(MqttServer server, int port, X509Certificate2 certificate)
    {
        _server = server;
        Port = port;
        _certificate = certificate;
    }

    public int Port { get; }

    public bool RejectConnections { get; set; }

    public X509Certificate2? ServerCertificate => _certificate;

    public static async Task<TlsBrokerHarness> StartAsync(int? port = null)
    {
        var selectedPort = port ?? GetEphemeralPort();
        var certificate = CreateSelfSignedCertificate();
        var server = CreateServer(selectedPort, certificate);
        var broker = new TlsBrokerHarness(server, selectedPort, certificate);
        server.ValidatingConnectionAsync += broker.ValidateConnectionAsync;
        await server.StartAsync();
        return broker;
    }

    public async Task RestartAsync()
    {
        await StopServerAsync();
        _server.Dispose();
        _server = CreateServer(Port, _certificate);
        _server.ValidatingConnectionAsync += ValidateConnectionAsync;
        await _server.StartAsync();
    }

    public async Task ForceDisconnectAllAsync()
    {
        var clients = await _server.GetClientsAsync();
        var options = new MqttServerClientDisconnectOptions
        {
            ReasonCode = MqttDisconnectReasonCode.AdministrativeAction,
        };
        foreach (var client in clients)
        {
            await _server.DisconnectClientAsync(client.Id, options);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopServerAsync();
        _server.Dispose();
        _certificate.Dispose();
    }

    private static MqttServer CreateServer(int port, X509Certificate2 certificate)
    {
        var options = new MqttServerOptionsBuilder()
            .WithoutDefaultEndpoint()
            .WithEncryptedEndpoint()
            .WithEncryptedEndpointPort(port)
            .WithEncryptionCertificate(certificate)
            .WithEncryptionSslProtocol(SslProtocols.Tls12 | SslProtocols.Tls13)
            .Build();
        return new MqttServerFactory().CreateMqttServer(options);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Round-trip through PFX so the private key is usable by the TLS stack on every platform.
        var pfx = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }

    private static int GetEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task StopServerAsync()
    {
        if (_server.IsStarted)
        {
            await _server.StopAsync(new MqttServerStopOptions());
        }
    }

    private Task ValidateConnectionAsync(ValidatingConnectionEventArgs args)
    {
        if (RejectConnections)
        {
            args.ReasonCode = MqttConnectReasonCode.NotAuthorized;
            args.ReasonString = "TlsBrokerHarness RejectConnections is enabled.";
        }
        return Task.CompletedTask;
    }
}
