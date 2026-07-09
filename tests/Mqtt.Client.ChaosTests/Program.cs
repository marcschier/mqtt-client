// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Diagnostics;
using Mqtt.Client;
using Mqtt.Client.ChaosTests;

var config = ChaosConfig.Parse(args);
var sw = Stopwatch.StartNew();
void Log(string m) => Console.WriteLine($"[{sw.Elapsed:hh\\:mm\\:ss}] {m}");

Console.WriteLine("=== Mqtt.Client chaos / soak ===");
Console.WriteLine($"config: {config}");
Console.WriteLine($"*** SEED={config.Seed} (re-pass --seed {config.Seed} to reproduce) ***");

var rng = new Random(config.Seed);

IChaosBroker broker;
try
{
    broker = await CreateBrokerAsync(config.Transport);
}
catch (NotSupportedException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
await using var _broker = broker;
Log($"broker ({config.Transport}) listening on 127.0.0.1:{broker.Port}");

await using var proxy = new ChaosProxy(
    new ChaosProxyOptions { BrokerPort = broker.Port }, rng);
proxy.Start();
Log($"chaos proxy listening on 127.0.0.1:{proxy.ListenPort} -> broker {broker.Port}");

MqttClient BuildClient(string id)
    => BuildClientForTransport(config, proxy.ListenPort, id);

using var metrics = new MetricsCollector();
var warmup = TimeSpan.FromSeconds(Math.Min(60, Math.Max(10, config.Duration.TotalSeconds / 4)));
var leak = new LeakDetector(warmup);

using var runCts = new CancellationTokenSource(config.Duration);
var workload = new Workload();
workload.Start(config.Clients, BuildClient, Log, runCts.Token);

var scheduler = new FaultScheduler(proxy, broker, rng, config.Scenario, Log);
var schedulerTask = scheduler.RunAsync(runCts.Token);

var violations = new List<string>();
const double monitorIntervalSec = 2;
const double leakIntervalSec = 15;
const double watchdogGraceSec = 8;   // healthy must persist this long before asserting liveness
// The stall clocks measure time since the last SUCCESS, so a recovering worker resets to 0 and only a
// genuine, non-recovering hang survives to the threshold. Publish is client-owned, so it is held to a
// tight bound; receive is looser because a deaf subscription can be broker-induced (a reference broker
// acking a resubscribe but dropping it after a restart) and self-heals on the next reconnect — well
// inside one fault cycle — whereas a real client receive hang is unbounded (the pre-fix regressions
// stalled for minutes).
const double maxPublishStallSec = 20;
const double maxReceiveStallSec = 45;
double? healthySince = null;
var lastLeak = 0.0;
leak.Sample(0);

try
{
    while (!runCts.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(monitorIntervalSec), runCts.Token);
        var elapsed = sw.Elapsed.TotalSeconds;
        metrics.Snapshot(elapsed);
        if (elapsed - lastLeak >= leakIntervalSec)
        {
            leak.Sample(elapsed);
            lastLeak = elapsed;
        }
        if (scheduler.IsHealthy)
        {
            healthySince ??= elapsed;
            if (elapsed - healthySince.Value >= watchdogGraceSec
                && !workload.CheckLiveness(maxPublishStallSec, maxReceiveStallSec, out var reason))
            {
                Log($"VIOLATION: {reason}");
                if (!violations.Contains(reason)) violations.Add(reason);
                if (config.FailFast) break;
            }
        }
        else
        {
            healthySince = null;
        }
    }
}
catch (OperationCanceledException)
{
}

Log("soak window elapsed; draining workers");
if (!runCts.IsCancellationRequested) runCts.Cancel();
try { await schedulerTask.ConfigureAwait(false); } catch { /* cancellation */ }
try { await workload.WhenAllAsync().ConfigureAwait(false); } catch { /* cancellation */ }

// Let pending acks/queues settle, then take a final leak + metrics sample at quiescence.
await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
metrics.Snapshot(sw.Elapsed.TotalSeconds);
leak.Sample(sw.Elapsed.TotalSeconds);

// ---- Final invariant checks ----
foreach (var w in workload.Workers)
{
    if (w.Fault is not null)
    {
        var msg = $"worker {w.Id} faulted: {w.Fault.GetType().Name}: {w.Fault.Message}";
        if (!violations.Contains(msg)) violations.Add(msg);
    }
}

if (leak.TryDetectLeak(out var leakReason))
{
    violations.Add($"resource leak: {leakReason}");
}

