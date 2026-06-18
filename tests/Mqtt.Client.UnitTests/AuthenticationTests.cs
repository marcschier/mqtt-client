// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Text;
using Mqtt.Client.Protocol;
using Mqtt.Client.Protocol.Packets;
using Mqtt.Client.UnitTests.Fakes;

namespace Mqtt.Client.UnitTests;

public class AuthenticationTests
{
    private sealed class StepHandler : IMqttAuthenticationHandler
    {
        private readonly Func<int, ReadOnlyMemory<byte>?, MqttAuthenticationResult> _next;
        public string Method { get; }
        public int CallCount { get; private set; }
        public List<byte[]?> Challenges { get; } = new();

        public StepHandler(
            string method,
            Func<int, ReadOnlyMemory<byte>?, MqttAuthenticationResult> next)
        {
            Method = method;
            _next = next;
        }

        public ValueTask<MqttAuthenticationResult> ContinueAsync(
            ReadOnlyMemory<byte>? challenge,
            CancellationToken cancellationToken)
        {
            Challenges.Add(challenge?.ToArray());
            var idx = CallCount++;
            return new ValueTask<MqttAuthenticationResult>(_next(idx, challenge));
        }
    }

    private static (MqttClient Client, FakeTransportFactory Factory) Build(
        IMqttAuthenticationHandler? handler,
        int maxRoundTrips = 5)
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
            AuthenticationHandler = handler,
            MaxAuthRoundTrips = maxRoundTrips,
        }, factory);
        return (client, factory);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Connect_with_single_step_handler_succeeds(CancellationToken ct)
    {
        var handler = new StepHandler(
            "PLAIN",
            (i, c) => MqttAuthenticationResult.Final(Encoding.UTF8.GetBytes("u:p")));
        var (client, factory) = Build(handler);
        await using var _ = client;
        var broker = new FakeBroker(factory.Transport);

        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct); // CONNECT
        await broker.SendConnAckAsync(MqttReasonCode.Success, ct: ct);
        var connack = await connectTask;

        await Assert.That(connack.IsSuccess).IsTrue();
        await Assert.That(handler.CallCount).IsEqualTo(1);
        await Assert.That(handler.Challenges[0]).IsNull();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Multi_roundtrip_handler_completes(CancellationToken ct)
    {
        var handler = new StepHandler("SCRAM-SHA-256", (i, c) => i switch
        {
            0 => MqttAuthenticationResult.Continue(Encoding.UTF8.GetBytes("client_first")),
            1 => MqttAuthenticationResult.Continue(Encoding.UTF8.GetBytes("client_final")),
            _ => MqttAuthenticationResult.Final(),
        });
        var (client, factory) = Build(handler);
        await using var _ = client;
        var broker = new FakeBroker(factory.Transport);

        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct); // CONNECT (contains initial auth)
        await broker.SendAuthAsync(
            MqttReasonCode.ContinueAuthentication,
            "SCRAM-SHA-256",
            Encoding.UTF8.GetBytes("server_first"),
            ct);
        var clientAuth1 = await broker.ReadDecodedPacketAsync(ct);
        await Assert.That(clientAuth1).IsNotNull();
        await broker.SendAuthAsync(
            MqttReasonCode.ContinueAuthentication,
            "SCRAM-SHA-256",
            Encoding.UTF8.GetBytes("server_final"),
            ct);
        var clientAuth2 = await broker.ReadDecodedPacketAsync(ct);
        await Assert.That(clientAuth2).IsNotNull();
        await broker.SendConnAckAsync(MqttReasonCode.Success, ct: ct);

        var connack = await connectTask;
        await Assert.That(connack.IsSuccess).IsTrue();
        await Assert.That(handler.CallCount).IsEqualTo(3);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Broker_rejects_method_returns_failure_connack(CancellationToken ct)
    {
        var handler = new StepHandler("BOGUS", (i, c) => MqttAuthenticationResult.Final());
        var (client, factory) = Build(handler);
        await using var _ = client;
        var broker = new FakeBroker(factory.Transport);

        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(MqttReasonCode.BadAuthenticationMethod, ct: ct);
        var connack = await connectTask;

        await Assert.That(connack.IsSuccess).IsFalse();
        await Assert.That(connack.ReasonCode).IsEqualTo(MqttReasonCode.BadAuthenticationMethod);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Handler_abort_on_initial_throws_MqttAuthenticationException(
        CancellationToken ct)
    {
        var handler = new StepHandler(
            "PLAIN",
            (i, c) => MqttAuthenticationResult.Abort(MqttReasonCode.NotAuthorized, "no creds"));
        var (client, factory) = Build(handler);
        await using var _ = client;

        await Assert.That(async () => await client.ConnectAsync(ct))
            .ThrowsExactly<MqttAuthenticationException>();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Reauthenticate_on_live_connection_completes(CancellationToken ct)
    {
        var (client, factory) = Build(handler: null);
        await using var _ = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(MqttReasonCode.Success, ct: ct);
        await connectTask;

        var handler = new StepHandler(
            "PLAIN",
            (i, c) => MqttAuthenticationResult.Final(Encoding.UTF8.GetBytes("token")));
        var reauthTask = client.ReauthenticateAsync(handler, ct);
        await broker.ReadDecodedPacketAsync(ct); // client's AUTH 0x19
        await broker.SendAuthAsync(
            MqttReasonCode.Success,
            "PLAIN",
            Encoding.UTF8.GetBytes("server-ack"),
            ct);
        var data = await reauthTask;
        await Assert.That(handler.CallCount).IsEqualTo(1);
        await Assert.That(data.HasValue).IsTrue();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Reauthenticate_requires_v5_protocol(CancellationToken ct)
    {
        var factory = new FakeTransportFactory();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "t",
            ProtocolVersion = MqttProtocolVersion.V311,
            KeepAliveSeconds = 0,
            Reconnect = null,
        }, factory);
        await using var _ = client;
        var broker = new FakeBroker(factory.Transport, MqttProtocolVersion.V311);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(MqttReasonCode.Success, ct: ct);
        await connectTask;

        var handler = new StepHandler("PLAIN", (i, c) => MqttAuthenticationResult.Final());
        await Assert.That(async () => await client.ReauthenticateAsync(handler, ct))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Round_trip_cap_exceeded_throws_MqttAuthenticationException(
        CancellationToken ct)
    {
        // Handler always wants to Continue → broker drives a never-ending exchange.
        var handler = new StepHandler(
            "PLAIN",
            (i, c) => MqttAuthenticationResult.Continue(new byte[] { (byte)i }));
        var (client, factory) = Build(handler, maxRoundTrips: 2);
        await using var _ = client;
        var broker = new FakeBroker(factory.Transport);

        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        // Send Continue indefinitely until the cap kicks in.
        try
        {
            for (var i = 0; i < 10; i++)
            {
                await broker.SendAuthAsync(
                    MqttReasonCode.ContinueAuthentication,
                    "PLAIN",
                    new byte[] { (byte)i },
                    ct);
                try { await broker.ReadDecodedPacketAsync(ct); } catch { break; }
            }
        }
        catch { /* pipe might close */ }

        await Assert.That(async () => await connectTask)
            .ThrowsExactly<MqttAuthenticationException>();
    }

    private sealed class GatedHandler : IMqttAuthenticationHandler
    {
        public string Method => "PLAIN";
        public TaskCompletionSource<MqttAuthenticationResult> Gate { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<MqttAuthenticationResult> ContinueAsync(
            ReadOnlyMemory<byte>? challenge,
            CancellationToken cancellationToken)
            => await Gate.Task.ConfigureAwait(false);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Reauthenticate_concurrent_call_throws_invalid_operation(CancellationToken ct)
    {
        var (client, factory) = Build(handler: null);
        await using var _ = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(MqttReasonCode.Success, ct: ct);
        await connectTask;

        var slow = new GatedHandler();
        var first = client.ReauthenticateAsync(slow, ct);
        // Yield so the first call has registered _pendingAuth before the second attempt.
        await Task.Delay(50, ct);

        var fastHandler = new StepHandler("PLAIN", (i, c) => MqttAuthenticationResult.Final());
        await Assert.That(async () => await client.ReauthenticateAsync(fastHandler, ct))
            .ThrowsExactly<InvalidOperationException>();

        // Cleanup the first call.
        slow.Gate.SetResult(MqttAuthenticationResult.Final());
        try { await broker.ReadDecodedPacketAsync(ct); } catch { }
        try { await first; } catch { }
    }
}
