# Code coverage

_Last collected: 2026-06-19 (covers the ReadOnlySequence payload API, pooled-by-default inbound,
inline-handler subscriptions, MQTT 5 enhanced auth, and the codec perf rework)._

| Suite | Line | Branch | Sources |
| --- | ---: | ---: | --- |
| **Unit tests** (`tests/Mqtt.Client.UnitTests`) | **84.9 %** (3356 / 3952) | **70.1 %** (800 / 1141) | codec (encode/decode round-trips + property branches), trie, packet-id allocator, buffer writer, builder, `MqttClient` end-to-end via `FakePipeTransport` + `FakeBroker`, persistence (in-memory + file), DI extensions (named clients + hosted service), exceptions, subscription overflow modes, sequence reader, reconnect/keep-alive, state/alias/sub-id, enhanced auth, sequence-payload + pooling, allocation-elimination |
| **Integration tests** (`tests/Mqtt.Client.IntegrationTests`) | **55.9 %** (1296 / 2317) | **35.7 %** (328 / 920) | end-to-end against in-process MQTTnet broker (QoS 0/1/2 round-trips, last-will, session resumption, sub-id, transport scenarios) |
| **Combined** (unit ∪ integration) | **88.8 %** (3656 / 4117) | **75.7 %** (730 / 964) | both suites merged |

The remaining unit-only gap is `TcpTransport` / `TlsTransport` / `WebSocketTransport` / `Stream*Pipe` pumps / `MqttClientHostedService` — these need a real socket and are integration-only by design.

## Reproduce

```bash
dotnet tool install --global dotnet-coverage   # one-time
pwsh scripts/coverage.ps1
```

The script builds in Release, runs each suite under `dotnet-coverage collect`, merges the
two cobertura XMLs, and prints the headline numbers.
