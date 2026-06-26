// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// End-to-end validation of the embeddable MqttTestBroker, driven by the real Mqtt.Client client.

namespace Mqtt.Client.Testing.Tests;

public class BrokerRoundTripTests
{
    private static MqttClient NewClient(
        MqttTestBroker broker,
        MqttProtocolVersion version = MqttProtocolVersion.V500,
        string? clientId = null,
        Action<MqttClientBuilder>? configure = null)
    {
        var builder = MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://127.0.0.1:{broker.Port}")
            .WithClientId(clientId ?? $"test-{Guid.NewGuid():N}")
            .WithProtocol(version)
            .WithReconnect(null);
        configure?.Invoke(builder);
        return builder.Build();
    }

    [Test]
    [Timeout(15_000)]
    public async Task Connect_Succeeds(CancellationToken ct)
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var client = NewClient(broker);
        var connect = await client.ConnectAsync();
        await Assert.That(connect.IsSuccess).IsTrue();
    }

    [Test]
    [Timeout(15_000)]
    public async Task RejectsBadCredentials(CancellationToken ct)
    {
        await using var broker = await MqttTestBroker.StartAsync(new MqttTestBrokerOptions
        {
            AllowAnonymous = false,
            Authenticate = (_, user, _) => user == "good",
        });
        await using var client = NewClient(broker, configure: b => b.WithCredentials("bad", "x"));
        var connect = await client.ConnectAsync();
        await Assert.That(connect.IsSuccess).IsFalse();
    }

    [Test]
    [Timeout(15_000)]
    public async Task QoS0_RoundTrip(CancellationToken ct)
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var client = NewClient(broker);
        await client.ConnectAsync();

        await using var sub = await client.SubscribeAsync("t/qos0");
        await client.PublishAsync("t/qos0", new byte[] { 1, 2, 3 }, MqttQoS.AtMostOnce);

        var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(msg.Topic).IsEqualTo("t/qos0");
        await Assert.That(msg.PayloadMemory.Span.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
    }

    [Test]
    [Timeout(15_000)]
    public async Task QoS1_RoundTrip(CancellationToken ct)
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var client = NewClient(broker);
        await client.ConnectAsync();

        await using var sub = await client.SubscribeAsync(
            "t/qos1", new MqttSubscriptionOptions { QoS = MqttQoS.AtLeastOnce });
        var result = await client.PublishAsync("t/qos1", new byte[] { 9 }, MqttQoS.AtLeastOnce);
        await Assert.That(result.IsSuccess).IsTrue();

        var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(msg.Topic).IsEqualTo("t/qos1");
    }

    [Test]
    [Timeout(15_000)]
    public async Task QoS2_RoundTrip(CancellationToken ct)
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var client = NewClient(broker);
        await client.ConnectAsync();

        await using var sub = await client.SubscribeAsync(
            "t/qos2", new MqttSubscriptionOptions { QoS = MqttQoS.ExactlyOnce });
        var result = await client.PublishAsync("t/qos2", new byte[] { 7 }, MqttQoS.ExactlyOnce);
        await Assert.That(result.IsSuccess).IsTrue();

        var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(msg.Topic).IsEqualTo("t/qos2");
    }

    [Test]
    [Timeout(15_000)]
    public async Task Wildcards_PlusAndHash(CancellationToken ct)
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var client = NewClient(broker);
        await client.ConnectAsync();

        await using var plus = await client.SubscribeAsync("sensors/+/temp");
        await using var hash = await client.SubscribeAsync("logs/#");

        await client.PublishAsync("sensors/room1/temp", new byte[] { 1 }, MqttQoS.AtMostOnce);
        var a = await plus.Reader.ReadAsync(ct);
        await Assert.That(a.Topic).IsEqualTo("sensors/room1/temp");

        await client.PublishAsync("logs/a/b/c", new byte[] { 2 }, MqttQoS.AtMostOnce);
        var b = await hash.Reader.ReadAsync(ct);
        await Assert.That(b.Topic).IsEqualTo("logs/a/b/c");
    }

    [Test]
    [Timeout(15_000)]
    public async Task Retained_DeliveredOnSubscribe(CancellationToken ct)
    {
        await using var broker = await MqttTestBroker.StartAsync();

        await using (var publisher = NewClient(broker))
        {
            await publisher.ConnectAsync();
            await publisher.PublishAsync(
                "r/state", new byte[] { 42 }, MqttQoS.AtLeastOnce, retain: true);
        }

        await using var subscriber = NewClient(broker);
        await subscriber.ConnectAsync();
        await using var sub = await subscriber.SubscribeAsync("r/state");

        var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(msg.Topic).IsEqualTo("r/state");
        await Assert.That(msg.PayloadMemory.Span[0]).IsEqualTo((byte)42);
    }

    [Test]
    [Timeout(15_000)]
    public async Task LastWill_DeliveredOnUngracefulDisconnect(CancellationToken ct)
    {
        await using var broker = await MqttTestBroker.StartAsync();

        var willId = $"willer-{Guid.NewGuid():N}";
        await using var dying = NewClient(broker, clientId: willId, configure: b => b
            .WithLastWill(new MqttLastWill
            {
                Topic = "clients/dead",
                PayloadMemory = new byte[] { 0xDE, 0xAD },
                QoS = MqttQoS.AtLeastOnce,
            }));
        await dying.ConnectAsync();

        await using var watcher = NewClient(broker);
        await watcher.ConnectAsync();
        await using var sub = await watcher.SubscribeAsync(
            "clients/dead", new MqttSubscriptionOptions { QoS = MqttQoS.AtLeastOnce });

        broker.DisconnectClient(willId);   // ungraceful drop -> broker publishes the will

        var will = await sub.Reader.ReadAsync(ct);
        await Assert.That(will.Topic).IsEqualTo("clients/dead");
        var willBytes = will.PayloadMemory.Span.SequenceEqual(new byte[] { 0xDE, 0xAD });
        await Assert.That(willBytes).IsTrue();
    }

    [Test]
    [Timeout(15_000)]
    public async Task V311_RoundTrip(CancellationToken ct)
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var client = NewClient(broker, MqttProtocolVersion.V311);
        await client.ConnectAsync();

        await using var sub = await client.SubscribeAsync("v3/topic");
        await client.PublishAsync("v3/topic", new byte[] { 5 }, MqttQoS.AtMostOnce);

        var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(msg.Topic).IsEqualTo("v3/topic");
    }

    [Test]
    [Timeout(15_000)]
    public async Task LargePayload_RoundTrip(CancellationToken ct)
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var client = NewClient(
            broker, configure: b => b.Configure(o => o.MaxIncomingPacketSize = 1024 * 1024));
        await client.ConnectAsync();

        await using var sub = await client.SubscribeAsync(
            "t/large", new MqttSubscriptionOptions { QoS = MqttQoS.AtLeastOnce });

        var payload = new byte[256_000];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)((i * 31) + 7);
        await client.PublishAsync("t/large", payload, MqttQoS.AtLeastOnce);

        var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(msg.PayloadMemory.Length).IsEqualTo(payload.Length);
        await Assert.That(msg.PayloadMemory.Span.SequenceEqual(payload)).IsTrue();
    }

    [Test]
    [Timeout(20_000)]
    public async Task ParallelIsolatedBrokers(CancellationToken ct)
    {
        async Task RunOne(int index)
        {
            await using var broker = await MqttTestBroker.StartAsync();
            await using var client = NewClient(broker);
            await client.ConnectAsync();
            await using var sub = await client.SubscribeAsync("iso/topic");

            var payload = new byte[] { (byte)index };
            await client.PublishAsync("iso/topic", payload, MqttQoS.AtMostOnce);

            var msg = await sub.Reader.ReadAsync(ct);
            // Each broker is isolated: the only message on this port is this broker's own.
            await Assert.That(msg.PayloadMemory.Span[0]).IsEqualTo((byte)index);
        }

        var runs = new Task[6];
        for (var i = 0; i < runs.Length; i++) runs[i] = RunOne(i);
        await Task.WhenAll(runs);
    }
}
