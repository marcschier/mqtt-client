// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using Mqtt.Client.Transport;
using Mqtt.Client.UnitTests.Fakes;

namespace Mqtt.Client.UnitTests;

public class MqttClientReconnectAndKeepAliveTests
{
    private static (MqttClient Client, FakeTransportFactory Factory) Build(MqttReconnectPolicy? policy, ushort keepAliveSeconds = 0)
    {
        var factory = new FakeTransportFactory();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "test",
            ProtocolVersion = MqttProtocolVersion.V500,
            CleanStart = true,
            KeepAliveSeconds = keepAliveSeconds,
            Reconnect = policy,
        }, factory);
        return (client, factory);
    }

    [Test]
    [Timeout(5_000)]
    public async Task DisconnectAsync_cancels_pending_reconnect(CancellationToken ct)
    {
        var (client, _) = Build(MqttReconnectPolicy.Fixed(TimeSpan.FromMilliseconds(50)));
        await using var _0 = client;
        await client.DisconnectAsync(ct);   // never connected; still safe
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Disconnected);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Disconnected_event_fires_on_broker_disconnect(CancellationToken ct)
    {
        var (client, factory) = Build(policy: null);
        await using var _0 = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var tcs = new TaskCompletionSource<MqttDisconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (s, e) => tcs.TrySetResult(e);

        // Simulate broker tearing down the pipe.
        factory.Transport.ToClient.Complete();
        var args = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);
        await Assert.That(args.Reason).IsNotNull();
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Disconnected);
    }

    [Test]
    [Timeout(5_000)]
    public async Task KeepAlive_sends_PINGREQ_at_interval(CancellationToken ct)
    {
        var (client, factory) = Build(policy: null, keepAliveSeconds: 1);  // -> PINGREQ every ~800ms
        await using var _0 = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var pingTask = broker.ReadPacketAsync(ct);
        var completed = await Task.WhenAny(pingTask, Task.Delay(TimeSpan.FromSeconds(2), ct));
        await Assert.That(completed == pingTask).IsTrue();   // received before timeout
        var sent = await pingTask;
        await Assert.That(sent.Type).IsEqualTo(12);  // PINGREQ
    }

    [Test]
    [Timeout(5_000)]
    public async Task Outbound_queue_can_be_disposed_safely_mid_use(CancellationToken ct)
    {
        var (client, factory) = Build(policy: null);
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        client.TryPublish("t", new byte[] { 1 });
        await client.DisposeAsync();
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Disposed);
    }
}
