// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client;

/// <summary>
/// Connects straight to the target host/port with no proxy. Default connector.
/// </summary>
internal sealed class DefaultConnector : ISocketConnector
{
    public static DefaultConnector Instance { get; } = new();

    public ValueTask<Socket> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
        => SocketConnect.ConnectAsync(host, port, cancellationToken);
}
