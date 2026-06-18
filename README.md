# Mqtt.Client

A high-performance, low-allocation MQTT 3.1.1 + 5.0 client for .NET — designed to feel like `System.Threading.Channels`.

## 🚀 Highlights

- **Multi-TFM**: `netstandard2.1`, `net8.0`, `net9.0`, `net10.0` (NativeAOT-clean).
- **Channels-style API** — `ChannelReader<MqttMessage>` for subscriptions, `TryPublish` / `PublishAsync` for sending.
- **Built-in** DI extensions, source-generated logging, `System.Diagnostics.Metrics`, `ActivitySource`.
- **Transports**: TCP, TLS, WebSocket, Secure WebSocket.
- **Auto-reconnect** with exponential backoff + jitter; queued publishes survive reconnect.
- **Pluggable** session persistence for QoS 1/2 in-flight state.
- **Secure defaults** — TLS 1.2/1.3, CRL checking, capped incoming packet size.

## 📦 Install

```bash
dotnet add package Mqtt.Client
```

## ⚡ Quickstart

```csharp
using Mqtt.Client;

await using var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtts://broker:8883")
    .WithClientId("svc-1")
    .WithCredentials("user", "pw")
    .Build();

await client.ConnectAsync();

await using var sub = await client.SubscribeAsync("sensors/+/temp");
await foreach (var msg in sub.Reader.ReadAllAsync())
{
    Console.WriteLine($"{msg.Topic}: {msg.Payload.Length} bytes");
}

await client.PublishAsync("commands/svc-1", "ping"u8.ToArray());
```

**Low-allocation extras**

```csharp
// Publish a payload that's already split across buffers — no concatenation needed.
ReadOnlySequence<byte> framed = BuildFramedPayload();
await client.PublishAsync("telemetry", framed, MqttQoS.AtLeastOnce);

// Opt into pooled inbound buffers for zero-GC receive paths. MqttMessage then owns a pooled
// buffer: dispose each message after use and don't retain its payload afterwards.
var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtt://broker:1883")
    .Configure(o => o.ReuseInboundBuffers = true)
    .Build();

await foreach (var msg in sub.Reader.ReadAllAsync())
{
    using (msg)                       // returns the pooled buffer
    {
        Process(msg.PayloadMemory.Span);
    }
}
```

## 📚 Documentation

- [Quickstart](docs/quickstart.md)
- [Core concepts](docs/concepts.md) — channels-style, threading, backpressure
- [Samples](docs/samples.md) — auth, TLS, DI, last-will, MQTT 5 properties
- [Advanced](docs/advanced.md) — custom transport, persistence, AOT, metrics
- [Troubleshooting](docs/troubleshooting.md)
- [Spec conformance](docs/conformance.md)

## 🔍 When to pick [MQTTnet](https://github.com/dotnet/MQTTnet) instead

`MQTTnet` is a mature, battle-tested .NET MQTT library that covers ground this client
intentionally doesn't:

- You need a **broker** in addition to a client.
- You need **ASP.NET Core integration** (managed clients, hosted broker, middleware).
- You need the **broadest possible protocol option coverage** and a long support history.
- You're already on it and it serves you well.

`Mqtt.Client` is focused on being a small, fast, AOT-friendly, channels-style
**client** library for .NET services. Pick whichever fits your situation; both projects
are MIT-licensed and the protocol wire formats are identical.

## 📊 Benchmarks (vs MQTTnet 5.x)

The repository ships a `tests/Mqtt.Client.Benchmarks` BenchmarkDotNet comparison suite.
Run locally:

```bash
dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- --filter '*' --report
```

See [docs/benchmarks.md](docs/benchmarks.md) for the full matrix.

<!-- benchmarks:start -->
_Headline rows from the latest BenchmarkDotNet ShortRun. Hardware: Intel Xeon W-2235 @ 3.80 GHz, .NET 10. Numbers vary by hardware and broker — re-run on your infra before drawing conclusions._

**End-to-end @ 256 B payload (in-process MQTTnet broker, MQTT 5)**

| Scenario | MQTTnet | Mqtt.Client | Time ratio | Alloc MQTTnet | Alloc Mqtt.Client | Alloc ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Publish QoS 0 | 56.55 µs | **6.70 µs** | **0.12×** | 2.03 KB | **1.52 KB** | **0.75×** |
| Publish QoS 1 (awaits PUBACK) | 122.3 µs | **116.0 µs** | **0.95×** | 4.16 KB | **3.10 KB** | **0.75×** |
| Publish QoS 2 (full PUBREC/REL/COMP) | 221.0 µs | **214.9 µs** | **0.97×** | 7.02 KB | **4.72 KB** | **0.67×** |
| Subscribe receive | 183.5 µs | **179.9 µs** | **0.99×** | 5.49 KB | **3.72 KB** | **0.68×** |
| Connect + Disconnect | 5.03 ms | **4.30 ms** | **0.86×** | **37.3 KB** | 47.7 KB | 1.28× |

