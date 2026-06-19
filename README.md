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
    using (msg)   // payloads are pooled by default — dispose after use, don't retain
    {
        Console.WriteLine($"{msg.Topic}: {msg.Payload.Length} bytes");
    }
}

await client.PublishAsync("commands/svc-1", "ping"u8.ToArray());
```

**Low-allocation extras**

```csharp
// Publish a payload that's already split across buffers — no concatenation needed.
ReadOnlySequence<byte> framed = BuildFramedPayload();
await client.PublishAsync("telemetry", framed, MqttQoS.AtLeastOnce);

// Inline-handler subscription: the payload is a true zero-copy slice of the receive buffer,
// valid only inside the handler (no allocation, nothing to dispose). Back-pressure flows to
// the broker while the handler runs.
await client.SubscribeAsync("telemetry/#", msg =>
{
    Process(msg.PayloadMemory.Span);   // do not retain msg or its payload
    return ValueTask.CompletedTask;
});

// Channel subscriptions pool the payload by default (dispose each message). If you need to
// retain messages freely instead, opt back into garbage-collected payloads:
var retaining = MqttClient.CreateBuilder()
    .ConnectTo("mqtt://broker:1883")
    .Configure(o => o.RetainableInboundMessages = true)
    .Build();
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
