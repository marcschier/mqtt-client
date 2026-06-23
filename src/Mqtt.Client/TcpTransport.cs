// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace Mqtt.Client;

/// <summary>
/// Plain-TCP transport. Bridges the raw socket to a duplex <see cref="Pipe"/> via
/// Pipelines.Sockets.Unofficial's <see cref="SocketConnection"/>, which drives reads and writes with
/// a pooled <c>SocketAsyncEventArgs</c> directly over the pipe buffers (zero-copy scatter-gather, no
/// <c>NetworkStream</c>).
/// </summary>
internal sealed class TcpTransport : IMqttTransport
{
    private readonly SocketConnection _connection;

    public TcpTransport(Socket socket, int pauseWriterThreshold = 1024 * 1024)
    {
        var pipeOptions = new PipeOptions(
            pauseWriterThreshold: pauseWriterThreshold,
            resumeWriterThreshold: pauseWriterThreshold / 2,
            minimumSegmentSize: 8 * 1024,
            useSynchronizationContext: false);
        _connection = SocketConnection.Create(socket, pipeOptions);
        RemoteAddress = socket.RemoteEndPoint?.ToString();
    }

    public PipeReader Input => _connection.Input;
    public PipeWriter Output => _connection.Output;
    public string? RemoteAddress { get; }

    public ValueTask DisposeAsync()
    {
        // SocketConnection.Dispose() shuts down and closes the underlying socket.
        _connection.Dispose();
        return default;
    }
}

internal sealed class TcpTransportFactory : IMqttTransportFactory
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _pauseThreshold;
    private readonly ISocketConnector _connector;

    public TcpTransportFactory(
        string host,
        int port,
        int pauseThreshold = 1024 * 1024,
        ISocketConnector? connector = null)
    {
        _host = host;
        _port = port;
        _pauseThreshold = pauseThreshold;
        _connector = connector ?? DefaultConnector.Instance;
    }

    public async ValueTask<IMqttTransport> ConnectAsync(CancellationToken cancellationToken)
    {
        var socket = await _connector.ConnectAsync(_host, _port, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return new TcpTransport(socket, _pauseThreshold);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
