# Code coverage

_Last collected: 2026-06-17 (v0.9.2 — coverage uplift)._

| Suite | Line | Branch | Sources |
| --- | ---: | ---: | --- |
| **Unit tests** (`tests/Mqtt.Client.UnitTests`) | **76.6 %** (1712 / 2235) | **58.3 %** (467 / 801) | codec, trie, packet-id allocator, buffer writer, builder, `MqttClient` end-to-end via `FakePipeTransport` + `FakeBroker`, persistence, DI extensions, exceptions, subscription extension |
| **Integration tests** (`tests/Mqtt.Client.IntegrationTests`) | **60.3 %** (909 / 1508) | **39.1 %** (265 / 678) | end-to-end against in-process MQTTnet broker (QoS 0/1/2 round-trips) |
| **Combined** (unit ∪ integration) | **83.1 %** (1908 / 2297) | **65.2 %** (433 / 664) | both suites merged |

The remaining gap is dominated by `TcpTransport` / `TlsTransport` / `WebSocketTransport` / `MqttClientHostedService` which need a real socket / broker — those are exercised by the integration suite.

## Reproduce

```bash
dotnet tool install --global dotnet-coverage   # one-time
pwsh scripts/coverage.ps1
```

The script builds in Release, runs each suite under `dotnet-coverage collect`, merges the
two cobertura XMLs, and prints the headline numbers.
