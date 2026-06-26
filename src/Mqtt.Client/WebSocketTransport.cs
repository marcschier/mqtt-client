// Copyright (c) 2026 marcschier. Licensed under the MIT License.

#if !NETSTANDARD2_0   // WebSocketPipe targets netstandard2.1+; ws unsupported on ns2.0

using System;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Devlooped.Net;

namespace Mqtt.Client;

/// <summary>
/// Connects over WebSockets (ws/wss). Adapts the <see cref="ClientWebSocket"/> to a duplex pipe via
/// devlooped/WebSocketPipe, which frames outgoing writes as binary messages (as MQTT-over-WebSocket
/// requires) and reassembles incoming frames into the input pipe.
/// </summary>
internal sealed class WebSocketTransport : IMqttTransport
{
    private readonly IWebSocketPipe _pipe;
    private readonly Task _runTask;

    public WebSocketTransport(ClientWebSocket socket, Uri uri)
    {
        _pipe = WebSocketPipe.Create(socket, closeWhenCompleted: true);
        _runTask = _pipe.RunAsync();
        RemoteAddress = uri.ToString();
    }

    public PipeReader Input => _pipe.Input;
    public PipeWriter Output => _pipe.Output;
    public string? RemoteAddress { get; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _pipe.CompleteAsync(WebSocketCloseStatus.NormalClosure, "client")
                .ConfigureAwait(false);
        }
        catch { }
        try { await _runTask.ConfigureAwait(false); } catch { }
        _pipe.Dispose();
    }
}

internal sealed class WebSocketTransportFactory : IMqttTransportFactory
{
    private readonly Uri _uri;
    private readonly Action<ClientWebSocketOptions>? _configure;

    public WebSocketTransportFactory(Uri uri, Action<ClientWebSocketOptions>? configure = null)
    {
        _uri = uri;
        _configure = configure;
    }

    public async ValueTask<IMqttTransport> ConnectAsync(CancellationToken cancellationToken)
    {
        var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol("mqtt");
        _configure?.Invoke(ws.Options);
        try
        {
            await ws.ConnectAsync(_uri, cancellationToken).ConfigureAwait(false);
            return new WebSocketTransport(ws, _uri);
        }
        catch
        {
            ws.Dispose();
            throw;
        }
    }
}
#endif
