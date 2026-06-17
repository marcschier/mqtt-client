# Troubleshooting

## Connect hangs / times out

- Confirm the broker port matches the scheme (`mqtt://` → 1883, `mqtts://` → 8883).
- For TLS, set `WithTls(o => o.TargetHost = "broker.host")` if the cert CN differs
  from the address you connect to.
- A misconfigured proxy intercepting the TLS handshake will fail certificate
  validation; check the broker's certificate fingerprint matches what your client sees.

## `MqttConnectionException: Broker closed connection before CONNACK`

The broker accepted the TCP/TLS connection but rejected the MQTT handshake. Most
common causes:
- Wrong protocol version (`WithProtocol(MqttProtocolVersion.V311)` for legacy brokers).
- Broker requires authentication you haven't supplied.
- Broker requires a unique `ClientId`; another client is connected under that ID.

## QoS 1 publishes never resolve

The default `OperationTimeout` is 30 seconds, but `PublishAsync(qos:1)` honors the
`CancellationToken` you pass — pass one with a sane deadline. If the broker advertises
`Receive Maximum=N`, the client allows at most N in-flight QoS&gt;0 publishes; further
calls await until one acks.

## Subscriptions miss messages

- Confirm topic filter wildcards: `+` matches exactly one level; `#` must be the last
  level and matches zero or more.
- Topics starting with `$` (e.g. `$SYS/...`) are not matched by `+` or `#` — register
  the literal filter.
- If `MqttOverflowMode.DropNewest` or `DropOldest` is set, messages are intentionally
  dropped when the channel is full; switch to `Wait` to apply backpressure instead.

## Memory growth under load

- Lower `ReceiveMaximum` to bound concurrent inbound QoS&gt;0 publishes.
- Lower per-subscription `Capacity`; the default of 1024 is generous.
- Lower `MaxIncomingPacketSize` if you don't accept large payloads.

## Reconnect storms in logs

Set a `MqttReconnectPolicy.Fixed(TimeSpan.FromSeconds(10))` instead of the exponential
default to cap retry cadence, or `WithReconnect(null)` to disable auto-reconnect
entirely (you'll get a `Disconnected` event and can decide what to do).

## NativeAOT publish prints trim warnings

The library itself is warning-free. Trim warnings during your app's publish step
almost always come from `ILogger` providers, `Microsoft.Extensions.Configuration`
binders, or other dependencies. Suppress per the library that emitted the warning,
or pin the offending NuGet to an AOT-friendly version.

## Where to file an issue

[https://github.com/marcschier/mqtt-client/issues](https://github.com/marcschier/mqtt-client/issues).
Please include the protocol version, broker software + version, and a minimal repro.
