// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Mqtt.Client.ChaosTests;

public sealed class ChaosProxyOptions
{
    public string BrokerHost { get; set; } = "127.0.0.1";

    public int BrokerPort { get; set; }
}

public sealed class ChaosProxy : IAsyncDisposable
{
    private const int BufferSize = 16 * 1024;
    private const int BlackHoleDelayMs = 100;

    private readonly ConcurrentDictionary<ProxyConnection, byte> _connections = new();
    private readonly TcpListener _listener;
    private readonly object _randomGate = new();
    private readonly ChaosProxyOptions _options;
    private readonly Random _random;
    private readonly CancellationTokenSource _stop = new();
    private int _blackHole;
    private int _latencyJitterMs;
    private int _latencyMs;
    private int _refuseConnections;
    private int _started;
    private int _throttleBytesPerSec;
    private long _corruptionRateBits;
    private long _truncationRateBits;
    private Task? _acceptTask;

    public ChaosProxy(ChaosProxyOptions options)
        : this(options, null)
    {
    }

    public ChaosProxy(ChaosProxyOptions options, Random? random)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _random = random ?? new Random();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        ListenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int ListenPort { get; }

    public bool BlackHole
    {
        get => Volatile.Read(ref _blackHole) != 0;
        set => Volatile.Write(ref _blackHole, value ? 1 : 0);
    }

    public bool RefuseConnections
    {
        get => Volatile.Read(ref _refuseConnections) != 0;
        set => Volatile.Write(ref _refuseConnections, value ? 1 : 0);
    }

    public int LatencyMs
    {
        get => Volatile.Read(ref _latencyMs);
        set => Volatile.Write(ref _latencyMs, Math.Max(0, value));
    }

    public int LatencyJitterMs
    {
        get => Volatile.Read(ref _latencyJitterMs);
        set => Volatile.Write(ref _latencyJitterMs, Math.Max(0, value));
    }

    public int ThrottleBytesPerSec
    {
        get => Volatile.Read(ref _throttleBytesPerSec);
        set => Volatile.Write(ref _throttleBytesPerSec, Math.Max(0, value));
    }

    public double CorruptionRate
    {
        get => ReadRate(ref _corruptionRateBits);
        set => WriteRate(ref _corruptionRateBits, value);
    }

    public double TruncationRate
    {
        get => ReadRate(ref _truncationRateBits);
        set => WriteRate(ref _truncationRateBits, value);
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _acceptTask = AcceptLoopAsync(_stop.Token);
    }

