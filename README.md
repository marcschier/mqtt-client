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

| Scenario | MQTTnet (baseline) | Mqtt.Client | Ratio | Allocated (M / O) |
| --- | ---: | ---: | ---: | ---: |
| Publish QoS 0 | 40.93 µs | **7.82 µs** | **0.19×** | 1.97 KB / 1.55 KB |
| Publish QoS 1 (awaits PUBACK) | 152.9 µs | **129.0 µs** | **0.84×** | 4.21 KB / 3.08 KB |
| Publish QoS 2 (full PUBREC/REL/COMP) | 281.2 µs | **238.4 µs** | **0.85×** | 7.06 KB / 4.67 KB |
| Subscribe receive | 199.9 µs | 213.4 µs | 1.07× | 5.54 KB / **3.96 KB** |
| Connect + Disconnect | 4.77 ms | 4.98 ms | 1.05× | 37 KB / 44 KB |

**Publish QoS 0 across payload sizes (in-process broker, MQTT 5)**

| Payload | MQTTnet | Mqtt.Client | Ratio |
| ---: | ---: | ---: | ---: |
| 64 B | 55.15 µs | **5.34 µs** | **0.10×** |
| 256 B | 55.77 µs | **6.28 µs** | **0.11×** |
| 1 KB | 50.12 µs | **9.17 µs** | **0.18×** |
| 4 KB | 54.67 µs | **31.42 µs** | 0.58× |
| 16 KB | 59.54 µs | **42.82 µs** | 0.72× |
| 64 KB | 164.9 µs | 175.96 µs | 1.07× |

**Subscribe receive across payload sizes (publisher → broker → subscriber)**

| Payload | MQTTnet | Mqtt.Client | Ratio | Allocated (M / O) |
| ---: | ---: | ---: | ---: | ---: |
| 64 B | 187.4 µs | **184.8 µs** | 0.99× | 4.80 KB / **3.39 KB** |
| 256 B | 199.9 µs | 213.4 µs | 1.07× | 5.54 KB / **3.96 KB** |
| 1 KB | 200.1 µs | 206.0 µs | 1.03× | 8.56 KB / **6.19 KB** |
| 4 KB | 212.3 µs | **197.6 µs** | 0.93× | 20.62 KB / **15.21 KB** |
| 16 KB | 323.6 µs | **229.4 µs** | **0.71×** | 69.08 KB / **51.42 KB** |
| 64 KB | 797.7 µs | **337.4 µs** | **0.42×** | 262.83 KB / 424.65 KB |

See [docs/benchmarks.md](docs/benchmarks.md) for the full matrix (all 6 payload sizes × 8 scenarios) and codec micro-benchmarks. Both libraries are MIT-licensed; the goal of these benchmarks is to make tradeoffs visible, not to declare a winner.
<!-- benchmarks:end -->

Numbers vary by hardware and broker. The point is the methodology — re-run on your
infra before drawing conclusions.

## 🧪 Tests

- Unit tests (TUnit): `tests/Mqtt.Client.UnitTests` — runs on net8/9/10. **30 tests pass**, **36.4 %** line coverage / **25.9 %** branch coverage of `src/Mqtt.Client`.
- Integration tests vs MQTTnet broker: `tests/Mqtt.Client.IntegrationTests` — **3 tests pass**, lifts combined coverage to **65.7 %** line / **42.5 %** branch.
- NativeAOT smoke: `tests/Mqtt.Client.AotTests` (publishes with `PublishAot=true`).
- Fuzz harnesses (SharpFuzz + libFuzzer, Linux): `tests/Mqtt.Client.FuzzTests`.

Reproduce coverage locally with `pwsh scripts/coverage.ps1`. Full breakdown in [docs/coverage.md](docs/coverage.md).

## 🔐 Security

Found something? Please file privately via GitHub Security Advisories — see
[SECURITY.md](SECURITY.md). Threat model and audit findings are in
[docs/security-audit.md](docs/security-audit.md).
