# MQTT spec conformance — Mqtt.Client v0.1

This document maps OASIS MQTT 3.1.1 and 5.0 normative chapters to the implementation in `Mqtt.Client`.

## Status legend
- ✅ Implemented and unit-tested
- 🟡 Implemented, additional tests pending
- ❌ Deferred (tracked as a todo)

## MQTT 3.1.1 ([OASIS 3.1.1](http://docs.oasis-open.org/mqtt/mqtt/v3.1.1/os/mqtt-v3.1.1-os.html))

| Section | Feature | Status |
| --- | --- | --- |
| 1.5.x | Data representations (var int, UTF-8 string, binary data) | ✅ `MqttBufferWriter` / `MqttSequenceReader` |
| 3.1   | CONNECT | ✅ `MqttPacketEncoder.EncodeConnect` |
| 3.2   | CONNACK (incl. return-code mapping to reason codes) | ✅ `MqttPacketDecoder.DecodeConnAck` |
| 3.3   | PUBLISH (QoS 0/1) | ✅ |
| 3.3   | PUBLISH QoS 2 (PUBREC/PUBREL/PUBCOMP state machine) | ❌ tracked |
| 3.4   | PUBACK | ✅ |
| 3.5–3.7 | PUBREC/PUBREL/PUBCOMP | 🟡 encoders provided; receive side stubbed |
| 3.8   | SUBSCRIBE | ✅ |
| 3.9   | SUBACK | ✅ |
| 3.10  | UNSUBSCRIBE | ✅ |
| 3.11  | UNSUBACK | ✅ |
| 3.12  | PINGREQ | ✅ |
| 3.13  | PINGRESP | ✅ (drives keep-alive) |
| 3.14  | DISCONNECT | ✅ |
| 4.6   | Last-will | ✅ encoder; broker delivery is broker-side |
| 4.7   | Topic names / wildcards / `$` topics | ✅ `TopicFilterTrie` |
| 4.7.3 | Retained messages | 🟡 transport-level; explicit retain APIs available via `MqttPublishProperties` |

## MQTT 5.0 ([OASIS 5.0](https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html))

| Section | Feature | Status |
| --- | --- | --- |
| 2.2.2 | Properties (incl. UserProperty) | ✅ |
| 3.1.2.11 | CONNECT properties (Session expiry, Receive Max, Max packet size, Topic alias max, Auth method/data) | ✅ |
| 3.2.2 | CONNACK properties (Assigned client id, Server keep alive, Reason string, etc.) | ✅ |
| 3.3.2.3 | PUBLISH properties (Topic alias, Response topic, Correlation data, Subscription ids, Content type) | ✅ |
| 3.4   | PUBACK reason codes + properties | ✅ |
| 3.8.3.1 | SUBSCRIBE options (NoLocal, RetainAsPublished, Retain Handling) | ✅ |
| 3.14  | DISCONNECT with reason code + Server Reference / Session Expiry override | ✅ |
| 3.15  | AUTH (Enhanced Auth challenge/response) | 🟡 packet codec done, public auth handler pending |
| 4.7.3 | Shared subscriptions (`$share/...`) | ❌ tracked |
| 4.7.2 | Topic alias inbound/outbound management | ❌ tracked (`TopicAliasManager` stub) |

## Auto-reconnect

`MqttReconnectPolicy.Exponential()` / `Fixed(delay)` is exposed on `MqttClientOptions`. The current implementation surfaces `Disconnected` events but the automatic reconnect loop is tracked as a follow-up todo.

## Persistence

`IPersistentSessionStore` is pluggable; `InMemorySessionStore` ships by default. File / SQLite implementations are deferred.

## NativeAOT

`net10.0` build enables `IsAotCompatible`, `EnableAotAnalyzer`, `EnableTrimAnalyzer`. The `Mqtt.Client.AotTests` project publishes with `PublishAot=true` and exercises the public API end-to-end as a CI gate.
