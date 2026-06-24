// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Exercises enforcement of broker-advertised CONNACK limits (Maximum QoS, Retain Available,
/// Maximum Packet Size, Topic Alias Maximum) and Receive Maximum flow control on the publish path.
/// </summary>
public class BrokerLimitsTests
{
    private static (MqttClient Client, FakeBroker Broker) Build(
        Action<MqttClientOptions>? configure = null)
    {
        var factory = new FakeTransportFactory();
        var options = new MqttClientOptions
        {
            Host = "fake",
            ClientId = "test",
            ProtocolVersion = MqttProtocolVersion.V500,
            CleanStart = true,
            KeepAliveSeconds = 0,
            Reconnect = null,
        };
        configure?.Invoke(options);
        var client = new MqttClient(options, factory);
        return (client, new FakeBroker(factory.Transport, MqttProtocolVersion.V500));
    }

    private static async Task ConnectWithLimitsAsync(
        MqttClient client,
        FakeBroker broker,
        CancellationToken ct,
        ushort? receiveMaximum = null,
        MqttQoS? maximumQoS = null,
        bool? retainAvailable = null,
        uint? maximumPacketSize = null,
        ushort? topicAliasMaximum = null)
    {
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckWithLimitsAsync(
            receiveMaximum, maximumQoS, retainAvailable, maximumPacketSize, topicAliasMaximum,
            ct: ct);
        await connectTask;
    }

    [Test]
    [Timeout(5_000)]
    public async Task Publish_qos_above_maximum_qos_throws(CancellationToken ct)
    {
        var (client, broker) = Build();
        await using var _ = client;
        await ConnectWithLimitsAsync(client, broker, ct, maximumQoS: MqttQoS.AtLeastOnce);

        await Assert.That(async () => await client.PublishAsync(
                "t", new byte[] { 1 }, MqttQoS.ExactlyOnce, cancellationToken: ct))
            .Throws<MqttProtocolException>();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Publish_retain_when_unavailable_throws(CancellationToken ct)
    {
        var (client, broker) = Build();
        await using var _ = client;
        await ConnectWithLimitsAsync(client, broker, ct, retainAvailable: false);

        await Assert.That(async () => await client.PublishAsync(
                "t", new byte[] { 1 }, MqttQoS.AtMostOnce, retain: true, cancellationToken: ct))
            .Throws<MqttProtocolException>();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Publish_exceeding_maximum_packet_size_throws(CancellationToken ct)
    {
        var (client, broker) = Build();
        await using var _ = client;
        await ConnectWithLimitsAsync(client, broker, ct, maximumPacketSize: 16);

        await Assert.That(async () => await client.PublishAsync(
                "topic/large", new byte[64], MqttQoS.AtMostOnce, cancellationToken: ct))
            .Throws<MqttProtocolException>();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Publish_topic_alias_above_maximum_throws(CancellationToken ct)
    {
        var (client, broker) = Build();
        await using var _ = client;
        await ConnectWithLimitsAsync(client, broker, ct, topicAliasMaximum: 5);

        var props = new MqttPublishProperties { TopicAlias = 10 };
        await Assert.That(async () => await client.PublishAsync(
                "t", new byte[] { 1 }, MqttQoS.AtMostOnce, properties: props,
                cancellationToken: ct))
            .Throws<MqttProtocolException>();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Adapt_downgrades_qos_to_broker_maximum(CancellationToken ct)
    {
        var (client, broker) = Build(o => o.BrokerLimitBehavior = MqttBrokerLimitBehavior.Adapt);
        await using var _ = client;
        await ConnectWithLimitsAsync(client, broker, ct, maximumQoS: MqttQoS.AtLeastOnce);

        var pub = client.PublishAsync(
            "t", new byte[] { 1 }, MqttQoS.ExactlyOnce, cancellationToken: ct).AsTask();
        var sent = await broker.ReadPacketAsync(ct);
        await Assert.That(sent.Type).IsEqualTo(3);              // PUBLISH
        await Assert.That(sent.QoS).IsEqualTo(MqttQoS.AtLeastOnce);  // downgraded 2 -> 1
        await broker.SendPubAckAsync(sent.PacketId, ct: ct);
        await pub;
    }

    [Test]
    [Timeout(5_000)]
    public async Task Adapt_drops_retain_when_unavailable(CancellationToken ct)
    {
        var (client, broker) = Build(o => o.BrokerLimitBehavior = MqttBrokerLimitBehavior.Adapt);
        await using var _ = client;
        await ConnectWithLimitsAsync(client, broker, ct, retainAvailable: false);

        await client.PublishAsync(
            "t", new byte[] { 1 }, MqttQoS.AtMostOnce, retain: true, cancellationToken: ct);
        var sent = await broker.ReadPacketAsync(ct);
        await Assert.That(sent.Type).IsEqualTo(3);
        await Assert.That(sent.FirstByte & 0x01).IsEqualTo(0);  // retain bit cleared
    }

    [Test]
    [Timeout(5_000)]
    public async Task Receive_maximum_reject_throws_when_quota_exhausted(CancellationToken ct)
    {
        var (client, broker) = Build(
            o => o.ReceiveMaximumBehavior = MqttReceiveMaximumBehavior.Reject);
        await using var _ = client;
        await ConnectWithLimitsAsync(client, broker, ct, receiveMaximum: 1);

        var pub1 = client.PublishAsync(
            "t/1", new byte[] { 1 }, MqttQoS.AtLeastOnce, cancellationToken: ct).AsTask();
        var sent1 = await broker.ReadPacketAsync(ct);
        await Assert.That(sent1.Type).IsEqualTo(3);

        await Assert.That(async () => await client.PublishAsync(
                "t/2", new byte[] { 2 }, MqttQoS.AtLeastOnce, cancellationToken: ct))
            .Throws<MqttProtocolException>();

        await broker.SendPubAckAsync(sent1.PacketId, ct: ct);
        await pub1;
    }

    [Test]
    [Timeout(5_000)]
    public async Task Receive_maximum_backpressure_blocks_until_first_ack(CancellationToken ct)
    {
        var (client, broker) = Build();   // default: Backpressure
        await using var _ = client;
        await ConnectWithLimitsAsync(client, broker, ct, receiveMaximum: 1);

        var pub1 = client.PublishAsync(
            "t/1", new byte[] { 1 }, MqttQoS.AtLeastOnce, cancellationToken: ct).AsTask();
        var sent1 = await broker.ReadPacketAsync(ct);
        await Assert.That(sent1.Type).IsEqualTo(3);

        // The second publish must block on the in-flight quota until the first is acked.
        var pub2 = client.PublishAsync(
            "t/2", new byte[] { 2 }, MqttQoS.AtLeastOnce, cancellationToken: ct).AsTask();
        await Task.Delay(50, ct);
        await Assert.That(pub2.IsCompleted).IsFalse();

        await broker.SendPubAckAsync(sent1.PacketId, ct: ct);
        await pub1;
        var sent2 = await broker.ReadPacketAsync(ct);   // PUBLISH2 only now reaches the wire
        await Assert.That(sent2.Type).IsEqualTo(3);
        await broker.SendPubAckAsync(sent2.PacketId, ct: ct);
        await pub2;
    }
}
