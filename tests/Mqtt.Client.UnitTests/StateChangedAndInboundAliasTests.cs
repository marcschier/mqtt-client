// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using Mqtt.Client.Protocol;
using Mqtt.Client.UnitTests.Fakes;

namespace Mqtt.Client.UnitTests;

public class StateChangedAndInboundAliasTests
{
    private static (MqttClient Client, FakeTransportFactory Factory) Build(ushort topicAliasMax = 0)
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
            TopicAliasMaximum = topicAliasMax,
        }, factory);
        return (client, factory);
    }

    [Test]
    [Timeout(5_000)]
    public async Task StateChanged_fires_on_every_transition(CancellationToken ct)
    {
        var (client, factory) = Build();
        await using var _0 = client;
        var observed = new List<MqttConnectionState>();
        client.StateChanged += (s, state) => { lock (observed) observed.Add(state); };

        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;
        await client.DisconnectAsync(ct);

        // We expect at least Connected -> Disconnected; no duplicates.
        await Assert.That(observed).Contains(MqttConnectionState.Connected);
        await Assert.That(observed).Contains(MqttConnectionState.Disconnected);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Inbound_topic_alias_registers_and_resolves(CancellationToken ct)
    {
        var (client, factory) = Build(topicAliasMax: 10);
        await using var _0 = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var subTask = client.SubscribeAsync("sensors/temp", cancellationToken: ct);
        var subSent = await broker.ReadPacketAsync(ct);
        await broker.SendSubAckAsync(subSent.PacketId, MqttReasonCode.Success, ct);
        var sub = await subTask;

        // First publish: full topic + alias 7 → registers alias.
        await broker.SendPublishWithAliasAsync(topic: "sensors/temp", alias: 7, new byte[] { 2 }, ct: ct);
        var m1 = await sub.Reader.ReadAsync(ct);

        // Second publish: empty topic + alias 7 → resolves to "sensors/temp".
        await broker.SendPublishWithAliasAsync(topic: "", alias: 7, new byte[] { 3 }, ct: ct);
        var m2 = await sub.Reader.ReadAsync(ct);

        await Assert.That(m1.Topic).IsEqualTo("sensors/temp");
        await Assert.That(m2.Topic).IsEqualTo("sensors/temp");
    }

    [Test]
    [Timeout(5_000)]
    public async Task Subscription_identifier_fast_path_dispatches_message(CancellationToken ct)
    {
        var (client, factory) = Build();
        await using var _0 = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var subTask = client.SubscribeAsync("a/b", cancellationToken: ct);
        var subSent = await broker.ReadPacketAsync(ct);
        await broker.SendSubAckAsync(subSent.PacketId, MqttReasonCode.Success, ct);
        var sub = await subTask;

        await Assert.That(sub.Identifier).IsNotNull();

        // Use a non-matching topic to prove dispatch went through the id fast-path (not the trie).
        await broker.SendPublishWithSubIdsAsync(topic: "totally/unrelated", subscriptionIds: new uint[] { sub.Identifier!.Value }, new byte[] { 9 }, ct: ct);
        var m = await sub.Reader.ReadAsync(ct);
        await Assert.That(m.Topic).IsEqualTo("totally/unrelated");
    }
}
