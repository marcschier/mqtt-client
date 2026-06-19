# Advanced

## Custom persistence

Implement `IPersistentSessionStore` to back QoS 1/2 in-flight state with a file, SQLite,
Redis — whatever fits your durability requirements. The default `InMemorySessionStore`
loses pending publishes if the process dies before they're acked.

```csharp
public sealed class FileSessionStore : IPersistentSessionStore { /* ... */ }

services.AddMqttClient(o => ...)
        .AddSingleton<IPersistentSessionStore, FileSessionStore>();
```

## Custom transport

The transport layer is interface-based. To add a transport (Unix Domain Sockets,
QUIC, Bluetooth …), implement `IMqttTransport` (exposes `PipeReader Input` /
`PipeWriter Output`) plus `IMqttTransportFactory.ConnectAsync(...)`. The packet
codec, dispatcher, and client loop are transport-agnostic.

## Observability

### Logging

```csharp
var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtt://broker")
    .WithLogging(loggerFactory)
    .Build();
```

Log entries are emitted via `[LoggerMessage]` source generators — zero allocations on
the hot path when the level is disabled.

### Metrics

Hook up an `OpenTelemetry` (or any `System.Diagnostics.Metrics`) listener on the meter
name `Mqtt.Client`:

```csharp
using var meter = new MeterListener
{
    InstrumentPublished = (instrument, listener) =>
    {
        if (instrument.Meter.Name == "Mqtt.Client") listener.EnableMeasurementEvents(instrument);
    }
};
```

Counters: `mqtt.client.publishes`, `mqtt.client.receives`, `mqtt.client.bytes.sent`,
`mqtt.client.bytes.received`, `mqtt.client.reconnects`.
Histogram: `mqtt.client.publish.ack.duration` (milliseconds).

### Tracing

Subscribe to the `Mqtt.Client` `ActivitySource` for OpenTelemetry spans around
connect/publish/subscribe.

## Tuning the receive maximum

MQTT 5 lets the client advertise `Receive Maximum` — the number of QoS 1/2 PUBLISHes
the broker may send concurrently. The default is 65535 (no practical limit). Lower it
to bound memory:

```csharp
.Configure(o => o.ReceiveMaximum = 64)
```

## Tuning the max incoming packet size

The decoder enforces `MqttClientOptions.MaxIncomingPacketSize` (default 1 MiB). A
broker that advertises a remaining-length larger than this causes the client to
disconnect rather than buffer attacker-controlled bytes. Raise it if you legitimately
need larger payloads.

## Topic alias (MQTT 5)

Topic aliases compress repeated topic strings on the wire. The client
manages outbound aliases automatically when the broker advertises `Topic Alias Maximum`
in its CONNACK. No code change required.
