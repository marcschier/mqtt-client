// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client;

internal sealed class WebSocketTransport : IMqttTransport
{
    private readonly ClientWebSocket _socket;
    private readonly WebSocketStream _stream;
    private readonly StreamPipeReader _reader;
    private readonly StreamPipeWriter _writer;

    public WebSocketTransport(ClientWebSocket socket, Uri uri)
    {
        _socket = socket;
        _stream = new WebSocketStream(socket);
        _reader = new StreamPipeReader(_stream);
        _writer = new StreamPipeWriter(_stream);
        RemoteAddress = uri.ToString();
    }

    public PipeReader Input => _reader.Pipe;
    public PipeWriter Output => _writer.Pipe;
    public string? RemoteAddress { get; }

    public async ValueTask DisposeAsync()
    {
        try { await _writer.CompleteAsync().ConfigureAwait(false); } catch { }
        try { await _reader.CompleteAsync().ConfigureAwait(false); } catch { }
        try { await _socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "client",
            CancellationToken.None)
            .ConfigureAwait(false); } catch { }
        _socket.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Adapts a <see cref="ClientWebSocket"/> to a Stream so the existing pipe pump can drive it.
    /// </summary>
    private sealed class WebSocketStream : System.IO.Stream
    {
        private readonly ClientWebSocket _ws;
        public WebSocketStream(ClientWebSocket ws) { _ws = ws; }
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set
            => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException("Use async APIs.");
        public override long Seek(long offset, System.IO.SeekOrigin origin)
            => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException("Use async APIs.");

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var result = await _ws.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return 0;
            return result.Count;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
            => await _ws.SendAsync(
                buffer,
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);
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
