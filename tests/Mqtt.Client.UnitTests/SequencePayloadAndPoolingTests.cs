// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;

namespace Mqtt.Client.UnitTests;

public class SequencePayloadAndPoolingTests
{
    private sealed class Seg : ReadOnlySequenceSegment<byte>
    {
        public Seg(ReadOnlyMemory<byte> memory, Seg? previous = null)
        {
            Memory = memory;
            if (previous is not null)
            {
                previous.Next = this;
                RunningIndex = previous.RunningIndex + previous.Memory.Length;
            }
        }
    }

    private static ReadOnlySequence<byte> MultiSegment(params byte[][] chunks)
    {
        Seg? first = null, last = null;
        foreach (var c in chunks)
        {
            var s = new Seg(c, last);
            first ??= s;
            last = s;
        }
        return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
    }

    private static (MqttClient Client, FakeTransportFactory Factory) Build(bool pool = false)
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
            ReuseInboundBuffers = pool,
        }, factory);
        return (client, factory);
    }

    [Test]
    public async Task MultiSegment_payload_encodes_and_decodes_contiguously()
    {
        var seq = MultiSegment(new byte[] { 1, 2 }, new byte[] { 3, 4, 5 });
        var pkt = new PublishPacket
        {
            Topic = "seg/topic",
            QoS = MqttQoS.AtMostOnce,
            Payload = seq,
        };
        using var w = new MqttBufferWriter(64);
        MqttPacketEncoder.EncodePublish(pkt, MqttProtocolVersion.V500, w);

        var ok = MqttPacketDecoder.TryDecode(
            new ReadOnlySequence<byte>(w.WrittenMemory),
            MqttProtocolVersion.V500,
            out var decoded,
            out _,
            out _);
        await Assert.That(ok).IsTrue();
        var d = (PublishPacket)decoded!;
        await Assert.That(
            d.Payload.ToArray().AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4, 5 })).IsTrue();
    }

    [Test]
    public async Task PayloadMemory_is_zero_copy_for_single_segment()
    {
        var array = new byte[] { 9, 8, 7 };
        var msg = new MqttMessage { Topic = "t", Payload = new ReadOnlySequence<byte>(array) };
        // Single-segment: PayloadMemory should be backed by the same array (zero-copy view).
        await Assert.That(msg.Payload.IsSingleSegment).IsTrue();
        await Assert.That(msg.PayloadMemory.Length).IsEqualTo(3);
        await Assert.That(msg.PayloadMemory.Span[0]).IsEqualTo((byte)9);
    }

    [Test]
    public async Task PayloadMemory_materializes_multi_segment()
    {
        var msg = new MqttMessage
        {
            Topic = "t",
            Payload = MultiSegment(new byte[] { 1 }, new byte[] { 2, 3 }),
        };
        await Assert.That(msg.Payload.IsSingleSegment).IsFalse();
        await Assert.That(
            msg.PayloadMemory.ToArray().AsSpan().SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Outbound_sequence_publish_reaches_broker_concatenated(CancellationToken ct)
    {
        var (client, factory) = Build();
        await using var _ = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var seq = MultiSegment(new byte[] { 10, 20 }, new byte[] { 30, 40, 50 });
        await client.PublishAsync("seg/wire", seq, MqttQoS.AtMostOnce, cancellationToken: ct);

        var decoded = (PublishPacket)(await broker.ReadDecodedPacketAsync(ct))!;
        await Assert.That(decoded.Topic).IsEqualTo("seg/wire");
        await Assert.That(decoded.Payload.ToArray().AsSpan()
            .SequenceEqual(new byte[] { 10, 20, 30, 40, 50 })).IsTrue();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Pooled_inbound_delivers_payload_and_is_disposable(CancellationToken ct)
    {
        var (client, factory) = Build(pool: true);
        await using var _ = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var subTask = client.SubscribeAsync("pool/topic", cancellationToken: ct);
        var subSent = await broker.ReadPacketAsync(ct);
        await broker.SendSubAckAsync(subSent.PacketId, MqttReasonCode.Success, ct);
        var sub = await subTask;

        await broker.SendPublishAsync("pool/topic", new byte[] { 1, 2, 3, 4 }, ct: ct);
        var msg = await sub.Reader.ReadAsync(ct);
        await Assert.That(
            msg.PayloadMemory.ToArray().AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4 })).IsTrue();
        // Dispose returns the pooled buffer; must be idempotent.
        msg.Dispose();
        msg.Dispose();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Pooled_inbound_fanout_gives_each_sub_its_own_buffer(CancellationToken ct)
    {
        var (client, factory) = Build(pool: true);
        await using var _ = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        // Two overlapping subscriptions both match topic "a/b".
        var sub1Task = client.SubscribeAsync("a/+", cancellationToken: ct);
        var sub1Sent = await broker.ReadPacketAsync(ct);
        await broker.SendSubAckAsync(sub1Sent.PacketId, MqttReasonCode.Success, ct);
        var sub1 = await sub1Task;
        var sub2Task = client.SubscribeAsync("a/b", cancellationToken: ct);
        var sub2Sent = await broker.ReadPacketAsync(ct);
        await broker.SendSubAckAsync(sub2Sent.PacketId, MqttReasonCode.Success, ct);
        var sub2 = await sub2Task;

        await broker.SendPublishAsync("a/b", new byte[] { 7, 7, 7 }, ct: ct);

        var m1 = await sub1.Reader.ReadAsync(ct);
        var m2 = await sub2.Reader.ReadAsync(ct);
        await Assert.That(
            m1.PayloadMemory.ToArray().AsSpan().SequenceEqual(new byte[] { 7, 7, 7 })).IsTrue();
        await Assert.That(
            m2.PayloadMemory.ToArray().AsSpan().SequenceEqual(new byte[] { 7, 7, 7 })).IsTrue();

        // Each owns a distinct pooled buffer — disposing one must not corrupt the other.
        m1.Dispose();
        await Assert.That(
            m2.PayloadMemory.ToArray().AsSpan().SequenceEqual(new byte[] { 7, 7, 7 })).IsTrue();
        m2.Dispose();
    }
}
