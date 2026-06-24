// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Exercises the inbound QoS 2 exactly-once receiver: PUBREC on receipt, single delivery,
/// de-duplication of a redelivered PUBLISH, and PUBCOMP on PUBREL.
/// </summary>
public class InboundQoS2Tests
{
    private static async Task<(MqttClient Client, FakeBroker Broker, MqttSubscription Sub)>
        ConnectAndSubscribeAsync(string topic, CancellationToken ct)
    {
        var factory = new FakeTransportFactory();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "t",
            ProtocolVersion = MqttProtocolVersion.V500,
            CleanStart = true,
            KeepAliveSeconds = 0,
            Reconnect = null,
        }, factory);
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var subTask = client.SubscribeAsync(topic, cancellationToken: ct);
        var subSent = await broker.ReadPacketAsync(ct);
        await broker.SendSubAckAsync(subSent.PacketId, MqttReasonCode.Success, ct);
        var sub = await subTask;
        return (client, broker, sub);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Inbound_qos2_delivers_once_and_acks_pubrec_then_pubcomp(CancellationToken ct)
    {
        var (client, broker, sub) = await ConnectAndSubscribeAsync("q2/topic", ct);
        await using var _ = client;

        await broker.SendPublishAsync(
            "q2/topic", new byte[] { 1 }, MqttQoS.ExactlyOnce, packetId: 42, ct: ct);

        var rec = await broker.ReadPacketAsync(ct);
        await Assert.That(rec.Type).IsEqualTo(5);          // PUBREC
        await Assert.That(rec.PacketId).IsEqualTo((ushort)42);

        var m = await sub.Reader.ReadAsync(ct);
        await Assert.That(m.PayloadMemory.Span[0]).IsEqualTo((byte)1);
        m.Dispose();

        await broker.SendPubRelAsync(42, ct: ct);
        var comp = await broker.ReadPacketAsync(ct);
        await Assert.That(comp.Type).IsEqualTo(7);          // PUBCOMP
        await Assert.That(comp.PacketId).IsEqualTo((ushort)42);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Inbound_qos2_redelivery_before_pubrel_is_deduplicated(CancellationToken ct)
    {
        var (client, broker, sub) = await ConnectAndSubscribeAsync("q2/dedup", ct);
        await using var _ = client;

        // First delivery of packet id 7.
        await broker.SendPublishAsync(
            "q2/dedup", new byte[] { 1 }, MqttQoS.ExactlyOnce, packetId: 7, ct: ct);
        var rec1 = await broker.ReadPacketAsync(ct);
        await Assert.That(rec1.Type).IsEqualTo(5);          // PUBREC
        var m1 = await sub.Reader.ReadAsync(ct);
        await Assert.That(m1.PayloadMemory.Span[0]).IsEqualTo((byte)1);
        m1.Dispose();

        // Broker redelivers the same packet id before PUBREL: re-ack but do not deliver again.
        await broker.SendPublishAsync(
            "q2/dedup", new byte[] { 1 }, MqttQoS.ExactlyOnce, packetId: 7, ct: ct);
        var rec2 = await broker.ReadPacketAsync(ct);
        await Assert.That(rec2.Type).IsEqualTo(5);          // PUBREC again

        await broker.SendPubRelAsync(7, ct: ct);
        var comp = await broker.ReadPacketAsync(ct);
        await Assert.That(comp.Type).IsEqualTo(7);          // PUBCOMP

        // Prove the redelivery was not dispatched: the next channel message is the following
        // publish (payload 2), not a duplicate of payload 1.
        await broker.SendPublishAsync("q2/dedup", new byte[] { 2 }, ct: ct);
        var next = await sub.Reader.ReadAsync(ct);
        await Assert.That(next.PayloadMemory.Span[0]).IsEqualTo((byte)2);
        next.Dispose();
    }
}
