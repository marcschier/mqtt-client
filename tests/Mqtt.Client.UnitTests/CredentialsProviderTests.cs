// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Text;
using System.Threading.Channels;

namespace Mqtt.Client.UnitTests;

public class CredentialsProviderTests
{
    // Returns a fresh username/password pair on each call so reconnects are distinguishable.
    private sealed class SequenceProvider : IMqttCredentialsProvider
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<MqttCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _calls);
            return new ValueTask<MqttCredentials>(MqttCredentials.From($"user{n}", $"token{n}"));
        }
    }

    // Vends a fresh transport per connect so each reconnect attempt gets its own pipes.
    private sealed class MultiConnectFactory : IMqttTransportFactory
    {
        private readonly Channel<FakePipeTransport> _created =
            Channel.CreateUnbounded<FakePipeTransport>();

        public ChannelReader<FakePipeTransport> Created => _created.Reader;

        public ValueTask<IMqttTransport> ConnectAsync(CancellationToken cancellationToken)
        {
            var transport = new FakePipeTransport();
            _created.Writer.TryWrite(transport);
            return new ValueTask<IMqttTransport>(transport);
        }
    }

    [Test]
    [Timeout(5_000)]
    public async Task Provider_supplies_credentials_to_initial_connect(CancellationToken ct)
    {
        var factory = new FakeTransportFactory();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "c",
            ProtocolVersion = MqttProtocolVersion.V500,
            Reconnect = null,
            CredentialsProvider = new SequenceProvider(),
        }, factory);
        await using var _0 = client;
        var broker = new FakeBroker(factory.Transport);

        var connectTask = client.ConnectAsync(ct);
        var connect = await broker.ReadConnectAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        await Assert.That(connect.Username).IsEqualTo("user1");
        await Assert.That(Encoding.UTF8.GetString(connect.Password!)).IsEqualTo("token1");
    }

    [Test]
    [Timeout(10_000)]
    public async Task Provider_is_reinvoked_on_reconnect_with_new_credentials(CancellationToken ct)
    {
        var factory = new MultiConnectFactory();
        var provider = new SequenceProvider();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "c",
            ProtocolVersion = MqttProtocolVersion.V500,
            Reconnect = MqttReconnectPolicy.Fixed(TimeSpan.FromMilliseconds(50)),
            CredentialsProvider = provider,
        }, factory);
        await using var _0 = client;

        // Initial connect presents the first credentials.
        var connectTask = client.ConnectAsync(ct);
        var t1 = await factory.Created.ReadAsync(ct);
        var broker1 = new FakeBroker(t1);
        var connect1 = await broker1.ReadConnectAsync(ct);
        await broker1.SendConnAckAsync(ct: ct);
        await connectTask;
        await Assert.That(connect1.Username).IsEqualTo("user1");
        await Assert.That(Encoding.UTF8.GetString(connect1.Password!)).IsEqualTo("token1");

        // Tear the connection down from the "broker" side to trigger auto-reconnect.
        t1.ToClient.Complete();

        // The reconnect must consult the provider again and present the NEW credentials.
        var t2 = await factory.Created.ReadAsync(ct);
        var broker2 = new FakeBroker(t2);
        var connect2 = await broker2.ReadConnectAsync(ct);
        await broker2.SendConnAckAsync(ct: ct);

        await Assert.That(connect2.Username).IsEqualTo("user2");
        await Assert.That(Encoding.UTF8.GetString(connect2.Password!)).IsEqualTo("token2");
        await Assert.That(provider.Calls).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Provider_overrides_static_username_password(CancellationToken ct)
    {
        var factory = new FakeTransportFactory();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "c",
            ProtocolVersion = MqttProtocolVersion.V500,
            Reconnect = null,
            Username = "static-user",
            Password = Encoding.UTF8.GetBytes("static-pass"),
            CredentialsProvider = new SequenceProvider(),
        }, factory);
        await using var _0 = client;
        var broker = new FakeBroker(factory.Transport);

        var connectTask = client.ConnectAsync(ct);
        var connect = await broker.ReadConnectAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        await Assert.That(connect.Username).IsEqualTo("user1");
        await Assert.That(Encoding.UTF8.GetString(connect.Password!)).IsEqualTo("token1");
    }

    [Test]
    public async Task Builder_WithCredentialsProvider_wires_interface_and_delegate()
    {
        var c1 = MqttClient.CreateBuilder().ConnectTo("mqtt://broker")
            .WithCredentialsProvider(new SequenceProvider()).Build();
        await Assert.That(c1).IsNotNull();

        var c2 = MqttClient.CreateBuilder().ConnectTo("mqtt://broker")
            .WithCredentialsProvider(
                _ => new ValueTask<MqttCredentials>(MqttCredentials.From("u", "p")))
            .Build();
        await Assert.That(c2).IsNotNull();

        await Assert.That(() => MqttClient.CreateBuilder()
            .WithCredentialsProvider((IMqttCredentialsProvider)null!))
            .Throws<ArgumentNullException>();
        await Assert.That(() => MqttClient.CreateBuilder()
            .WithCredentialsProvider((Func<CancellationToken, ValueTask<MqttCredentials>>)null!))
            .Throws<ArgumentNullException>();
    }
}
