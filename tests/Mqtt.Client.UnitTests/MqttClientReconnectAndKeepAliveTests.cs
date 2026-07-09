// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

public class MqttClientReconnectAndKeepAliveTests
{
    private static (MqttClient Client, FakeTransportFactory Factory) Build(
        MqttReconnectPolicy? policy,
        ushort keepAliveSeconds = 0)
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

        var tcs = new TaskCompletionSource<MqttDisconnectedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
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
        // -> PINGREQ every ~800ms
        var (client, factory) = Build(policy: null, keepAliveSeconds: 1);
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

    [Test]
    [Timeout(5_000)]
    public async Task Rejected_connack_resets_state_to_Disconnected(CancellationToken ct)
    {
        var (client, factory) = Build(policy: null);
        await using var _0 = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(MqttReasonCode.NotAuthorized, ct: ct);
        var result = await connectTask;

        await Assert.That(result.IsSuccess).IsFalse();
        // Regression: a rejected CONNACK must return the client to Disconnected, not leave it stuck
        // in Connecting. The reconnect supervisor only attempts while state==Disconnected, so a stuck
        // Connecting state starves it and wedges the client permanently.
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Disconnected);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Failed_connect_resets_state_and_allows_retry(CancellationToken ct)
    {
        var factory = new FailableTransportFactory { Fail = true };
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "test",
            ProtocolVersion = MqttProtocolVersion.V500,
            CleanStart = true,
            KeepAliveSeconds = 0,
            Reconnect = null,
        }, factory);
        await using var _0 = client;

        await Assert.That(async () => await client.ConnectAsync(ct)).Throws<IOException>();
        // Regression: a throwing (re)connect must land back in Disconnected, otherwise a single
        // failed attempt (e.g. a corrupted CONNECT/CONNACK during a fault) wedges the client.
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Disconnected);

        // The client is not wedged: a subsequent attempt can CAS out of Disconnected and connect.
        factory.Fail = false;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        var result = await connectTask;

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Connected);
    }

    private sealed class FailableTransportFactory : IMqttTransportFactory
    {
        public FakePipeTransport Transport { get; } = new();
        public bool Fail { get; set; }

        public ValueTask<IMqttTransport> ConnectAsync(CancellationToken cancellationToken)
        {
            if (Fail) throw new IOException("connect failed");
            return new ValueTask<IMqttTransport>(Transport);
        }
    }
}
