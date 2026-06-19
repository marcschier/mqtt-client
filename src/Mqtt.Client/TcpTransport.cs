// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client;

internal sealed class TcpTransport : IMqttTransport
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly StreamPipeReader _reader;
    private readonly StreamPipeWriter _writer;

    public TcpTransport(Socket socket, int pauseWriterThreshold = 1024 * 1024)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: true);
        _reader = new StreamPipeReader(_stream, pauseWriterThreshold);
        _writer = new StreamPipeWriter(_stream, pauseWriterThreshold);
        RemoteAddress = (socket.RemoteEndPoint?.ToString());
    }

    public PipeReader Input => _reader.Pipe;
    public PipeWriter Output => _writer.Pipe;
    public string? RemoteAddress { get; }

    public async ValueTask DisposeAsync()
    {
        try { await _writer.CompleteAsync().ConfigureAwait(false); } catch { }
        try { await _reader.CompleteAsync().ConfigureAwait(false); } catch { }
        await _stream.DisposeAsync().ConfigureAwait(false);
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

/// <summary>
/// Wraps a <see cref="System.IO.Stream"/> in a <see cref="Pipe"/> and pumps reads/writes in a
/// background loop. Internal helper used by transports that don't natively speak Pipelines.
/// </summary>
internal sealed class StreamPipeReader : IAsyncDisposable
{
    // Larger segments reduce allocation churn for large payloads (one segment instead of many).
    private const int MinSegmentSize = 8 * 1024;

    private readonly Pipe _pipe;
    private readonly System.IO.Stream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pumpTask;

    public StreamPipeReader(System.IO.Stream stream, int pauseWriterThreshold = 1024 * 1024)
    {
        _stream = stream;
        _pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: pauseWriterThreshold,
            resumeWriterThreshold: pauseWriterThreshold / 2,
            minimumSegmentSize: MinSegmentSize,
            useSynchronizationContext: false));
        _pumpTask = PumpAsync(_cts.Token);
    }

    public PipeReader Pipe => _pipe.Reader;

    private async Task PumpAsync(CancellationToken ct)
    {
        var writer = _pipe.Writer;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var mem = writer.GetMemory(MinSegmentSize);
                var n = await _stream.ReadAsync(mem, ct).ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }
                writer.Advance(n);
                var flush = await writer.FlushAsync(ct).ConfigureAwait(false);
                if (flush.IsCompleted) break;
            }
            await writer.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex).ConfigureAwait(false);
        }
    }

    public async ValueTask CompleteAsync()
    {
        _cts.Cancel();
        try { await _pumpTask.ConfigureAwait(false); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await CompleteAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}

internal sealed class StreamPipeWriter : IAsyncDisposable
{
    private readonly Pipe _pipe;
    private readonly System.IO.Stream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pumpTask;

    public StreamPipeWriter(System.IO.Stream stream, int pauseWriterThreshold = 1024 * 1024)
    {
        _stream = stream;
        _pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: pauseWriterThreshold,
            resumeWriterThreshold: pauseWriterThreshold / 2,
            minimumSegmentSize: 8 * 1024,
            useSynchronizationContext: false));
        _pumpTask = PumpAsync(_cts.Token);
    }

    public PipeWriter Pipe => _pipe.Writer;

    private async Task PumpAsync(CancellationToken ct)
    {
        var reader = _pipe.Reader;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;
                if (buffer.Length > 0)
                {
                    foreach (var segment in buffer)
                    {
                        await _stream.WriteAsync(segment, ct).ConfigureAwait(false);
                    }
                    await _stream.FlushAsync(ct).ConfigureAwait(false);
                }
                reader.AdvanceTo(buffer.End);
                if (result.IsCompleted) break;
            }
            await reader.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await reader.CompleteAsync(ex).ConfigureAwait(false);
        }
    }

    public async ValueTask CompleteAsync()
    {
        try { await _pipe.Writer.CompleteAsync().ConfigureAwait(false); } catch { }
        try { await _pumpTask.ConfigureAwait(false); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await CompleteAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
