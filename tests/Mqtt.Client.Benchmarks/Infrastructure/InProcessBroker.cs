// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
using MQTTnet.Server;
using MQTTnet;

namespace Mqtt.Client.Benchmarks.Infrastructure;

/// <summary>
/// Hosts an in-process MQTTnet.Server on a random ephemeral port for benchmarks. If the
/// MQTT_BENCH_BROKER environment variable is set ("host:port"), no broker is started and that
/// endpoint is used instead.
/// </summary>
public sealed class InProcessBroker : IAsyncDisposable
{
    private readonly MqttServer? _server;

    private InProcessBroker(MqttServer? server, string host, int port)
    {
        _server = server;
        Host = host;
        Port = port;
    }

    public string Host { get; }
    public int Port { get; }

    public static async Task<InProcessBroker> StartAsync()
    {
        var env = Environment.GetEnvironmentVariable("MQTT_BENCH_BROKER");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var parts = env.Split(':');
            return new InProcessBroker(server: null, parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 1883);
        }

        var port = GetEphemeralPort();
        var options = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        var server = new MqttServerFactory().CreateMqttServer(options);
        await server.StartAsync();
        return new InProcessBroker(server, "127.0.0.1", port);
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
        if (_server is not null)
        {
            await _server.StopAsync();
            _server.Dispose();
        }
    }
}
