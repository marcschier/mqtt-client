# Code coverage

_Last collected: 2026-06-17 (v0.9.3 — coverage ≥ 80 %)._

| Suite | Line | Branch | Sources |
| --- | ---: | ---: | --- |
| **Unit tests** (`tests/Mqtt.Client.UnitTests`) | **83.8 %** (2117 / 2527) | **68.8 %** (580 / 843) | codec, trie, packet-id allocator, buffer writer, builder, `MqttClient` end-to-end via `FakePipeTransport` + `FakeBroker`, persistence, DI extensions (incl. named clients + hosted service), exceptions, subscription overflow modes, sequence reader, decoder property branches, reconnect/keep-alive |
| **Integration tests** (`tests/Mqtt.Client.IntegrationTests`) | **59.6 %** (920 / 1543) | **38.4 %** (264 / 688) | end-to-end against in-process MQTTnet broker (QoS 0/1/2 round-trips) |
| **Combined** (unit ∪ integration) | **88.5 %** (2291 / 2589) | **75.1 %** (541 / 720) | both suites merged |

The remaining 19 % unit-only gap is `TcpTransport` / `TlsTransport` / `WebSocketTransport` / `Stream*Pipe` pumps / `MqttClientHostedService` — these need a real socket and are integration-only by design.

## Reproduce

```bash
dotnet tool install --global dotnet-coverage   # one-time
pwsh scripts/coverage.ps1
```

The script builds in Release, runs each suite under `dotnet-coverage collect`, merges the
two cobertura XMLs, and prints the headline numbers.
