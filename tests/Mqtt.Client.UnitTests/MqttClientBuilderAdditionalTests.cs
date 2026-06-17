// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Mqtt.Client.Persistence;

namespace Mqtt.Client.UnitTests;

public class MqttClientBuilderAdditionalTests
{
    [Test]
    public async Task ConnectTo_throws_on_null_or_empty()
    {
        await Assert.That(() => MqttClient.CreateBuilder().ConnectTo(null!)).Throws<ArgumentException>();
        await Assert.That(() => MqttClient.CreateBuilder().ConnectTo(""))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ConnectTo_throws_on_unknown_scheme()
    {
        await Assert.That(() => MqttClient.CreateBuilder().ConnectTo("http://broker"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ConnectTo_parses_ws_and_wss_with_default_ports()
    {
        var c1 = MqttClient.CreateBuilder().ConnectTo("ws://broker").Build();
        await using var _1 = c1;
        var c2 = MqttClient.CreateBuilder().ConnectTo("wss://broker").Build();
        await using var _2 = c2;
        await Assert.That(c1.State).IsEqualTo(MqttConnectionState.Disconnected);
        await Assert.That(c2.State).IsEqualTo(MqttConnectionState.Disconnected);
    }

    [Test]
    public async Task With_methods_chain_fluently()
    {
        var will = new MqttLastWill { Topic = "lw", Payload = ReadOnlyMemory<byte>.Empty };
        var policy = MqttReconnectPolicy.Fixed(TimeSpan.FromSeconds(1));
        var c = MqttClient.CreateBuilder()
            .ConnectTo("mqtt://broker")
            .WithClientId("cid")
            .WithCredentials("u", new byte[] { 1 })
            .WithProtocol(MqttProtocolVersion.V311)
            .WithKeepAlive(10)
            .WithCleanStart(false)
            .WithReconnect(policy)
            .WithLastWill(will)
            .WithTls(_ => { })
            .WithLogging(NullLoggerFactory.Instance)
            .WithPersistence(new InMemorySessionStore())
            .Configure(o => o.Port = 9999)
            .Build();
        await using var _ = c;
        await Assert.That(c).IsNotNull();
    }

    [Test]
    public async Task WithCredentials_string_overload_encodes_password()
    {
        var c = MqttClient.CreateBuilder().ConnectTo("mqtt://broker").WithCredentials("u", "secret").Build();
        await using var _ = c;
        await Assert.That(c).IsNotNull();
    }

    [Test]
    public async Task WithClientCertificate_sets_tls_collection()
    {
        using var cert = MakeSelfSignedCert();
        var c = MqttClient.CreateBuilder().ConnectTo("mqtts://broker").WithClientCertificate(cert).Build();
        await using var _ = c;
        await Assert.That(c).IsNotNull();
    }

    private static X509Certificate2 MakeSelfSignedCert()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new CertificateRequest("CN=test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
