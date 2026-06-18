// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// Integration tests run against an in-process MQTTnet server. Each test spins up a fresh
// broker on a random ephemeral port for isolation.

using System.Net;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Server;

namespace Mqtt.Client.IntegrationTests;

internal sealed class InProcessBroker : IAsyncDisposable
{
    private readonly MqttServer _server;
    public int Port { get; }

    private InProcessBroker(MqttServer server, int port)
    {
        _server = server;
        Port = port;
    }

    public static async Task<InProcessBroker> StartAsync()
    {
        var port = GetEphemeralPort();
        var options = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        var server = new MqttServerFactory().CreateMqttServer(options);
        await server.StartAsync();
        return new InProcessBroker(server, port);
    }

    private static int GetEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _server.StopAsync();
        _server.Dispose();
    }
}

public class PublishSubscribeTests
{
    [Test]
    [Timeout(15_000)]
    public async Task QoS0_RoundTrip(CancellationToken ct)
    {
        await using var broker = await InProcessBroker.StartAsync();
        var client = MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://localhost:{broker.Port}")
            .WithClientId($"test-{Guid.NewGuid():N}")
            .WithProtocol(MqttProtocolVersion.V500)
            .Build();
        await using var _ = client;

        var connect = await client.ConnectAsync();
        await Assert.That(connect.IsSuccess).IsTrue();

        var sub = await client.SubscribeAsync("itest/qos0");
        await using var __ = sub;

        var payload = new byte[] { 1, 2, 3 };
        await client.PublishAsync("itest/qos0", payload, MqttQoS.AtMostOnce);

        var received = await sub.Reader.ReadAsync();
        await Assert.That(received.Topic).IsEqualTo("itest/qos0");
        await Assert.That(received.PayloadMemory.Length).IsEqualTo(3);
    }

    [Test]
    [Timeout(15_000)]
    public async Task QoS1_AwaitsAck(CancellationToken ct)
    {
        await using var broker = await InProcessBroker.StartAsync();
        var client = MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://localhost:{broker.Port}")
            .WithClientId($"test-{Guid.NewGuid():N}")
            .Build();
        await using var _ = client;
        await client.ConnectAsync();
        var result = await client.PublishAsync("itest/qos1", new byte[] { 9 }, MqttQoS.AtLeastOnce);
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    [Timeout(15_000)]
    public async Task QoS2_RoundTrip(CancellationToken ct)
    {
        await using var broker = await InProcessBroker.StartAsync();
        var client = MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://localhost:{broker.Port}")
            .WithClientId($"test-{Guid.NewGuid():N}")
            .Build();
        await using var _ = client;
        await client.ConnectAsync();
        var sub = await client.SubscribeAsync(
            "itest/qos2",
            new MqttSubscriptionOptions { QoS = MqttQoS.ExactlyOnce });
        await using var __ = sub;
        var result = await client.PublishAsync("itest/qos2", new byte[] { 7 }, MqttQoS.ExactlyOnce);
        await Assert.That(result.IsSuccess).IsTrue();
        var received = await sub.Reader.ReadAsync();
        await Assert.That(received.Topic).IsEqualTo("itest/qos2");
    }
}
