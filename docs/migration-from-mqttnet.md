# Migrating from MQTTnet

`Mqtt.Client` and [MQTTnet](https://github.com/dotnet/MQTTnet) solve overlapping problems with different priorities. This guide is a side-by-side cheat sheet for common operations.

## When to pick which

**Pick MQTTnet when:**
- You need a hosted **broker** (`MQTTnet.Server`)
- You need deep ASP.NET Core integration / middleware
- You depend on the broadest possible MQTT 5 option surface available today
- Your app already uses MQTTnet and the cost of switching outweighs perf wins

**Pick Mqtt.Client when:**
- You want the lowest-allocation client (≤ MQTTnet bytes/op on every measured workload, often half)
- You want a Channels-style consumer API (`ChannelReader<MqttMessage>`, `IAsyncEnumerable`)
- You want NativeAOT support on .NET 10 with zero suppressions
- You want first-class `Microsoft.Extensions.DependencyInjection` + source-generated logging out of the box

## Construction

| MQTTnet | Mqtt.Client |
|---|---|
| `new MqttClientFactory().CreateMqttClient()` | `MqttClient.CreateBuilder().ConnectTo("mqtt://...").Build()` |
| `MqttClientOptionsBuilder().WithTcpServer(...).Build()` | `.ConnectTo("mqtt://host:port")` |
| `.WithClientId("id")` | `.WithClientId("id")` |
| `.WithProtocolVersion(MqttProtocolVersion.V500)` | `.WithProtocol(MqttProtocolVersion.V500)` |
| `.WithCredentials(user, pw)` | `.WithCredentials(user, pw)` |
| `.WithCleanStart(true)` | `.WithCleanStart(true)` |
| `.WithKeepAlivePeriod(TimeSpan.FromSeconds(60))` | `.WithKeepAlive(60)` |
| `.WithWillTopic(...).WithWillPayload(...).WithWillQualityOfServiceLevel(...)` | `.WithLastWill(new MqttLastWill { Topic=..., Payload=..., QoS=... })` |

## Connect / disconnect

| MQTTnet | Mqtt.Client |
|---|---|
| `await client.ConnectAsync(options, ct)` | `await client.ConnectAsync(ct)` |
| `await client.DisconnectAsync()` | `await client.DisconnectAsync(ct)` |
| `client.ConnectedAsync += ...` | `client.Connected += ...` |
| `client.DisconnectedAsync += ...` | `client.Disconnected += ...` |
| _no equivalent_ | `client.StateChanged += (s, state) => ...` |

## Publish

```csharp
// MQTTnet
var msg = new MqttApplicationMessageBuilder()
    .WithTopic("t")
    .WithPayload(payload)
    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
    .Build();
await client.PublishAsync(msg);

// Mqtt.Client
await client.PublishAsync("t", payload, MqttQoS.AtLeastOnce);
// or fire-and-forget for QoS 0:
client.TryPublish("t", payload);
```

## Subscribe + consume

```csharp
// MQTTnet
client.ApplicationMessageReceivedAsync += async e =>
{
    await Process(e.ApplicationMessage);
};
await client.SubscribeAsync("topic/+/data");

// Mqtt.Client (channels-style)
var sub = await client.SubscribeAsync("topic/+/data");
await foreach (var msg in sub.Reader.ReadAllAsync(ct))
{
    await Process(msg);
}
// or dispose to UNSUBSCRIBE:
await sub.DisposeAsync();
```

## DI

```csharp
// Mqtt.Client
services.AddMqttClient(o => o.ConnectTo("mqtt://broker"))
        .AddMqttClientHostedReconnect();

// named clients:
services.AddMqttClient("primary", o => o.ConnectTo("mqtt://primary"));
services.AddMqttClient("secondary", o => o.ConnectTo("mqtt://secondary"));
var factory = sp.GetRequiredService<IMqttClientFactory>();
var primary = factory.Get("primary");
```

## Observability

| Concern | MQTTnet | Mqtt.Client |
|---|---|---|
| Logging | `IMqttNetLogger` | `Microsoft.Extensions.Logging` (source-generated) |
| Metrics | Manual | `System.Diagnostics.Metrics`, meter `Mqtt.Client` |
| Tracing | Manual | `ActivitySource` `Mqtt.Client` |

## Things to know

- **QoS 2** outbound state machine is fully implemented in `Mqtt.Client` (PUBREC → PUBREL → PUBCOMP).
- **Shared subscriptions** (`$share/group/topic`) are accepted; the broker performs distribution and `Mqtt.Client` routes the inbound messages via the underlying topic filter.
- **Subscription identifiers** (MQTT 5) are allocated automatically and used as a fast-path dispatch when the broker echoes them.
- **Topic aliases** are managed inbound (per `TopicAliasMaximum`) and outbound (via `TopicAliasManager`).
- **Persistence**: `IPersistentSessionStore` ships with `InMemorySessionStore` (default) and `FileSessionStore` (durable across process restarts).
