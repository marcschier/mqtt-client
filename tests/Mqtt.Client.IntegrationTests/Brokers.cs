// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// Lets every broker-agnostic integration scenario run against both the MQTTnet in-process broker
// and the embeddable Mqtt.Client.Testing broker (MqttTestBroker), for detailed cross-broker interop.

using System.Net;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Server;
using Mqtt.Client.Testing;

namespace Mqtt.Client.IntegrationTests;

/// <summary>Which broker implementation a parameterized test runs against.</summary>
public enum BrokerKind
{
    Mqttnet,
    Testing,
}

/// <summary>A running broker on a known loopback port; disposal stops it.</summary>
internal interface IIntegrationBroker : IAsyncDisposable
{
    int Port { get; }
}

internal static class Brokers
{
    /// <summary>
    /// Starts a broker of the given kind. Pass a non-zero <paramref name="port"/> to bind a
    /// specific port (e.g. to restart a broker on the same port); 0 picks an ephemeral one.
    /// </summary>
    public static async Task<IIntegrationBroker> StartAsync(BrokerKind kind, int port = 0)
        => kind switch
        {
            BrokerKind.Mqttnet => await MqttnetBroker.StartAsync(port),
            BrokerKind.Testing => await TestingBroker.StartAsync(port),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static int EphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class MqttnetBroker : IIntegrationBroker
    {
        private readonly MqttServer _server;

        private MqttnetBroker(MqttServer server, int port)
        {
            _server = server;
            Port = port;
        }

        public int Port { get; }

        public static async Task<IIntegrationBroker> StartAsync(int port)
        {
            if (port == 0) port = EphemeralPort();
            var options = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(port)
                .Build();
            var server = new MqttServerFactory().CreateMqttServer(options);
            await server.StartAsync();
            return new MqttnetBroker(server, port);
        }

        public async ValueTask DisposeAsync()
        {
            await _server.StopAsync(new MqttServerStopOptions());
            _server.Dispose();
        }
    }

    private sealed class TestingBroker : IIntegrationBroker
    {
        private readonly MqttTestBroker _broker;

        private TestingBroker(MqttTestBroker broker) => _broker = broker;

        public int Port => _broker.Port;

        public static async Task<IIntegrationBroker> StartAsync(int port)
        {
            var broker = await MqttTestBroker.StartAsync(new MqttTestBrokerOptions { Port = port });
            return new TestingBroker(broker);
        }

        public ValueTask DisposeAsync() => _broker.DisposeAsync();
    }
}
