// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.AspNetCore;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Mqtt.Client.ChaosTests;

public sealed class WsBrokerHarness : IChaosBroker
{
    private WebApplication _app;
    private MqttServer _server;

    private WsBrokerHarness(WebApplication app, MqttServer server, int port)
    {
        _app = app;
        _server = server;
        Port = port;
    }

    public int Port { get; }

    public bool RejectConnections { get; set; }

    public X509Certificate2? ServerCertificate => null;

    public static async Task<WsBrokerHarness> StartAsync(int? port = null)
    {
        var selectedPort = port ?? GetEphemeralPort();
        var (app, server) = await CreateAndStartAppAsync(selectedPort);
        var broker = new WsBrokerHarness(app, server, selectedPort);
        server.ValidatingConnectionAsync += broker.ValidateConnectionAsync;
        return broker;
    }

    public static async Task<bool> SelfTestAsync()
    {
        await using var broker = await StartAsync();
        using var client = new MqttClientFactory().CreateMqttClient();
        var topic = $"chaos/ws/self-test/{Guid.NewGuid():N}";
        var payload = "ok";
        var received = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        client.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == topic &&
                Encoding.UTF8.GetString(args.ApplicationMessage.Payload) == payload)
            {
                received.TrySetResult(true);
            }

            return Task.CompletedTask;
        };

        var options = new MqttClientOptionsBuilder()
            .WithWebSocketServer(o => o.WithUri($"ws://127.0.0.1:{broker.Port}/mqtt"))
            .Build();

        await client.ConnectAsync(options);
        await client.SubscribeAsync(topic);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();
        await client.PublishAsync(message);

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await client.DisconnectAsync();
        return completed == received.Task && await received.Task;
    }

    public async Task RestartAsync()
    {
        await StopAppAsync();
        await _app.DisposeAsync();
        var (app, server) = await CreateAndStartAppAsync(Port);
        _app = app;
        _server = server;
        _server.ValidatingConnectionAsync += ValidateConnectionAsync;
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
        await StopAppAsync();
        await _app.DisposeAsync();
    }

    private static async Task<(WebApplication App, MqttServer Server)> CreateAndStartAppAsync(
        int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
        });

        builder.Services.AddMqttServer(options => options.WithoutDefaultEndpoint());
        builder.Services.AddMqttConnectionHandler();
        builder.Services.AddConnections();

        var app = builder.Build();
        app.UseRouting();
        app.UseWebSockets();
        app.MapMqtt("/mqtt");

        await app.StartAsync();
        return (app, app.Services.GetRequiredService<MqttServer>());
    }

    private static int GetEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task StopAppAsync()
    {
        await _app.StopAsync();
    }

    private Task ValidateConnectionAsync(ValidatingConnectionEventArgs args)
    {
        if (RejectConnections)
        {
            args.ReasonCode = MqttConnectReasonCode.NotAuthorized;
            args.ReasonString = "WsBrokerHarness RejectConnections is enabled.";
        }

        return Task.CompletedTask;
    }
}
