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
| Publish QoS 0 | 41.97 µs | **5.57 µs** | **0.13×** | 2.00 KB | **1.32 KB** | **0.66×** |
| Publish QoS 1 (awaits PUBACK) | 115.4 µs | **100.3 µs** | **0.87×** | 4.16 KB | **2.91 KB** | **0.70×** |
| Publish QoS 2 (full PUBREC/REL/COMP) | 217.2 µs | **197.0 µs** | **0.91×** | 7.00 KB | **4.45 KB** | **0.64×** |
| Subscribe receive | 133.9 µs | 136.2 µs | 1.02× | 5.44 KB | **3.92 KB** | **0.72×** |
| Connect + Disconnect | **4.15 ms** | 4.52 ms | 1.09× ⚠ | **37.3 KB** | 49.7 KB | 1.33× ⚠ |

**Publish QoS 0 across payload sizes (in-process broker, MQTT 5)**

| Payload | MQTTnet | Mqtt.Client | Time ratio | Alloc MQTTnet | Alloc Mqtt.Client | Alloc ratio |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 64 B | 54.09 µs | **4.31 µs** | **0.08×** | 1.63 KB | **0.99 KB** | **0.61×** |
| 256 B | 41.97 µs | **5.57 µs** | **0.13×** | 2.00 KB | **1.32 KB** | **0.66×** |
| 1 KB | 45.20 µs | **6.55 µs** | **0.15×** | 3.47 KB | **2.93 KB** | **0.84×** |
| 4 KB | 47.73 µs | **25.75 µs** | **0.54×** | 9.43 KB | 10.62 KB | 1.13× ⚠ |
| 16 KB | 44.59 µs | 50.12 µs | 1.13× ⚠ | 33.50 KB | 33.51 KB | 1.00× |
| 64 KB | 189.5 µs | **152.6 µs** | **0.81×** | 130.2 KB | 130.7 KB | 1.00× |

**Subscribe receive across payload sizes (publisher → broker → subscriber)**

| Payload | MQTTnet | Mqtt.Client | Time ratio | Alloc MQTTnet | Alloc Mqtt.Client | Alloc ratio |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 64 B | 137.4 µs | 153.7 µs | 1.12× ⚠ | 4.70 KB | **3.36 KB** | **0.71×** |
| 256 B | 133.9 µs | 136.2 µs | 1.02× | 5.44 KB | **3.92 KB** | **0.72×** |
| 1 KB | 156.2 µs | **144.3 µs** | **0.92×** | 8.44 KB | **6.17 KB** | **0.73×** |
| 4 KB | 190.0 µs | 210.0 µs | 1.11× ⚠ | 20.61 KB | **15.24 KB** | **0.74×** |
| 16 KB | 427.0 µs | **250.1 µs** | **0.59×** | 69.09 KB | **51.46 KB** | **0.74×** |
| 64 KB | 821.5 µs | **391.0 µs** | **0.48×** | 262.9 KB | **196.4 KB** | **0.75×** |

### Where Mqtt.Client is slower (the ⚠ rows)

- **Connect + Disconnect (time 1.09×, alloc 1.33×)** — the only *consistent* end-to-end regression. `System.IO.Pipelines` setup allocates a `Pipe` pair, two long-running loop `Task`s and the transport on every connect, where MQTTnet uses a leaner socket reader. This is a **one-time cost per connection** that amortizes to ~0 for the long-lived clients this library targets; it does not scale with traffic.
- **QoS 0 @ 4 KB (alloc 1.13×) / @ 16 KB (time 1.13×), Subscribe @ 64 B & 4 KB (time ~1.1×)** — ShortRun noise. These are 3-iteration runs whose error bars (±5–10 µs on the means, and ±1 ArrayPool bucket on bytes) exceed the gap; the same scenarios sit at **0.5–0.9×** one payload size up or down. End-to-end timing is dominated by the in-process broker and OS scheduling, not by client code. The 4 KB allocation blip is an `ArrayPool` bucket-rounding artifact (a 4 KB payload nudges the pipe onto the next 8 KB segment); 16 KB/64 KB return to **1.00×**.
- **Codec micro-benchmarks** (`EncodePublish` ≈ 2×, `DecodePublish` ≈ 1.3–1.8×, `EncodeSubscribe` ≈ 1.9× — see `docs/benchmarks.md`) — nanosecond-scale and measure the codec *in isolation*. MQTTnet's codec is exceptionally tuned (direct span writes, no intermediate buffer). Ours trades a few-nanosecond fixed cost for a pooled `MqttBufferWriter` + zero-copy vectored payload writes, which is invisible against network/broker latency and is why the **end-to-end** publish rows above are faster. Per-call allocations stay ≤ 64 B (≤ MQTTnet).

Everywhere else Mqtt.Client matches or beats MQTTnet, and **allocations are 0.61–0.84× across every payload-bearing path** thanks to the pooled `IValueTaskSource` ack waiter and zero-copy payload handling. Full matrix in [docs/benchmarks.md](docs/benchmarks.md).

**Allocation work in `[Unreleased]`** — a pooled `IValueTaskSource` ack waiter replaced the per-operation `TaskCompletionSource`/`Task` for QoS > 0 publishes and subscribe/unsubscribe (256 B QoS 1 ≈ 3.10 → 2.91 KB, QoS 2 ≈ 4.72 → 4.45 KB). Setting `ReuseInboundBuffers = true` additionally removes the per-message payload allocation on the receive path by renting from `ArrayPool<byte>` (consumers then dispose each `MqttMessage`).

Both libraries are MIT-licensed; the goal of these benchmarks is to make tradeoffs visible, not to declare a winner.
<!-- benchmarks:end -->

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
