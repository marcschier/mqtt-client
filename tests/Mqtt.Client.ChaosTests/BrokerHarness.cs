// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Mqtt.Client.ChaosTests;

public sealed class BrokerHarness : IChaosBroker
{
    private MqttServer _server;

    private BrokerHarness(MqttServer server, int port)
    {
        _server = server;
        Port = port;
    }

    public int Port { get; }

    public bool RejectConnections { get; set; }

    public static async Task<BrokerHarness> StartAsync(int? port = null)
    {
        var selectedPort = port ?? GetEphemeralPort();
        var server = CreateServer(selectedPort);
        var broker = new BrokerHarness(server, selectedPort);
        server.ValidatingConnectionAsync += broker.ValidateConnectionAsync;
        await server.StartAsync();
        return broker;
    }

    public async Task RestartAsync()
    {
        await StopServerAsync();
        await RestartServerAsync();
    }

    public async Task ForceDisconnectAllAsync()
    {
        var clients = await _server.GetClientsAsync();
        var options = new MqttServerClientDisconnectOptions
        {
            ReasonCode = MqttDisconnectReasonCode.AdministrativeAction,
        };

        foreach (var client in clients)
        {
            await _server.DisconnectClientAsync(client.Id, options);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopServerAsync();
        _server.Dispose();
    }

    private static MqttServer CreateServer(int port)
    {
        var options = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        return new MqttServerFactory().CreateMqttServer(options);
    }

    private static int GetEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task RestartServerAsync()
    {
        _server.Dispose();
        _server = CreateServer(Port);
        _server.ValidatingConnectionAsync += ValidateConnectionAsync;
        await _server.StartAsync();
    }

    private async Task StopServerAsync()
    {
        if (_server.IsStarted)
        {
            await _server.StopAsync(new MqttServerStopOptions());
        }
    }

    private Task ValidateConnectionAsync(ValidatingConnectionEventArgs args)
    {
        if (RejectConnections)
        {
            args.ReasonCode = MqttConnectReasonCode.NotAuthorized;
            args.ReasonString = "BrokerHarness RejectConnections is enabled.";
        }

        return Task.CompletedTask;
    }
}
