// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics;

namespace Mqtt.Client.ChaosTests;

/// <summary>
/// A single self-publishing/self-subscribing worker. It subscribes to its own topic space and
/// publishes an incrementing sequence number (cycling QoS 0/1/2). Because the broker echoes the
/// worker's own publishes back, a healthy worker continuously receives — so a stalled receive clock
/// (e.g. a subscription that was never restored after a reconnect) is detectable independently of
/// the publish clock.
/// </summary>
public sealed class ChaosWorker
{
    private readonly MqttClient _client;
    private readonly string _topic;
    private readonly Action<string> _log;
    private long _lastPublishTicks;
    private long _lastReceiveTicks;

    public ChaosWorker(string id, MqttClient client, Action<string> log)
    {
        Id = id;
        _client = client;
        _topic = $"chaos/{id}/data";
        _log = log;
        var now = Stopwatch.GetTimestamp();
        _lastPublishTicks = now;
        _lastReceiveTicks = now;
    }

    public string Id { get; }
    public long Published { get; private set; }
    public long Acked { get; private set; }
    public long Received { get; private set; }
    public Exception? Fault { get; private set; }
    public MqttConnectionState ClientState => _client.State;

    public double SecondsSincePublish => Elapsed(Volatile.Read(ref _lastPublishTicks));
    public double SecondsSinceReceive => Elapsed(Volatile.Read(ref _lastReceiveTicks));

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await ConnectWithRetryAsync(ct).ConfigureAwait(false);
            var sub = await SubscribeWithRetryAsync(ct).ConfigureAwait(false);
            var reader = ReadLoopAsync(sub, ct);
            var writer = PublishLoopAsync(ct);
            await Task.WhenAll(reader, writer).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Fault = ex;
            _log($"worker {Id} faulted: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { await _client.DisposeAsync().ConfigureAwait(false); }
            catch { /* best-effort teardown */ }
        }
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await _client.ConnectAsync(cts.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<MqttSubscription> SubscribeWithRetryAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                return await _client.SubscribeAsync(
                    $"chaos/{Id}/#",
                    new MqttSubscriptionOptions { QoS = MqttQoS.AtLeastOnce, Capacity = 4096 },
                    cts.Token).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadLoopAsync(MqttSubscription sub, CancellationToken ct)
    {
        try
        {
            await foreach (var msg in sub.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                Received++;
                Volatile.Write(ref _lastReceiveTicks, Stopwatch.GetTimestamp());
                msg.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PublishLoopAsync(CancellationToken ct)
    {
        var seq = 0L;
        while (!ct.IsCancellationRequested)
        {
            var qos = (MqttQoS)(seq % 3);
            var payload = ArrayPool<byte>.Shared.Rent(16);
            try
            {
                BitConverter.TryWriteBytes(payload.AsSpan(0, 8), seq);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                // Per-publish timeout kept well under the liveness stall threshold (maxStallSec) so a
                // single publish whose ack the broker drops mid-chaos is abandoned and retried before
                // it trips the hang watchdog — the client waits for a QoS>0 ack up to the caller's
                // token, so this is the application's responsibility, not a client-side timeout.
                cts.CancelAfter(TimeSpan.FromSeconds(6));
                await _client.PublishAsync(
                    _topic,
                    new ReadOnlyMemory<byte>(payload, 0, 16),
                    qos,
                    cancellationToken: cts.Token).ConfigureAwait(false);
                Published++;
                if (qos != MqttQoS.AtMostOnce)
                {
                    Acked++;
                }
                Volatile.Write(ref _lastPublishTicks, Stopwatch.GetTimestamp());
                seq++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Disconnected / timed out mid-fault: back off and let auto-reconnect recover.
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payload);
            }
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static double Elapsed(long sinceTicks)
        => (Stopwatch.GetTimestamp() - sinceTicks) * 1.0 / Stopwatch.Frequency;
}

/// <summary>
/// Owns the pool of <see cref="ChaosWorker"/> instances and aggregates their liveness for the hang
/// watchdog and the end-of-run invariant checks.
/// </summary>
public sealed class Workload
{
    private readonly List<ChaosWorker> _workers = new();
    private readonly List<Task> _tasks = new();

    public IReadOnlyList<ChaosWorker> Workers => _workers;

    public void Start(
        int count,
        Func<string, MqttClient> clientFactory,
        Action<string> log,
        CancellationToken ct)
    {
        for (var i = 0; i < count; i++)
        {
            var id = $"w{i}";
            var worker = new ChaosWorker(id, clientFactory(id), log);
            _workers.Add(worker);
            _tasks.Add(Task.Run(() => worker.RunAsync(ct), ct));
        }
    }

    public Task WhenAllAsync() => Task.WhenAll(_tasks);

    public long TotalPublished => Sum(static w => w.Published);
    public long TotalAcked => Sum(static w => w.Acked);
    public long TotalReceived => Sum(static w => w.Received);

    /// <summary>
    /// Asserts every worker has made forward progress on BOTH its publish and receive clocks. Call
    /// only during a healthy window. The clocks measure time since the last SUCCESS, so a worker that
    /// recovers resets to zero — a stall only survives here if the worker made no progress for the
    /// whole threshold, i.e. a genuine hang, not transient fault recovery. The receive threshold is
    /// looser than the publish one: a deaf subscription can be broker-induced (a reference broker that
    /// acks a resubscribe but drops it after a restart) and self-heals on the next reconnect, whereas
    /// the client owns the publish path directly.
    /// </summary>
    public bool CheckLiveness(double maxPublishStall, double maxReceiveStall, out string reason)
    {
        foreach (var w in _workers)
        {
            if (w.Fault is not null)
            {
                reason = $"worker {w.Id} faulted: {w.Fault.GetType().Name}: {w.Fault.Message}";
                return false;
            }
            if (w.SecondsSincePublish > maxPublishStall)
            {
                reason = $"worker {w.Id} publish stalled {w.SecondsSincePublish:n1}s "
                    + $"(> {maxPublishStall:n0}s) during a healthy window [state={w.ClientState}]";
                return false;
            }
            if (w.SecondsSinceReceive > maxReceiveStall)
            {
                reason = $"worker {w.Id} receive stalled {w.SecondsSinceReceive:n1}s "
                    + $"(> {maxReceiveStall:n0}s) during a healthy window [state={w.ClientState}]"
                    + " — subscription not recovered?";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    private long Sum(Func<ChaosWorker, long> selector)
    {
        long total = 0;
        foreach (var w in _workers)
        {
            total += selector(w);
        }
        return total;
    }
}
