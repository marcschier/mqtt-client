// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// Validates the WebSocket transport (devlooped/WebSocketPipe) by round-tripping a large, multi-frame
// payload through the real WebSocketTransportFactory against a loopback WebSocket echo server.

using System.Net;
using System.Net.WebSockets;

namespace Mqtt.Client.IntegrationTests;

public class WebSocketTransportTests
{
    [Test]
    [Timeout(20_000)]
    public async Task WebSocket_LargePayload_RoundTrip(CancellationToken ct)
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var serverTask = RunWebSocketEchoServerAsync(listener, ct);

        var factory = new WebSocketTransportFactory(new Uri($"ws://localhost:{port}/"));
        await using var transport = await factory.ConnectAsync(ct);

        var payload = new byte[150_000];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)((i * 17) + 3);
        }

        var writeTask = Task.Run(async () =>
        {
            await transport.Output.WriteAsync(payload, ct);
            await transport.Output.FlushAsync(ct);
        }, ct);

        var received = new byte[payload.Length];
        var got = 0;
        while (got < received.Length)
        {
            var result = await transport.Input.ReadAsync(ct);
            var buffer = result.Buffer;
            var consumed = 0L;
            foreach (var segment in buffer)
            {
                if (got >= received.Length) break;
                var take = Math.Min(segment.Length, received.Length - got);
                segment.Span.Slice(0, take).CopyTo(received.AsSpan(got));
                got += take;
                consumed += take;
            }
            transport.Input.AdvanceTo(buffer.GetPosition(consumed));
            if (result.IsCompleted && got < received.Length) break;
        }

        await writeTask;
        await Assert.That(got).IsEqualTo(payload.Length);
        await Assert.That(received.AsSpan().SequenceEqual(payload)).IsTrue();
    }

    private static async Task RunWebSocketEchoServerAsync(
        HttpListener listener,
        CancellationToken ct)
    {
        try
        {
            var context = await listener.GetContextAsync();
            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }
            var wsContext = await context.AcceptWebSocketAsync("mqtt");
            using var ws = wsContext.WebSocket;
            var buffer = new byte[16 * 1024];
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                await ws.SendAsync(
                    buffer.AsMemory(0, result.Count),
                    WebSocketMessageType.Binary,
                    result.EndOfMessage,
                    ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException
            or WebSocketException or HttpListenerException or ObjectDisposedException)
        {
        }
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
