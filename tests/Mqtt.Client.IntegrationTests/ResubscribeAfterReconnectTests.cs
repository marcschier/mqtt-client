// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// Ruggedization regression test: when a reconnect reports session-present=false (e.g. the broker
// restarted and forgot our session), the client must automatically re-establish its subscriptions.
// Without auto-resubscribe the subscriber goes silently deaf after the restart and this test hangs.

using System.Net;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Server;

namespace Mqtt.Client.IntegrationTests;

public class ResubscribeAfterReconnectTests
{
    private static int GetEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static MqttServer CreateServer(int port)
    {
        var options = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        return new MqttServerFactory().CreateMqttServer(options);
    }

    [Test]
    [Timeout(40_000)]
    public async Task Resubscribes_after_session_lost_reconnect(CancellationToken ct)
    {
        var port = GetEphemeralPort();
        var server = CreateServer(port);
        await server.StartAsync();

        var client = MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://127.0.0.1:{port}")
            .WithClientId($"resub-{Guid.NewGuid():N}")
            .WithProtocol(MqttProtocolVersion.V500)
            .WithKeepAlive(2)
            .WithCleanStart(true)
            .WithReconnect(MqttReconnectPolicy.Fixed(TimeSpan.FromMilliseconds(200)))
            .Build();
        await using var _client = client;

        try
        {
            await client.ConnectAsync(ct);
            var sub = await client.SubscribeAsync(
                "itest/resub/#",
                new MqttSubscriptionOptions { QoS = MqttQoS.AtLeastOnce },
                ct);

            // Sanity: delivery works before the restart.
            await client.PublishAsync("itest/resub/a", new byte[] { 1 }, MqttQoS.AtLeastOnce,
                cancellationToken: ct);
            var first = await ReadWithTimeoutAsync(sub, TimeSpan.FromSeconds(10), ct);
            await Assert.That(first).IsNotNull();

            // Restart the broker on the same port: a CleanStart reconnect reports
            // session-present=false, so the broker has forgotten our subscription.
            await server.StopAsync(new MqttServerStopOptions());
            server.Dispose();
            server = CreateServer(port);
            await server.StartAsync();

            // Wait for the client to auto-reconnect.
            await WaitForConnectedAsync(client, TimeSpan.FromSeconds(20), ct);

            // After auto-resubscribe, a freshly published message must still be delivered. Retry a
            // few times to ride out any reconnect blip; without resubscribe this never arrives.
            var delivered = false;
            for (var attempt = 0; attempt < 30 && !delivered; attempt++)
            {
                try
                {
                    await client.PublishAsync("itest/resub/b", new byte[] { 2 },
                        MqttQoS.AtLeastOnce, cancellationToken: ct);
                }
                catch
                {
                    // Mid-reconnect: back off and retry.
                }
                var msg = await ReadWithTimeoutAsync(sub, TimeSpan.FromSeconds(1), ct);
                if (msg is not null && msg.Topic == "itest/resub/b")
                {
                    delivered = true;
                }
            }

            await Assert.That(delivered).IsTrue();
        }
        finally
        {
            await server.StopAsync(new MqttServerStopOptions());
            server.Dispose();
        }
    }

    private static async Task<MqttMessage?> ReadWithTimeoutAsync(
        MqttSubscription sub, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await sub.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task WaitForConnectedAsync(
        MqttClient client, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        while (client.State != MqttConnectionState.Connected)
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(100, cts.Token);
        }
    }
}
