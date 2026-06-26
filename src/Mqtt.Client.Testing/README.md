# Mqtt.Client.Testing

An **embeddable, in-process MQTT broker** for testing MQTT clients and applications.

- **Pure managed** — no native binaries, no `mosquitto`/Docker, nothing to install.
- **Every target framework** — `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.
- **Parallel-isolated** — each broker is an independent instance on its own ephemeral
  loopback port, so tests can run many brokers at once with no shared state.
- **MQTT 3.1.1 + 5.0** — connect, subscribe (with `+`/`#` wildcards), publish at QoS 0/1/2,
  retained messages, Last Will, keep-alive.

It is a **test fixture**, not a production or conformance-reference broker.

## Quickstart

```csharp
using Mqtt.Client.Testing;

await using var broker = await MqttTestBroker.StartAsync();

// Point any MQTT client at the broker:
//   broker.Host  -> "127.0.0.1"
//   broker.Port  -> an ephemeral port
//   broker.Uri   -> mqtt://127.0.0.1:<port>

var client = MqttClient.CreateBuilder()
    .WithTcpServer(broker.Host, broker.Port)
    .Build();
await client.ConnectAsync();
// ... exercise your code against a real broker ...
```

Disposing the broker stops the listener and drops all connections.

## Options

```csharp
await using var broker = await MqttTestBroker.StartAsync(new MqttTestBrokerOptions
{
    Port = 0,                 // 0 = ephemeral (default)
    AllowAnonymous = true,    // or supply Authenticate
});
```
