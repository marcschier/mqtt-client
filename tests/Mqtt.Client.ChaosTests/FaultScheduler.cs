// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.ChaosTests;

/// <summary>
/// Drives a deterministic (seeded) sequence of network and broker faults against the proxy and
/// broker harness. Between faults it opens a "healthy" window during which the workload is expected
/// to have fully recovered and to make forward progress — the hang watchdog only asserts liveness
/// while <see cref="IsHealthy"/> is true.
/// </summary>
public sealed class FaultScheduler
{
    private static readonly string[] AllFaults =
    {
        "proxy-drop", "proxy-blackhole", "proxy-latency", "proxy-throttle", "proxy-corrupt",
        "proxy-refuse", "broker-restart", "broker-disconnect", "broker-reject",
    };

    private readonly ChaosProxy _proxy;
    private readonly IChaosBroker _broker;
    private readonly Random _random;
    private readonly Action<string> _log;
    private readonly string[] _faults;
    private int _healthy;

    public FaultScheduler(
        ChaosProxy proxy,
        IChaosBroker broker,
        Random random,
        string scenario,
        Action<string> log)
    {
        _proxy = proxy;
        _broker = broker;
        _random = random;
        _log = log;
        _faults = scenario == "all"
            ? AllFaults
            : Array.FindAll(AllFaults, f => f == scenario);
        if (_faults.Length == 0)
        {
            throw new ArgumentException($"Unknown scenario: {scenario}");
        }
    }

    /// <summary>
    /// True when no fault is active and the post-fault recovery grace has elapsed — i.e. the
    /// workload should be flowing. The watchdog asserts forward progress only in this state.
    /// </summary>
    public bool IsHealthy => Volatile.Read(ref _healthy) != 0;

    public int FaultsApplied { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        // Begin with a healthy window so clients connect and subscribe before the first fault.
        await HealthyWindowAsync(NextSeconds(8, 16), ct).ConfigureAwait(false);
        while (!ct.IsCancellationRequested)
        {
            var fault = _faults[_random.Next(_faults.Length)];
            try
            {
                await ApplyFaultAsync(fault, ct).ConfigureAwait(false);
                FaultsApplied++;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"fault '{fault}' raised {ex.GetType().Name}: {ex.Message}");
            }
            // Recovery grace (unhealthy): let the client reconnect + resubscribe before we assert.
            await UnhealthyDelayAsync(NextSeconds(10, 16), ct).ConfigureAwait(false);
            await HealthyWindowAsync(NextSeconds(8, 18), ct).ConfigureAwait(false);
        }
    }

    private async Task ApplyFaultAsync(string fault, CancellationToken ct)
    {
        Volatile.Write(ref _healthy, 0);
        var hold = NextSeconds(2, 8);
        _log($"injecting {fault} (hold {hold:n0}s)");
        switch (fault)
        {
            case "proxy-drop":
                _proxy.DropAllConnections();
                break;
            case "proxy-blackhole":
                _proxy.BlackHole = true;
                await Task.Delay(TimeSpan.FromSeconds(hold), ct).ConfigureAwait(false);
                _proxy.BlackHole = false;
                break;
            case "proxy-latency":
                _proxy.LatencyMs = _random.Next(50, 400);
                _proxy.LatencyJitterMs = _random.Next(0, 150);
                await Task.Delay(TimeSpan.FromSeconds(hold), ct).ConfigureAwait(false);
                _proxy.LatencyMs = 0;
                _proxy.LatencyJitterMs = 0;
                break;
            case "proxy-throttle":
                _proxy.ThrottleBytesPerSec = _random.Next(512, 8192);
                await Task.Delay(TimeSpan.FromSeconds(hold), ct).ConfigureAwait(false);
                _proxy.ThrottleBytesPerSec = 0;
                break;
            case "proxy-corrupt":
                _proxy.CorruptionRate = 0.02 + (_random.NextDouble() * 0.1);
                await Task.Delay(TimeSpan.FromSeconds(hold), ct).ConfigureAwait(false);
                _proxy.CorruptionRate = 0;
                break;
            case "proxy-refuse":
                _proxy.RefuseConnections = true;
                _proxy.DropAllConnections();
                await Task.Delay(TimeSpan.FromSeconds(hold), ct).ConfigureAwait(false);
                _proxy.RefuseConnections = false;
                break;
            case "broker-restart":
                await _broker.RestartAsync().ConfigureAwait(false);
                break;
            case "broker-disconnect":
                await _broker.ForceDisconnectAllAsync().ConfigureAwait(false);
                break;
            case "broker-reject":
                _broker.RejectConnections = true;
                _proxy.DropAllConnections();
                await Task.Delay(TimeSpan.FromSeconds(hold), ct).ConfigureAwait(false);
                _broker.RejectConnections = false;
                break;
        }
    }

    private async Task HealthyWindowAsync(double seconds, CancellationToken ct)
    {
        Volatile.Write(ref _healthy, 1);
        _log($"healthy window {seconds:n0}s");
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
    }

    private async Task UnhealthyDelayAsync(double seconds, CancellationToken ct)
    {
        Volatile.Write(ref _healthy, 0);
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
    }

    private double NextSeconds(int minInclusive, int maxExclusive)
        => _random.Next(minInclusive, maxExclusive) + _random.NextDouble();
}
