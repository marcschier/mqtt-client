// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// Core publish/subscribe round-trips. Each test runs against both broker implementations
// (the MQTTnet in-process server and the embeddable MqttTestBroker) via [Arguments].

namespace Mqtt.Client.IntegrationTests;

public class PublishSubscribeTests
{
    private static MqttClient Connect(
        IIntegrationBroker broker, MqttProtocolVersion version = MqttProtocolVersion.V500)
        => MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://127.0.0.1:{broker.Port}")
            .WithClientId($"test-{Guid.NewGuid():N}")
            .WithProtocol(version)
            .Build();

    [Test]
    [Arguments(BrokerKind.Mqttnet)]
    [Arguments(BrokerKind.Testing)]
    [Timeout(15_000)]
    public async Task QoS0_RoundTrip(BrokerKind kind, CancellationToken ct)
    {
        await using var broker = await Brokers.StartAsync(kind);
        await using var client = Connect(broker);

        var connect = await client.ConnectAsync(ct);
        await Assert.That(connect.IsSuccess).IsTrue();

        var sub = await client.SubscribeAsync("itest/qos0", cancellationToken: ct);
        await using var _ = sub;

        var payload = new byte[] { 1, 2, 3 };
        await client.PublishAsync("itest/qos0", payload, MqttQoS.AtMostOnce, cancellationToken: ct);

        var received = await sub.Reader.ReadAsync(ct);
        await Assert.That(received.Topic).IsEqualTo("itest/qos0");
        await Assert.That(received.PayloadMemory.Length).IsEqualTo(3);
    }

    [Test]
    [Arguments(BrokerKind.Mqttnet)]
    [Arguments(BrokerKind.Testing)]
    [Timeout(15_000)]
    public async Task QoS1_RoundTrip(BrokerKind kind, CancellationToken ct)
    {
        await using var broker = await Brokers.StartAsync(kind);
        await using var client = Connect(broker);
        await client.ConnectAsync(ct);

        var sub = await client.SubscribeAsync(
            "itest/qos1", new MqttSubscriptionOptions { QoS = MqttQoS.AtLeastOnce }, ct);
        await using var _ = sub;

        var result = await client.PublishAsync(
            "itest/qos1", new byte[] { 9 }, MqttQoS.AtLeastOnce, cancellationToken: ct);
        await Assert.That(result.IsSuccess).IsTrue();

        var received = await sub.Reader.ReadAsync(ct);
        await Assert.That(received.Topic).IsEqualTo("itest/qos1");
    }

    [Test]
    [Arguments(BrokerKind.Mqttnet)]
    [Arguments(BrokerKind.Testing)]
    [Timeout(15_000)]
    public async Task QoS2_RoundTrip(BrokerKind kind, CancellationToken ct)
    {
        await using var broker = await Brokers.StartAsync(kind);
        await using var client = Connect(broker);
        await client.ConnectAsync(ct);

        var sub = await client.SubscribeAsync(
            "itest/qos2", new MqttSubscriptionOptions { QoS = MqttQoS.ExactlyOnce }, ct);
        await using var _ = sub;

        var result = await client.PublishAsync(
            "itest/qos2", new byte[] { 7 }, MqttQoS.ExactlyOnce, cancellationToken: ct);
        await Assert.That(result.IsSuccess).IsTrue();

        var received = await sub.Reader.ReadAsync(ct);
        await Assert.That(received.Topic).IsEqualTo("itest/qos2");
    }

    [Test]
    [Arguments(BrokerKind.Mqttnet)]
    [Arguments(BrokerKind.Testing)]
    [Timeout(20_000)]
    public async Task LargePayload_RoundTrip_MultiSegment(BrokerKind kind, CancellationToken ct)
    {
        await using var broker = await Brokers.StartAsync(kind);
        var client = MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://127.0.0.1:{broker.Port}")
            .WithClientId($"test-{Guid.NewGuid():N}")
            .WithProtocol(MqttProtocolVersion.V500)
            .Configure(o => o.MaxIncomingPacketSize = 1024 * 1024)
            .Build();
        await using var _ = client;
        await client.ConnectAsync(ct);

        var sub = await client.SubscribeAsync(
            "itest/large", new MqttSubscriptionOptions { QoS = MqttQoS.AtLeastOnce }, ct);
        await using var __ = sub;

        // Far larger than the 8 KB outbound pipe segment, spanning enough segments to take the
        // scatter-gather socket send path (the payload must arrive byte-for-byte intact).
        var payload = new byte[600_000];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)((i * 31) + 7);
        }
        var result = await client.PublishAsync(
            "itest/large", payload, MqttQoS.AtLeastOnce, cancellationToken: ct);
        await Assert.That(result.IsSuccess).IsTrue();

        var received = await sub.Reader.ReadAsync(ct);
        await Assert.That(received.PayloadMemory.Length).IsEqualTo(payload.Length);
        await Assert.That(received.PayloadMemory.Span.SequenceEqual(payload)).IsTrue();
    }
}
