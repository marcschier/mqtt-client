// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Text;

namespace Mqtt.Client.UnitTests;

public class AllocationEliminationTests
{
    private static (MqttClient Client, FakeTransportFactory Factory) Build()
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
        return (client, factory);
    }

    private static async Task ConnectAsync(
        MqttClient client, FakeBroker broker, CancellationToken ct)
    {
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;
    }

    private static byte[] EncodePublish(string topic, byte[] payload)
    {
        var pkt = new PublishPacket
        {
            Topic = topic,
            QoS = MqttQoS.AtMostOnce,
            PayloadMemory = payload,
        };
        var w = new MqttBufferWriter(payload.Length + 64);
        try
        {
            MqttPacketEncoder.EncodePublish(pkt, MqttProtocolVersion.V500, ref w);
            return w.WrittenSpan.ToArray();
        }
        finally
        {
            w.Dispose();
        }
    }

    [Test]
    public async Task Decoder_slice_mode_does_not_copy_the_payload()
    {
        var payload = new byte[64 * 1024];
        new Random(1).NextBytes(payload);
        var bytes = EncodePublish("big/t", payload);
        var seq = new ReadOnlySequence<byte>(bytes);

        // Warm up JIT / type init so the measured call only reflects per-decode allocations.
        MqttPacketDecoder.TryDecode(
            seq, MqttProtocolVersion.V500, int.MaxValue, poolPayload: true, out _, out _, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();
        MqttPacketDecoder.TryDecode(
            seq, MqttProtocolVersion.V500, int.MaxValue, poolPayload: true,
            out var packet, out _, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var pkt = (PublishPacket)packet!;
        await Assert.That((int)pkt.Payload.Length).IsEqualTo(64 * 1024);
        // Slice mode must not allocate a 64 KB copy — only the small packet object graph.
        await Assert.That(allocated).IsLessThan(8 * 1024);
    }

    [Test]
    public async Task Decoder_copy_mode_allocates_the_payload()
    {
        var payload = new byte[64 * 1024];
        var bytes = EncodePublish("big/t", payload);
        var seq = new ReadOnlySequence<byte>(bytes);

        MqttPacketDecoder.TryDecode(
            seq, MqttProtocolVersion.V500, int.MaxValue, poolPayload: false, out _, out _, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();
        MqttPacketDecoder.TryDecode(
            seq, MqttProtocolVersion.V500, int.MaxValue, poolPayload: false,
            out var packet, out _, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        _ = (PublishPacket)packet!;
        // Copy mode allocates the 64 KB payload array.
        await Assert.That(allocated).IsGreaterThanOrEqualTo(64 * 1024);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Inline_handler_delivers_payload(CancellationToken ct)
    {
        var (client, factory) = Build();
        await using var _client = client;
        var broker = new FakeBroker(factory.Transport);
        await ConnectAsync(client, broker, ct);

        var received = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var subTask = client.SubscribeAsync("inline/t", msg =>
        {
            received.TrySetResult(msg.PayloadMemory.ToArray());
            return default;
        }, cancellationToken: ct);
        var subSent = await broker.ReadPacketAsync(ct);
        await broker.SendSubAckAsync(subSent.PacketId, MqttReasonCode.Success, ct);
        await subTask;

        await broker.SendPublishAsync("inline/t", new byte[] { 5, 6, 7 }, ct: ct);
        var got = await received.Task;
        await Assert.That(got.AsSpan().SequenceEqual(new byte[] { 5, 6, 7 })).IsTrue();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Inline_handler_subscription_has_no_channel_reader(CancellationToken ct)
    {
        var (client, factory) = Build();
        await using var _client = client;
        var broker = new FakeBroker(factory.Transport);
        await ConnectAsync(client, broker, ct);

        var subTask = client.SubscribeAsync("inline/x", _ => default, cancellationToken: ct);
        var subSent = await broker.ReadPacketAsync(ct);
        await broker.SendSubAckAsync(subSent.PacketId, MqttReasonCode.Success, ct);
        var sub = await subTask;

        await Assert.That(() => _ = sub.Reader).Throws<InvalidOperationException>();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Channel_delivery_preserves_correlation_after_advance(CancellationToken ct)
    {
        // Pooled (default) channel delivery must copy CorrelationData out of the receive buffer so
        // it remains valid after the read loop advances the pipe.
        var (client, factory) = Build();
        await using var _client = client;
        var broker = new FakeBroker(factory.Transport);
        await ConnectAsync(client, broker, ct);

        var subTask = client.SubscribeAsync("corr/t", cancellationToken: ct);
        var subSent = await broker.ReadPacketAsync(ct);
        await broker.SendSubAckAsync(subSent.PacketId, MqttReasonCode.Success, ct);
        var sub = await subTask;

        var correlation = Encoding.UTF8.GetBytes("req-42");
        await broker.SendPublishWithCorrelationAsync(
            "corr/t", correlation, new byte[] { 1 }, ct: ct);

        var msg = await sub.Reader.ReadAsync(ct);
        // Force more inbound traffic so the pipe buffer that backed the slice is recycled.
        await broker.SendPublishAsync("corr/t", new byte[] { 2 }, ct: ct);
        _ = await sub.Reader.ReadAsync(ct);

        await Assert.That(msg.Properties).IsNotNull();
        await Assert.That(msg.Properties!.CorrelationData.HasValue).IsTrue();
        await Assert.That(
            msg.Properties.CorrelationData!.Value.ToArray().AsSpan().SequenceEqual(correlation))
            .IsTrue();
        msg.Dispose();
    }

    [Test]
    public async Task Encode_publish_header_is_allocation_free()
    {
        var packet = new PublishPacket
        {
            Topic = "alloc/header",
            QoS = MqttQoS.AtMostOnce,
            PayloadMemory = new byte[16],
        };

        // Warm up: prime the JIT and seed the ArrayPool so the measured loop reuses one buffer.
        for (var i = 0; i < 16; i++)
        {
            var warm = new MqttBufferWriter(128);
            MqttPacketEncoder.EncodePublishHeader(packet, MqttProtocolVersion.V500, ref warm);
            warm.Dispose();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            var w = new MqttBufferWriter(128);
            MqttPacketEncoder.EncodePublishHeader(packet, MqttProtocolVersion.V500, ref w);
            w.Dispose();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // The writer is now a value type (stack) and the backing array is pooled + returned each
        // iteration, so a warmed-up encode allocates nothing on the managed heap.
        await Assert.That(allocated).IsEqualTo(0L);
    }
}