    public async Task StopAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            await _stop.CancelAsync();
        }

        _listener.Stop();
        DropAllConnections();

        if (_acceptTask is not null)
        {
            await IgnoreConnectionErrorsAsync(_acceptTask);
        }
    }

    public void DropAllConnections()
    {
        foreach (var connection in _connections.Keys)
        {
            if (_connections.TryRemove(connection, out _))
            {
                connection.ResetAndDispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop.Dispose();
    }

    private static double ReadRate(ref long bits)
    {
        return BitConverter.Int64BitsToDouble(Volatile.Read(ref bits));
    }

    private static void WriteRate(ref long bits, double value)
    {
        var rate = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
        Volatile.Write(ref bits, BitConverter.DoubleToInt64Bits(rate));
    }

    private static async Task IgnoreConnectionErrorsAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex) when (IsExpectedConnectionError(ex))
        {
        }
    }

    private static bool IsExpectedConnectionError(Exception ex)
    {
        return ex is IOException
            or ObjectDisposedException
            or OperationCanceledException
            or SocketException;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;

            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);

                if (RefuseConnections)
                {
                    client.Dispose();
                    continue;
                }

                _ = HandleClientAsync(client, cancellationToken);
                client = null;
            }
            catch (Exception ex) when (IsExpectedConnectionError(ex))
            {
                client?.Dispose();

                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
                }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken stopToken)
    {
        var broker = new TcpClient();

        try
        {
            await broker.ConnectAsync(_options.BrokerHost, _options.BrokerPort, stopToken);
        }
        catch (Exception ex) when (IsExpectedConnectionError(ex))
        {
            client.Dispose();
            broker.Dispose();
            return;
        }

        var connection = new ProxyConnection(client, broker);

        if (!_connections.TryAdd(connection, 0))
        {
            connection.Dispose();
            return;
        }

        await using var stopRegistration = stopToken.Register(connection.ResetAndDispose);
        var clientPump = PumpAsync(
            connection,
            client.GetStream(),
            broker.GetStream(),
            stopToken);
        var brokerPump = PumpAsync(
            connection,
            broker.GetStream(),
            client.GetStream(),
            stopToken);

        await Task.WhenAny(clientPump, brokerPump);
        RemoveConnection(connection);
        await IgnoreConnectionErrorsAsync(Task.WhenAll(clientPump, brokerPump));
    }

    private async Task PumpAsync(
        ProxyConnection connection,
        NetworkStream source,
        NetworkStream target,
        CancellationToken stopToken)
    {
        var buffer = new byte[BufferSize];
        var token = connection.Token;

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                token,
                stopToken);

            while (!linkedCts.Token.IsCancellationRequested)
            {
                var read = await source.ReadAsync(buffer, linkedCts.Token);

                if (read == 0)
                {
                    break;
                }

                if (BlackHole)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(BlackHoleDelayMs),
                        linkedCts.Token);
                    continue;
                }

                var count = ApplyFaults(buffer, read);
                await DelayForLatencyAsync(linkedCts.Token);

                if (count > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count), linkedCts.Token);
                    await DelayForThrottleAsync(count, linkedCts.Token);
                }
            }
        }
        catch (Exception ex) when (IsExpectedConnectionError(ex))
        {
        }
        finally
        {
            RemoveConnection(connection);
        }
    }

    private int ApplyFaults(byte[] buffer, int count)
    {
        var written = count;

        if (Hits(CorruptionRate) && written > 0)
        {
            var index = Next(0, written);
            buffer[index] ^= 0xFF;
        }

        if (Hits(TruncationRate) && written > 0)
        {
            written = Next(0, written + 1);
        }

        return written;
    }

    private async Task DelayForLatencyAsync(CancellationToken cancellationToken)
    {
        var latencyMs = LatencyMs;
        var jitterMs = LatencyJitterMs;

        if (jitterMs > 0)
        {
            latencyMs += Next(-jitterMs, jitterMs + 1);
        }

        latencyMs = Math.Max(0, latencyMs);

        if (latencyMs > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(latencyMs), cancellationToken);
        }
    }

    private async Task DelayForThrottleAsync(int bytes, CancellationToken cancellationToken)
    {
        var bytesPerSec = ThrottleBytesPerSec;

        if (bytesPerSec <= 0)
        {
            return;
        }

        var delayMs = Math.Max(1, (int)Math.Ceiling(bytes * 1000d / bytesPerSec));
        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }

    private bool Hits(double probability)
    {
        if (probability <= 0)
        {
            return false;
        }

        if (probability >= 1)
        {
            return true;
        }

        lock (_randomGate)
        {
            return _random.NextDouble() < probability;
        }
    }

    private int Next(int minValue, int maxValue)
    {
        lock (_randomGate)
        {
            return _random.Next(minValue, maxValue);
        }
    }

    private void RemoveConnection(ProxyConnection connection)
    {
        if (_connections.TryRemove(connection, out _))
        {
            connection.Dispose();
        }
    }

    private sealed class ProxyConnection : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private int _disposed;

        public ProxyConnection(TcpClient client, TcpClient broker)
        {
            Client = client;
            Broker = broker;
        }

        public TcpClient Client { get; }

        public TcpClient Broker { get; }

        public CancellationToken Token => _cancellation.Token;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _cancellation.Cancel();
            Client.Dispose();
            Broker.Dispose();
            _cancellation.Dispose();
        }

        public void ResetAndDispose()
        {
            SetReset(Client);
            SetReset(Broker);
            Dispose();
        }

        private static void SetReset(TcpClient client)
        {
            try
            {
                client.LingerState = new LingerOption(true, 0);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
