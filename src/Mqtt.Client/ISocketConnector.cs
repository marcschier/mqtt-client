// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client;

/// <summary>
/// Establishes a connected <see cref="Socket"/> to a target host/port. Implementations either
/// connect directly or tunnel through a proxy. Used by the TCP and TLS transport factories so the
/// proxy concern is orthogonal to byte-stream framing.
/// </summary>
internal interface ISocketConnector
{
    ValueTask<Socket> ConnectAsync(string host, int port, CancellationToken cancellationToken);
}

/// <summary>
/// Shared helper that opens a TCP <see cref="Socket"/> with the client's standard options.
/// </summary>
internal static class SocketConnect
{
    public static async ValueTask<Socket> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
#if NETSTANDARD2_1
            using (cancellationToken.Register(static s => ((Socket)s!).Dispose(), socket))
            {
                await socket.ConnectAsync(host, port).ConfigureAwait(false);
            }
#else
            await socket.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
#endif
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