var reconnects = metrics.Total("mqtt.client.reconnects");
if (scheduler.FaultsApplied > 0 && config.Duration.TotalSeconds >= 60 && reconnects == 0)
{
    violations.Add(
        "no reconnects recorded despite injected faults — recovery path not exercised");
}

// ---- Reports ----
Directory.CreateDirectory(config.ReportDir);
var metricsCsv = Path.Combine(config.ReportDir, "chaos-metrics.csv");
var memoryCsv = Path.Combine(config.ReportDir, "chaos-memory.csv");
metrics.WriteCsv(metricsCsv);
leak.WriteCsv(memoryCsv);

Console.WriteLine();
Console.WriteLine("=== summary ===");
Console.WriteLine($"duration         : {sw.Elapsed}");
Console.WriteLine($"seed             : {config.Seed}");
Console.WriteLine($"faults applied   : {scheduler.FaultsApplied}");
Console.WriteLine($"published        : {workload.TotalPublished}");
Console.WriteLine($"acked (QoS>0)    : {workload.TotalAcked}");
Console.WriteLine($"received         : {workload.TotalReceived}");
Console.WriteLine($"reconnects       : {metrics.Total("mqtt.client.reconnects")}");
Console.WriteLine($"resubscribes     : {metrics.Total("mqtt.client.resubscribes")}");
Console.WriteLine($"disconnects      : {metrics.Total("mqtt.client.disconnects")}");
Console.WriteLine($"keepalive timeout: {metrics.Total("mqtt.client.keepalive.timeouts")}");
Console.WriteLine($"decode errors    : {metrics.Total("mqtt.client.decode.errors")}");
Console.WriteLine($"messages dropped : {metrics.Total("mqtt.client.messages.dropped")}");
Console.WriteLine($"pending acks (end): {metrics.LatestGauge("mqtt.client.pending.acks")}");
Console.WriteLine($"queue depth (end) : {metrics.LatestGauge("mqtt.client.outbound.queue.depth")}");
Console.WriteLine($"subscriptions(end): {metrics.LatestGauge("mqtt.client.subscriptions")}");
Console.WriteLine($"metrics csv      : {metricsCsv}");
Console.WriteLine($"memory csv       : {memoryCsv}");
Console.WriteLine();

if (violations.Count == 0)
{
    Console.WriteLine("CHAOS SOAK PASSED");
    return 0;
}

Console.WriteLine($"CHAOS SOAK FAILED with {violations.Count} violation(s):");
foreach (var v in violations)
{
    Console.WriteLine($"  - {v}");
}
return 1;

static async Task<IChaosBroker> CreateBrokerAsync(string transport) => transport switch
{
    "tcp" => await BrokerHarness.StartAsync(),
    "tls" => await TlsBrokerHarness.StartAsync(),
    "ws" => await WsBrokerHarness.StartAsync(),
    _ => throw new NotSupportedException(
        $"transport '{transport}' is not supported (use: tcp, tls, ws)."),
};

static MqttClient BuildClientForTransport(ChaosConfig cfg, int proxyPort, string id)
{
    var builder = MqttClient.CreateBuilder()
        .WithClientId($"chaos-{id}-{Guid.NewGuid():N}")
        .WithProtocol(MqttProtocolVersion.V500)
        .WithKeepAlive((ushort)cfg.KeepAliveSeconds)
        .WithCleanStart(true)
        // Fast-recovery configuration for the soak: it asserts the client re-establishes within a
        // tight post-fault window, so bound the connect handshake (OperationTimeout) tightly and cap
        // the reconnect backoff. A production deployment tuned for fast recovery uses similar values.
        .Configure(o => o.OperationTimeout = TimeSpan.FromSeconds(3))
        .WithReconnect(new MqttReconnectPolicy
        {
            InitialDelay = TimeSpan.FromMilliseconds(200),
            MaxDelay = TimeSpan.FromSeconds(2),
            BackoffFactor = 2.0,
            JitterFactor = 0.2,
        });
    switch (cfg.Transport)
    {
        case "tcp":
            builder.ConnectTo($"mqtt://127.0.0.1:{proxyPort}");
            break;
        case "tls":
            builder.ConnectTo($"mqtts://127.0.0.1:{proxyPort}")
                .WithTls(o =>
                {
#pragma warning disable CA5359 // test-only: trust the harness's self-signed certificate
                    o.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
                    o.TargetHost = "localhost";
                });
            break;
        case "ws":
            builder.ConnectTo($"ws://127.0.0.1:{proxyPort}");
            break;
        default:
            throw new NotSupportedException($"transport '{cfg.Transport}'");
    }
    return builder.Build();
}
