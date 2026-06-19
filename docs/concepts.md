# Core concepts

## Channels-style API

The public surface mirrors `System.Threading.Channels`:

| Concept | Mqtt.Client | Channels analogue |
| --- | --- | --- |
| Subscribe | `MqttSubscription.Reader` | `ChannelReader<T>` |
| Publish (awaited) | `PublishAsync` | `Writer.WriteAsync` |
| Publish (try) | `TryPublish` | `Writer.TryWrite` |
| Backpressure | `MqttOverflowMode` | `BoundedChannelFullMode` |

Every subscription owns its own bounded channel. Inbound messages are dispatched into
the matching channel(s) by an alloc-free topic filter trie.

## Threading model (hidden by design)

There is exactly **one send loop** and **one receive loop** behind every connection.

- `PublishAsync` enqueues into a bounded outbound channel.
- `TryPublish` is non-blocking and returns `false` if the outbound queue is full.
- `SubscribeAsync` registers a topic filter and returns a `MqttSubscription` whose
  `Reader` you consume from any thread.

You never need to `lock`, never need a `SemaphoreSlim`, and never need `Task.Run`.

## Backpressure

Per subscription, choose what happens when the consumer can't keep up:

```csharp
await client.SubscribeAsync("telemetry/#", new MqttSubscriptionOptions
{
    Capacity = 4096,
    Overflow = MqttOverflowMode.Wait,        // apply TCP backpressure (default)
    // or DropOldest, or DropNewest
});
```

`Wait` is the safest default: the channel writer suspends, which throttles the receive
loop, which lets TCP's receive window do its job. Drop modes are for telemetry where
"newer is better" or "older first" is the right policy.

## QoS

| QoS | Semantics | API |
| --- | --- | --- |
| 0 | At most once. Fire and forget. | `TryPublish` / `PublishAsync(qos:0)` |
| 1 | At least once. Awaits PUBACK. | `PublishAsync(qos:1)` |
| 2 | Exactly once. PUBREC → PUBREL → PUBCOMP. | `PublishAsync(qos:2)` |

QoS 2 carries the strongest delivery guarantee but is the slowest — use it for
duplicate-sensitive control messages, not for telemetry.

## Auto-reconnect

`MqttReconnectPolicy.Exponential()` is the default. After an unexpected disconnect, the
client will retry with exponential backoff + jitter until you call `DisconnectAsync`,
the broker disconnects you with `Banned`, or you dispose the client. The outbound
publish queue **survives** a reconnect so queued messages still get sent.
