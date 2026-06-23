// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace Mqtt.Client;

/// <summary>
/// Connects over TCP+TLS (MQTTS). The encrypted <see cref="SslStream"/> is adapted to a duplex pipe
/// via Pipelines.Sockets.Unofficial's <see cref="StreamConnection"/> (a raw socket-backed zero-copy
/// pipe isn't possible once TLS owns the byte stream).
/// </summary>
internal sealed class TlsTransport : IMqttTransport
{
    private readonly SslStream _ssl;
    private readonly Socket _socket;
    private readonly IDuplexPipe _pipe;

    public TlsTransport(Socket socket, SslStream sslStream)
    {
        _socket = socket;
        _ssl = sslStream;
        var pipeOptions = new PipeOptions(
            pauseWriterThreshold: 1024 * 1024,
            resumeWriterThreshold: 512 * 1024,
            minimumSegmentSize: 8 * 1024,
            useSynchronizationContext: false);
        _pipe = StreamConnection.GetDuplex(sslStream, pipeOptions);
        RemoteAddress = socket.RemoteEndPoint?.ToString();
    }

    public PipeReader Input => _pipe.Input;
    public PipeWriter Output => _pipe.Output;
    public string? RemoteAddress { get; }

    public async ValueTask DisposeAsync()
    {
        try { _pipe.Output.Complete(); } catch { }
        try { _pipe.Input.Complete(); } catch { }
        await _ssl.DisposeAsync().ConfigureAwait(false);
        _socket.Dispose();
    }
}

internal sealed class TlsTransportFactory : IMqttTransportFactory
{
    private readonly string _host;
    private readonly int _port;
    private readonly SslClientAuthenticationOptions _options;
    private readonly ISocketConnector _connector;

    public TlsTransportFactory(
        string host,
        int port,
        SslClientAuthenticationOptions options,
        ISocketConnector? connector = null)
    {
        _host = host;
        _port = port;
        _options = options;
        _connector = connector ?? DefaultConnector.Instance;
    }

    public async ValueTask<IMqttTransport> ConnectAsync(CancellationToken cancellationToken)
    {
        var socket = await _connector.ConnectAsync(_host, _port, cancellationToken)
            .ConfigureAwait(false);
        SslStream? ssl = null;
        try
        {
            ssl = new SslStream(
                new NetworkStream(socket, ownsSocket: false),
                leaveInnerStreamOpen: false);
#if NETSTANDARD2_1
            await ssl.AuthenticateAsClientAsync(
                _options.TargetHost!,
                _options.ClientCertificates,
                _options.EnabledSslProtocols,
                checkCertificateRevocation: _options.CertificateRevocationCheckMode
                    != X509RevocationMode.NoCheck).ConfigureAwait(false);
#else
            await ssl.AuthenticateAsClientAsync(_options, cancellationToken).ConfigureAwait(false);
#endif
            return new TlsTransport(socket, ssl);
        }
        catch
        {
            ssl?.Dispose();
            socket.Dispose();
            throw;
        }
    }
}
