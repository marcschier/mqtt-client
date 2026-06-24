// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Exercises session persistence: an in-flight QoS&gt;0 publish is saved, survives a disconnect,
/// and is redelivered (DUP = 1, same packet id) on a Session-Present reconnect — with the original
/// awaiter completing on the post-reconnect ack — or discarded on a clean-session reconnect.
/// </summary>
public class PersistenceRedeliveryTests
{
    private static MqttClient BuildClient(
        MultiConnectFakeFactory factory, IPersistentSessionStore store)
        => new(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "t",
            ProtocolVersion = MqttProtocolVersion.V500,
            CleanStart = false,
            KeepAliveSeconds = 0,
            Reconnect = MqttReconnectPolicy.Fixed(TimeSpan.FromMilliseconds(50)),
        }, factory, null, store);

    [Test]
    [Timeout(10_000)]
    public async Task Pending_qos1_publish_redelivered_with_dup_on_session_present_reconnect(
        CancellationToken ct)
    {
        var factory = new MultiConnectFakeFactory();
        var client = BuildClient(factory, new InMemorySessionStore());
        await using var _0 = client;

        // Connect 1.
        var connectTask = client.ConnectAsync(ct);
        var t1 = await factory.Created.ReadAsync(ct);
        var broker1 = new FakeBroker(t1);
        await broker1.ReadConnectAsync(ct);
        await broker1.SendConnAckAsync(ct: ct);
        await connectTask;

        // Publish QoS 1; it parks awaiting the ack and the broker receives it.
        var pub = client.PublishAsync(
            "t/1", new byte[] { 9 }, MqttQoS.AtLeastOnce, cancellationToken: ct).AsTask();
        var sent1 = await broker1.ReadPacketAsync(ct);
        await Assert.That(sent1.Type).IsEqualTo(3);
        var id = sent1.PacketId;

        // Drop the connection before acking.
        t1.ToClient.Complete();

        // Connect 2 with Session Present: the publish is redelivered with DUP set and the same id.
        var t2 = await factory.Created.ReadAsync(ct);
        var broker2 = new FakeBroker(t2);
        await broker2.ReadConnectAsync(ct);
        await broker2.SendConnAckAsync(sessionPresent: true, ct: ct);

        var sent2 = await broker2.ReadPacketAsync(ct);
        await Assert.That(sent2.Type).IsEqualTo(3);                 // PUBLISH
        await Assert.That(sent2.PacketId).IsEqualTo(id);            // same packet id
        await Assert.That(sent2.FirstByte & 0x08).IsEqualTo(0x08);  // DUP flag set

        // Acking the redelivery completes the original await (await-continuity).
        await broker2.SendPubAckAsync(id, ct: ct);
        var result = await pub;
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    [Timeout(10_000)]
    public async Task Pending_publish_discarded_on_clean_session_reconnect(CancellationToken ct)
    {
        var factory = new MultiConnectFakeFactory();
        var client = BuildClient(factory, new InMemorySessionStore());
        await using var _0 = client;

        var connectTask = client.ConnectAsync(ct);
        var t1 = await factory.Created.ReadAsync(ct);
        var broker1 = new FakeBroker(t1);
        await broker1.ReadConnectAsync(ct);
        await broker1.SendConnAckAsync(ct: ct);
        await connectTask;

        var pub = client.PublishAsync(
            "t/1", new byte[] { 9 }, MqttQoS.AtLeastOnce, cancellationToken: ct).AsTask();
        await broker1.ReadPacketAsync(ct);
        t1.ToClient.Complete();

        // Reconnect WITHOUT Session Present: the in-flight publish is abandoned.
        var t2 = await factory.Created.ReadAsync(ct);
        var broker2 = new FakeBroker(t2);
        await broker2.ReadConnectAsync(ct);
        await broker2.SendConnAckAsync(sessionPresent: false, ct: ct);

        await Assert.That(async () => await pub).Throws<MqttConnectionException>();
    }

    [Test]
    [Timeout(10_000)]
    public async Task Inbound_qos2_dedup_survives_session_present_reconnect(CancellationToken ct)
    {
        var factory = new MultiConnectFakeFactory();
        var client = BuildClient(factory, new InMemorySessionStore());
        await using var _0 = client;

        var count = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Connect 1 + inline subscription (survives reconnect client-side).
        var connectTask = client.ConnectAsync(ct);
        var t1 = await factory.Created.ReadAsync(ct);
        var broker1 = new FakeBroker(t1);
        await broker1.ReadConnectAsync(ct);
        await broker1.SendConnAckAsync(ct: ct);
        await connectTask;

        var subTask = client.SubscribeAsync(
            "q2/persist",
            _ =>
            {
                var c = Interlocked.Increment(ref count);
                if (c == 1) first.TrySetResult();
                else if (c == 2) second.TrySetResult();
                return default;
            },
            cancellationToken: ct);
        var subSent = await broker1.ReadPacketAsync(ct);
        await broker1.SendSubAckAsync(subSent.PacketId, MqttReasonCode.Success, ct);
        await subTask;

        // Inbound QoS 2 (id 5): delivered once, PUBREC sent, receipt persisted.
        await broker1.SendPublishAsync(
            "q2/persist", new byte[] { 1 }, MqttQoS.ExactlyOnce, packetId: 5, ct: ct);
        var rec1 = await broker1.ReadPacketAsync(ct);
        await Assert.That(rec1.Type).IsEqualTo(5);   // PUBREC
        await first.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);

        // Drop before PUBREL; reconnect with Session Present restores the receipt state.
        t1.ToClient.Complete();
        var t2 = await factory.Created.ReadAsync(ct);
        var broker2 = new FakeBroker(t2);
        await broker2.ReadConnectAsync(ct);
        await broker2.SendConnAckAsync(sessionPresent: true, ct: ct);

        // Redelivery of id 5: re-acked with PUBREC but NOT delivered again.
        await broker2.SendPublishAsync(
            "q2/persist", new byte[] { 1 }, MqttQoS.ExactlyOnce, packetId: 5, ct: ct);
        var rec2 = await broker2.ReadPacketAsync(ct);
        await Assert.That(rec2.Type).IsEqualTo(5);   // PUBREC
        await broker2.SendPubRelAsync(5, ct: ct);
        var comp = await broker2.ReadPacketAsync(ct);
        await Assert.That(comp.Type).IsEqualTo(7);   // PUBCOMP

        // A genuinely new message is still delivered — proving the subscription works and only the
        // redelivery was suppressed (count reaches exactly 2, not 3).
        await broker2.SendPublishAsync("q2/persist", new byte[] { 2 }, ct: ct);
        await second.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);
        await Assert.That(count).IsEqualTo(2);
    }

    private sealed class ThrowOnceSaveStore : IPersistentSessionStore
    {
        private int _calls;
        public ValueTask SavePendingPublishAsync(ushort packetId, MqttMessage message)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                throw new System.IO.IOException("disk full");
            }
            return default;
        }
        public ValueTask RemovePendingPublishAsync(ushort packetId) => default;
        public ValueTask<IReadOnlyList<(ushort PacketId, MqttMessage Message)>>
            ListPendingPublishesAsync()
            => new(System.Array.Empty<(ushort, MqttMessage)>());
        public ValueTask ClearAsync() => default;
    }

    [Test]
    [Timeout(10_000)]
    public async Task Failed_persistence_save_does_not_leak_the_receive_quota(CancellationToken ct)
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
        }, factory, null, new ThrowOnceSaveStore());
        await using var _0 = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadConnectAsync(ct);
        await broker.SendConnAckWithLimitsAsync(receiveMaximum: 1, ct: ct);
        await connectTask;

        // The first persistence save throws; the acquired Receive-Maximum slot must be released.
        await Assert.That(async () => await client.PublishAsync(
                "t/1", new byte[] { 1 }, MqttQoS.AtLeastOnce, cancellationToken: ct))
            .Throws<System.IO.IOException>();

        // With a leaked slot (quota = 1) this next publish would block forever; it must complete.
        var pub = client.PublishAsync(
            "t/2", new byte[] { 2 }, MqttQoS.AtLeastOnce, cancellationToken: ct).AsTask();
        var sent = await broker.ReadPacketAsync(ct);
        await Assert.That(sent.Type).IsEqualTo(3);
        await broker.SendPubAckAsync(sent.PacketId, ct: ct);
        await pub;
    }
}
