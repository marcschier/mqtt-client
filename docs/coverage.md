# Code coverage

_Last collected: 2026-06-17. Re-run with `pwsh scripts/coverage.ps1`._

| Suite | Line coverage | Branch coverage | Sources |
| --- | ---: | ---: | --- |
| **Unit tests** (`tests/Mqtt.Client.UnitTests`) | **36.4 %** (583 / 1603) | **25.9 %** (186 / 718) | codec, trie, packet-id allocator, buffer writer, builder URI parsing |
| **Integration tests** (`tests/Mqtt.Client.IntegrationTests`) | **59.6 %** (868 / 1457) | **38.0 %** (251 / 660) | end-to-end via in-process MQTTnet broker (QoS 0/1/2 round-trips) |
| **Combined** (unit ∪ integration) | **65.7 %** (1094 / 1665) | **42.5 %** (252 / 593) | both suites merged |

The unit-test number alone is intentionally modest: the connection loop, transports,
auto-reconnect, and DI hosted-service paths are exercised by the integration suite
against a real broker, not by isolated unit tests. The combined number is the
honest answer for "how much of the library is covered by automated tests".

## Per-class (unit tests only)

| Class | Line | Branch |
| --- | ---: | ---: |
| `Mqtt.Client.Transport.TcpTransportFactory` | 100% | 100% |
| `Mqtt.Client.Protocol.Packets.ConnectPacket` | 100% | 100% |
| `Mqtt.Client.Diagnostics.MqttMetrics` | 100% | 100% |
| `Mqtt.Client.MqttClientOptions` | 100% | 100% |
| `Mqtt.Client.Transport.TlsTransportFactory` | 100% | 100% |
| `Mqtt.Client.Subscriptions.TopicFilterTrie<T>` | 95.8% | 93.5% |
| `Mqtt.Client.PacketIdAllocator` | 95% | 70% |
| `Mqtt.Client.Buffers.MqttBufferWriter` | 82.1% | 77.3% |
| `Mqtt.Client.TopicAliasManager` | 76.2% | 100% |
| `Mqtt.Client.Buffers.MqttSequenceReader` | 55.6% | 43.8% |
| `Mqtt.Client.MqttClientBuilder` | 45.3% | 21.7% |
| `Mqtt.Client.Protocol.MqttPacketEncoder` | 42.5% | 36.4% |
| `Mqtt.Client.MqttReconnectPolicy` | 41.7% | 100% |
| `Mqtt.Client.Protocol.MqttPacketDecoder` | 23.8% | 5.5% |
| `Mqtt.Client.MqttClient` | 22.5% | 23% |
| `Mqtt.Client.Persistence.InMemorySessionStore` | 15.4% | 0% |
| `Mqtt.Client.Diagnostics.MqttActivitySource` | 0% | 100% |
| `Mqtt.Client.Transport.WebSocketTransport` | 0% | 100% |
| `Mqtt.Client.MqttClient.OutboundEnvelope` | 0% | 100% |
| `Mqtt.Client.Protocol.Packets.ConnAckPacket` | 0% | 100% |
| `Mqtt.Client.DependencyInjection.MqttClientServiceCollectionExtensions.<>c` | 0% | 100% |
| `Mqtt.Client.Transport.WebSocketTransportFactory` | 0% | 100% |
| `Mqtt.Client.Diagnostics.MqttLog` | 0% | 0% |
| `Mqtt.Client.MqttClient.<>c` | 0% | 100% |
| `Mqtt.Client.MqttProtocolException` | 0% | 100% |
| `Mqtt.Client.Transport.StreamPipeWriter` | 0% | 100% |
| `Mqtt.Client.Transport.StreamPipeReader` | 0% | 100% |
| `Mqtt.Client.Transport.WebSocketTransport.WebSocketStream` | 0% | 100% |
| `Mqtt.Client.Transport.TcpTransport` | 0% | 0% |
| `Mqtt.Client.DependencyInjection.MqttClientHostedService` | 0% | 0% |
| `Mqtt.Client.DependencyInjection.MqttClientServiceCollectionExtensions` | 0% | 0% |
| `Mqtt.Client.MqttSubscriptionOptions` | 0% | 100% |
| `Mqtt.Client.MqttSubscriptionExtensions` | 0% | 0% |
| `Mqtt.Client.MqttSubscription` | 0% | 0% |
| `Mqtt.Client.MqttConnectResult` | 0% | 100% |
| `Mqtt.Client.MqttPublishResult` | 0% | 100% |
| `Mqtt.Client.MqttDisconnectedEventArgs` | 0% | 100% |
| `Mqtt.Client.MqttConnectionException` | 0% | 100% |
| `Mqtt.Client.Transport.TlsTransport` | 0% | 0% |
| `Mqtt.Client.Protocol.MqttPacketDecoder.ConnAckBuilder` | 0% | 0% |

## Reproduce

`ash
dotnet tool install --global dotnet-coverage   # one-time
pwsh scripts/coverage.ps1
`

The script:

1. Builds in Release.
2. Runs the unit tests under `dotnet-coverage collect` → `coverage/unit.cobertura.xml`.
3. Runs the integration tests under `dotnet-coverage collect` → `coverage/integration.cobertura.xml`.
4. Merges them → `coverage/merged.cobertura.xml`.
5. Prints the headline numbers above.

Filter is set to `include-files src/Mqtt.Client/*` so test code, generated source
(BDN benchmarks, fuzz harnesses), and third-party assemblies are excluded.