**Publish QoS 0 across payload sizes (in-process broker, MQTT 5)**

| Payload | MQTTnet | Mqtt.Client | Time ratio | Alloc MQTTnet | Alloc Mqtt.Client | Alloc ratio |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 64 B | 45.78 µs | **4.73 µs** | **0.10×** | 1.67 KB | **1.14 KB** | **0.68×** |
| 256 B | 56.55 µs | **6.70 µs** | **0.12×** | 2.03 KB | **1.52 KB** | **0.75×** |
| 1 KB | 52.29 µs | **6.96 µs** | **0.13×** | 3.57 KB | **3.06 KB** | **0.86×** |
| 4 KB | 49.70 µs | **15.16 µs** | **0.31×** | 11.3 KB | **9.23 KB** | **0.82×** |
| 16 KB | 52.75 µs | 55.07 µs | 1.05× ⚠ | 34.34 KB | **34.09 KB** | **0.99×** |
| 64 KB | 161.5 µs | **157.8 µs** | **0.98×** | 133.3 KB | 133.6 KB | 1.00× |

**Subscribe receive across payload sizes (publisher → broker → subscriber)**

| Payload | MQTTnet | Mqtt.Client | Time ratio | Alloc MQTTnet | Alloc Mqtt.Client | Alloc ratio |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 64 B | 176.7 µs | **163.4 µs** | **0.93×** | 4.87 KB | **3.15 KB** | **0.65×** |
| 256 B | 183.5 µs | **179.9 µs** | **0.99×** | 5.49 KB | **3.72 KB** | **0.68×** |
| 1 KB | 204.3 µs | 209.1 µs | 1.02× | 8.64 KB | **6.00 KB** | **0.69×** |
| 4 KB | 223.7 µs | **198.6 µs** | **0.89×** | 20.65 KB | **15.01 KB** | **0.73×** |
| 16 KB | 404.8 µs | **283.7 µs** | **0.70×** | 69.1 KB | **51.22 KB** | **0.74×** |
| 64 KB | 907.1 µs | **324.9 µs** | **0.36×** | 263.0 KB | **196.1 KB** | **0.75×** |

The remaining `> 1.0×` rows are within ShortRun noise (16 KB QoS 0 has stdev > 3 µs on a 55 µs mean) or one-time Connect overhead from Pipelines initialisation. Full matrix in [docs/benchmarks.md](docs/benchmarks.md).

**Allocation reductions in `[Unreleased]`** — a pooled `IValueTaskSource` ack waiter replaced the per-operation `TaskCompletionSource`/`Task` for QoS > 0 publishes and subscribe/unsubscribe, lowering allocations on those paths (256 B QoS 1 ≈ 3.10 → 2.91 KB, QoS 2 ≈ 4.72 → 4.45 KB on the same hardware). Setting `ReuseInboundBuffers = true` additionally removes the per-message payload allocation on the receive path by renting from `ArrayPool<byte>` (consumers then dispose each `MqttMessage`).

Both libraries are MIT-licensed; the goal of these benchmarks is to make tradeoffs visible, not to declare a winner.
<!-- benchmarks:end -->

Numbers vary by hardware and broker. The point is the methodology — re-run on your
infra before drawing conclusions.

## 🧪 Tests

- Unit tests (TUnit): `tests/Mqtt.Client.UnitTests` — runs on net8/9/10. **102 tests pass**, **83.8 %** line / **68.8 %** branch coverage of `src/Mqtt.Client` via a `FakePipeTransport` + `FakeBroker` in-process harness.
- Integration tests vs MQTTnet broker: `tests/Mqtt.Client.IntegrationTests` — **3 tests pass**, lifts combined coverage to **88.5 %** line / **75.1 %** branch.
- NativeAOT smoke: `tests/Mqtt.Client.AotTests` (publishes with `PublishAot=true`).
- Fuzz harnesses (SharpFuzz + libFuzzer, Linux): `tests/Mqtt.Client.FuzzTests`.

Reproduce coverage locally with `pwsh scripts/coverage.ps1`. Full breakdown in [docs/coverage.md](docs/coverage.md).

## 🔐 Security

Found something? Please file privately via GitHub Security Advisories — see
[SECURITY.md](SECURITY.md). Threat model and audit findings are in
[docs/security-audit.md](docs/security-audit.md).
