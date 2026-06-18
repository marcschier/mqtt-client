// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
namespace Mqtt.Client.UnitTests;

public class MqttClientFakeTransportTests
{
    private static (MqttClient Client, FakePipeTransport Transport, FakeBroker Broker) Build(
        MqttProtocolVersion v = MqttProtocolVersion.V500)
    {
        var factory = new FakeTransportFactory();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "test",
            ProtocolVersion = v,
            CleanStart = true,
            KeepAliveSeconds = 0,
            Reconnect = null,
        }, factory);
        return (client, factory.Transport, new FakeBroker(factory.Transport, v));
    }

    [Test]
    [Timeout(5_000)]
    public async Task ConnectAsync_succeeds_when_broker_replies_with_success_connack(
        CancellationToken ct)
    {
        var (client, _, broker) = Build();
        await using var _ = client;
        var connectTask = client.ConnectAsync(ct);
        var sent = await broker.ReadPacketAsync(ct);
        await Assert.That(sent.Type).IsEqualTo(1);   // CONNECT
        await broker.SendConnAckAsync(MqttReasonCode.Success, ct: ct);
        var result = await connectTask;
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Connected);
    }

    [Test]
    [Timeout(5_000)]
    public async Task ConnectAsync_returns_failure_when_broker_rejects(CancellationToken ct)
    {
        var (client, _, broker) = Build();
        await using var _ = client;
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(MqttReasonCode.NotAuthorized, ct: ct);
        var result = await connectTask;
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.ReasonCode).IsEqualTo(MqttReasonCode.NotAuthorized);
    }

    [Test]
    [Timeout(5_000)]
    public async Task PublishAsync_qos1_completes_on_puback(CancellationToken ct)
    {
        var (client, _, broker) = Build();
        await using var _ = client;
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var pubTask = client.PublishAsync(
            "topic/qos1",
            new byte[] { 1, 2, 3 },
            MqttQoS.AtLeastOnce,
            cancellationToken: ct);
        var sent = await broker.ReadPacketAsync(ct);
        await Assert.That(sent.Type).IsEqualTo(3);  // PUBLISH
        await broker.SendPubAckAsync(sent.PacketId, ct: ct);
        var result = await pubTask;
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    [Timeout(5_000)]
    public async Task PublishAsync_qos2_completes_on_pubcomp(CancellationToken ct)
    {
        var (client, _, broker) = Build();
        await using var _ = client;
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var pubTask = client.PublishAsync(
            "topic/qos2",
            new byte[] { 9 },
            MqttQoS.ExactlyOnce,
            cancellationToken: ct);
        var sent = await broker.ReadPacketAsync(ct);
        await Assert.That(sent.Type).IsEqualTo(3);
        await broker.SendPubRecAsync(sent.PacketId, ct);
        // Client should send PUBREL.
        var rel = await broker.ReadPacketAsync(ct);
        await Assert.That(rel.Type).IsEqualTo(6);  // PUBREL
        await broker.SendPubCompAsync(rel.PacketId, ct);
        var result = await pubTask;
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    [Timeout(5_000)]
    public async Task TryPublish_returns_true_for_qos0_when_connected(CancellationToken ct)
    {
        var (client, _, broker) = Build();
        await using var _ = client;
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;
        var ok = client.TryPublish("topic/qos0", new byte[] { 1, 2 });
        await Assert.That(ok).IsTrue();
        var sent = await broker.ReadPacketAsync(ct);
        await Assert.That(sent.Type).IsEqualTo(3);
        await Assert.That(sent.Topic).IsEqualTo("topic/qos0");
    }

    [Test]
    [Timeout(5_000)]
    public async Task SubscribeAsync_yields_inbound_publish(CancellationToken ct)
    {
        var (client, _, broker) = Build();
        await using var _ = client;
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var subTask = client.SubscribeAsync("inbound/+/topic", cancellationToken: ct);
        var sent = await broker.ReadPacketAsync(ct);
        await Assert.That(sent.Type).IsEqualTo(8);  // SUBSCRIBE
        await broker.SendSubAckAsync(sent.PacketId, MqttReasonCode.GrantedQoS1, ct);
        var subscription = await subTask;

        await broker.SendPublishAsync("inbound/dev1/topic", new byte[] { 0x01 }, ct: ct);
        var msg = await subscription.Reader.ReadAsync(ct);
        await Assert.That(msg.Topic).IsEqualTo("inbound/dev1/topic");
        await Assert.That(msg.Payload.Length).IsEqualTo(1);
        // Skip explicit subscription dispose — the client's await using will tear down loops.
    }

    [Test]
    [Timeout(5_000)]
    public async Task DisconnectAsync_transitions_state(CancellationToken ct)
    {
        var (client, _, broker) = Build();
        await using var _ = client;
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;
        await client.DisconnectAsync(ct);
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Disconnected);
    }

    [Test]
    [Timeout(5_000)]
    public async Task ConnectAsync_throws_when_already_connecting(CancellationToken ct)
    {
        var (client, _, broker) = Build();
        await using var _ = client;
        var t1 = client.ConnectAsync(ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(ct));
        await broker.SendConnAckAsync(ct: ct);
        try { await t1; } catch { }
    }

    [Test]
    [Timeout(2_000)]
    public async Task PublishAsync_throws_when_not_connected(CancellationToken ct)
    {
        _ = ct;
        var (client, _, _) = Build();
        await using var _2 = client;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PublishAsync("t", new byte[] { 1 }).AsTask());
    }

    [Test]
    [Timeout(2_000)]
    public async Task TryPublish_throws_when_not_connected(CancellationToken ct)
    {
        _ = ct;
        var (client, _, _) = Build();
        await using var _2 = client;
        await Assert.That(() => client.TryPublish("t", new byte[] { 1 }))
            .Throws<InvalidOperationException>();
    }
}
