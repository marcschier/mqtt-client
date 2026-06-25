// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Mqtt.Client.InteropTests;

/// <summary>
/// Cross-implementation interop tests: Mqtt.Client against a real Eclipse Mosquitto broker (C),
/// and against the mosquitto_pub / mosquitto_sub C client tools in both directions. Every test
/// no-ops when Mosquitto is not installed (see <see cref="MosquittoBroker.IsAvailable"/>), so the
/// suite is safe to run anywhere; it exercises real interop only where Mosquitto is present.
/// </summary>
public class MosquittoInteropTests
{
    private static readonly string[] V5PublishArgs =
    {
        "-D", "publish", "user-property", "k1", "v1",
        "-D", "publish", "content-type", "text/plain",
    };

    private static MqttClient BuildPlain(MosquittoBroker broker)
        => MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://{broker.Host}:{broker.Port}")
            .WithClientId($"ours-{Guid.NewGuid():N}")
            .WithProtocol(MqttProtocolVersion.V500)
            .WithCleanStart(true)
            .WithKeepAlive(30)
            .WithReconnect(null)
            .Build();

    private static MqttClient BuildTls(MosquittoBroker broker)
    {
        var tls = new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            // Pin the broker's self-signed test certificate.
            RemoteCertificateValidationCallback = (_, cert, _, _) =>
                cert?.GetCertHashString() == broker.TlsThumbprint,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };
        return MqttClient.CreateBuilder()
            .ConnectTo($"mqtts://{broker.Host}:{broker.TlsPort}")
            .WithClientId($"ours-tls-{Guid.NewGuid():N}")
            .WithProtocol(MqttProtocolVersion.V500)
            .WithCleanStart(true)
            .WithKeepAlive(30)
            .WithReconnect(null)
            .Configure(o => o.Tls = tls)
            .Build();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Timeout(30_000)]
    public async Task RoundTrip_through_mosquitto(int qos, CancellationToken ct)
    {
        if (!MosquittoBroker.IsAvailable) return;
        await using var broker = await MosquittoBroker.StartAsync(ct);
        await using var client = BuildPlain(broker);
        await client.ConnectAsync(ct);

        var topic = $"interop/rt/{qos}/{Guid.NewGuid():N}";
        var sub = await client.SubscribeAsync(topic, cancellationToken: ct);
        var payload = System.Text.Encoding.UTF8.GetBytes($"rt-qos{qos}");
        await client.PublishAsync(topic, payload, (MqttQoS)qos, cancellationToken: ct);

        using var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(msg.PayloadMemory.ToArray()).IsEquivalentTo(payload);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Ours_publishes_retained_C_client_receives(CancellationToken ct)
    {
        if (!MosquittoBroker.IsAvailable) return;
        await using var broker = await MosquittoBroker.StartAsync(ct);
        await using var client = BuildPlain(broker);
        await client.ConnectAsync(ct);

        var topic = $"interop/retain/{Guid.NewGuid():N}";
        await client.PublishAsync(
            topic, System.Text.Encoding.UTF8.GetBytes("retained-hello"),
            MqttQoS.AtLeastOnce, retain: true, cancellationToken: ct);

        // A fresh C subscriber receives the retained message immediately on subscribe.
        var lines = await MosquittoTools.SubscribeCaptureAsync(
            broker.Port, topic, count: 1, ct: ct);
        await Assert.That(lines).Contains("retained-hello");
    }

    [Test]
    [Timeout(30_000)]
    public async Task C_client_publishes_ours_receives(CancellationToken ct)
    {
        if (!MosquittoBroker.IsAvailable) return;
        await using var broker = await MosquittoBroker.StartAsync(ct);
        await using var client = BuildPlain(broker);
        await client.ConnectAsync(ct);

        var topic = $"interop/cpub/{Guid.NewGuid():N}";
        var sub = await client.SubscribeAsync(topic, cancellationToken: ct);  // completes on SUBACK
        await MosquittoTools.PublishAsync(broker.Port, topic, "from-c", qos: 1, ct: ct);

        using var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(System.Text.Encoding.UTF8.GetString(msg.PayloadMemory.Span))
            .IsEqualTo("from-c");
    }

    [Test]
    [Timeout(30_000)]
    public async Task C_client_will_is_delivered_to_ours(CancellationToken ct)
    {
        if (!MosquittoBroker.IsAvailable) return;
        await using var broker = await MosquittoBroker.StartAsync(ct);
        await using var client = BuildPlain(broker);
        await client.ConnectAsync(ct);

        var willTopic = $"interop/will/{Guid.NewGuid():N}";
        var sub = await client.SubscribeAsync(willTopic, cancellationToken: ct);

        // A C client connects with a Last Will, then is killed ungracefully (no DISCONNECT), so
        // the broker publishes its will to our subscription.
        var willed = MosquittoTools.StartWilledClient(
            broker.Port, idleTopic: "interop/idle", willTopic, willPayload: "gone", willQos: 1);
        try
        {
            await Task.Delay(1500, ct);   // let it connect + register the will
            willed.Kill(entireProcessTree: true);

            using var msg = await sub.Reader.ReadAsync(ct);
            await Assert.That(System.Text.Encoding.UTF8.GetString(msg.PayloadMemory.Span))
                .IsEqualTo("gone");
        }
        finally
        {
            try { willed.Kill(entireProcessTree: true); } catch { /* already dead */ }
            willed.Dispose();
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C_client_v5_properties_are_decoded_by_ours(CancellationToken ct)
    {
        if (!MosquittoBroker.IsAvailable) return;
        await using var broker = await MosquittoBroker.StartAsync(ct);
        await using var client = BuildPlain(broker);
        await client.ConnectAsync(ct);

        var topic = $"interop/v5/{Guid.NewGuid():N}";
        var sub = await client.SubscribeAsync(topic, cancellationToken: ct);

        await MosquittoTools.PublishAsync(
            broker.Port, topic, "props", qos: 1,
            extraArgs: V5PublishArgs,
            ct: ct);

        using var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(msg.Properties).IsNotNull();
        await Assert.That(msg.Properties!.ContentType).IsEqualTo("text/plain");
        var ups = msg.Properties!.UserProperties;
        await Assert.That(ups).IsNotNull();
        await Assert.That(ups!.Any(p => p.Name == "k1" && p.Value == "v1")).IsTrue();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Tls_round_trip_through_mosquitto(CancellationToken ct)
    {
        if (!MosquittoBroker.IsAvailable) return;
        await using var broker = await MosquittoBroker.StartAsync(ct);
        await using var client = BuildTls(broker);
        await client.ConnectAsync(ct);

        var topic = $"interop/tls/{Guid.NewGuid():N}";
        var sub = await client.SubscribeAsync(topic, cancellationToken: ct);
        var payload = System.Text.Encoding.UTF8.GetBytes("tls-hello");
        await client.PublishAsync(topic, payload, MqttQoS.AtLeastOnce, cancellationToken: ct);

        using var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(msg.PayloadMemory.ToArray()).IsEquivalentTo(payload);
    }
}
