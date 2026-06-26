// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client.Testing;

/// <summary>One MQTT application message routed through the broker.</summary>
internal sealed class Message
{
    public required string Topic { get; init; }
    public required byte[] Payload { get; init; }
    public required byte Qos { get; init; }
    public required bool Retain { get; init; }

    /// <summary>Raw MQTT 5 property bytes (without the length prefix), or null.</summary>
    public byte[]? V5Properties { get; init; }
}

/// <summary>
/// An embeddable, in-process MQTT 3.1.1 + 5.0 broker for tests. Each instance listens on its own
/// loopback port and keeps entirely independent state, so many brokers can run concurrently.
/// </summary>
public sealed class MqttTestBroker : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<BrokerConnection> _connections = new();
    private readonly Dictionary<string, Message> _retained = new(StringComparer.Ordinal);
    private Task _acceptLoop = Task.CompletedTask;
    private int _disposed;

    private MqttTestBroker(TcpListener listener, MqttTestBrokerOptions options, int port)
    {
        _listener = listener;
        Options = options;
        Host = "127.0.0.1";
        Port = port;
        Uri = new Uri($"mqtt://127.0.0.1:{port}");
    }

    internal MqttTestBrokerOptions Options { get; }

    /// <summary>The loopback host the broker is listening on (always <c>127.0.0.1</c>).</summary>
    public string Host { get; }

    /// <summary>The TCP port the broker is listening on.</summary>
    public int Port { get; }

    /// <summary>The broker endpoint as <c>mqtt://127.0.0.1:{port}</c>.</summary>
    public Uri Uri { get; }

    /// <summary>Raised after a client's CONNECT is accepted.</summary>
    public event EventHandler<MqttTestBrokerClient>? ClientConnected;

    /// <summary>Raised after a client disconnects (gracefully or otherwise).</summary>
    public event EventHandler<MqttTestBrokerClient>? ClientDisconnected;

    /// <summary>Starts a broker listening on <see cref="MqttTestBrokerOptions.Port"/>.</summary>
    public static Task<MqttTestBroker> StartAsync(
        MqttTestBrokerOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new MqttTestBrokerOptions();
        var listener = new TcpListener(IPAddress.Loopback, options.Port);
        // Allow the same port to be re-bound promptly (e.g. a test restarting the broker on a
        // fixed port) without tripping over connections lingering in TIME_WAIT.
        listener.Server.SetSocketOption(
            SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var broker = new MqttTestBroker(listener, options, port);
        broker._acceptLoop = Task.Run(broker.AcceptLoopAsync, CancellationToken.None);
        return Task.FromResult(broker);
    }

    /// <summary>Forcibly disconnects the connection(s) with the given client id, if any.</summary>
    public void DisconnectClient(string clientId)
    {
        BrokerConnection[] snapshot;
        lock (_gate) snapshot = _connections.ToArray();
        foreach (var c in snapshot)
        {
            if (string.Equals(c.ClientId, clientId, StringComparison.Ordinal)) c.Close();
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }

            try { client.NoDelay = true; } catch (SocketException) { /* best effort */ }
            var conn = new BrokerConnection(this, client);
            lock (_gate) _connections.Add(conn);
            _ = conn.RunAsync(_cts.Token);
        }
    }

    internal bool Authenticate(string clientId, string? username, ReadOnlyMemory<byte> password)
    {
        var auth = Options.Authenticate;
        if (auth is not null) return auth(clientId, username, password);
        return Options.AllowAnonymous;
    }

    internal void OnConnected(BrokerConnection conn)
        => ClientConnected?.Invoke(this, new MqttTestBrokerClient(conn.ClientId));

    internal void Remove(BrokerConnection conn, bool wasConnected)
    {
        lock (_gate) _connections.Remove(conn);
        if (wasConnected) ClientDisconnected?.Invoke(this, new MqttTestBrokerClient(conn.ClientId));
    }

    /// <summary>Routes a published message: updates the retained store, then fans out.</summary>
    internal async Task RouteAsync(Message msg)
    {
        if (msg.Retain)
        {
            lock (_retained)
            {
                if (msg.Payload.Length == 0) _retained.Remove(msg.Topic);
                else _retained[msg.Topic] = msg;
            }
        }

        BrokerConnection[] snapshot;
        lock (_gate) snapshot = _connections.ToArray();
        foreach (var c in snapshot)
        {
            await c.TryDeliverAsync(msg, retainedFlag: false).ConfigureAwait(false);
        }
    }

    /// <summary>Returns the retained messages whose topic matches the given filter.</summary>
    internal List<Message> RetainedMatching(string filter)
    {
        var result = new List<Message>();
        lock (_retained)
        {
            foreach (var kvp in _retained)
            {
                if (TopicFilter.Matches(filter, kvp.Key)) result.Add(kvp.Value);
            }
        }
        return result;
    }

    /// <summary>Stops the broker, closes the listener and drops all connections.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        try { _listener.Stop(); } catch (SocketException) { /* best effort */ }

        BrokerConnection[] snapshot;
        lock (_gate) snapshot = _connections.ToArray();
        foreach (var c in snapshot) c.Close();

        try { await _acceptLoop.ConfigureAwait(false); } catch { /* best effort */ }
        _cts.Dispose();
    }
}
