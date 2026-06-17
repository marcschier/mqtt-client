# MQTT spec conformance — Mqtt.Client v0.9.3

Maps OASIS MQTT 3.1.1 and 5.0 normative chapters to the implementation in `Mqtt.Client`.

## Status legend

- ✅ Implemented + tested (unit, integration, or fuzz harness)
- 🟡 Implemented, additional tests pending
- ❌ Deferred / tracked as a todo

## MQTT 3.1.1 ([OASIS 3.1.1](http://docs.oasis-open.org/mqtt/mqtt/v3.1.1/os/mqtt-v3.1.1-os.html))

| Section | Feature | Status |
| --- | --- | --- |
| 1.5.x | Data representations (var int, UTF-8 string, binary data) | ✅ `MqttBufferWriter` / `MqttSequenceReader`, fuzzed by `DecoderHarness` |
| 3.1 | CONNECT | ✅ `MqttPacketEncoder.EncodeConnect` |
| 3.2 | CONNACK (incl. v3.1.1 return-code mapping to reason codes) | ✅ `MqttPacketDecoder.DecodeConnAck` + unit test `ConnAck_v311_maps_return_codes` |
| 3.3 | PUBLISH (QoS 0/1/2) | ✅ end-to-end integration tests for all three QoS levels |
| 3.4 | PUBACK | ✅ |
| 3.5 / 3.6 / 3.7 | PUBREC / PUBREL / PUBCOMP (full QoS 2 state machine) | ✅ Outbound: dispatcher routes PUBREC → PUBREL, awaits PUBCOMP. Inbound: PUBREL → PUBCOMP responder |
| 3.8 | SUBSCRIBE | ✅ |
| 3.9 | SUBACK | ✅ |
| 3.10 | UNSUBSCRIBE | ✅ with bounded UNSUBACK wait |
| 3.11 | UNSUBACK | ✅ |
| 3.12 | PINGREQ | ✅ keep-alive loop drives it |
| 3.13 | PINGRESP | ✅ |
| 3.14 | DISCONNECT | ✅ |
| 4.6 | Last-will | ✅ encoder + builder API; broker delivery is broker-side |
| 4.7 | Topic names / wildcards / `$` topics | ✅ `TopicFilterTrie` + property-based fuzz harness |
| 4.7.3 | Retained messages | 🟡 client passes `retain` flag end-to-end; broker honours storage |

## MQTT 5.0 ([OASIS 5.0](https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html))

| Section | Feature | Status |
| --- | --- | --- |
| 2.2.2 | Properties (incl. UserProperty) | ✅ |
| 3.1.2.11 | CONNECT properties (Session expiry, Receive Max, Max packet size, Topic alias max, Auth method/data) | ✅ |
| 3.2.2 | CONNACK properties (Assigned client id, Server keep alive, Reason string, …) | ✅ + unit tests |
| 3.3.2.3 | PUBLISH properties (Topic alias, Response topic, Correlation data, Subscription ids, Content type) | ✅ + unit test `Publish_v5_with_full_properties_roundtrips` |
| 3.4 | PUBACK reason codes + properties | ✅ |
| 3.5 / 3.6 / 3.7 | PUBREC / PUBREL / PUBCOMP reason codes + properties | ✅ |
| 3.8.3.1 | SUBSCRIBE options (NoLocal, RetainAsPublished, Retain Handling) | ✅ |
| 3.14 | DISCONNECT with reason code + ServerReference + SessionExpiryInterval | ✅ + unit test |
| 3.15 | AUTH (Enhanced Auth challenge/response) | 🟡 packet codec done; public `IMqttAuthenticationHandler` callback tracked as v1.0 todo (`v1-enhanced-auth`) |
| 4.7.2 (outbound) | Topic alias outbound | ✅ `TopicAliasManager` |
| 4.7.2 (inbound) | Topic alias inbound expansion | ❌ tracked as `v010-inbound-topic-alias` |
| 4.8.2 | Shared subscriptions (`$share/<group>/<filter>`) | ❌ tracked as `v1-shared-subs` |
| 4.10 | Subscription identifier | ❌ tracked as `v010-subscription-id` |

## Subsystems

### Transports
TCP, TCP+TLS (mTLS-ready, TLS 1.2/1.3 default, CRL on), WebSocket, Secure WebSocket — all in.

### Auto-reconnect
`MqttReconnectPolicy.Exponential()` (default) / `Fixed(delay)`. On transport close, the
reconnect loop fires unless the caller called `DisconnectAsync` (manual disconnect).
Outbound publish queue survives across reconnect.

### Persistence
`IPersistentSessionStore` is pluggable; `InMemorySessionStore` ships in-box.
File-based store tracked as `v010-file-persistence`.

### NativeAOT
`net10.0` build is `IsAotCompatible=true` with `EnableAotAnalyzer` / `EnableTrimAnalyzer` /
`EnableSingleFileAnalyzer`. The `Mqtt.Client.AotTests` project publishes with
`PublishAot=true` as a CI gate; zero IL/trim warnings on the library.

### Security
`MqttClientOptions.MaxIncomingPacketSize` (1 MiB default) caps decoder buffering;
`ClearPooledBuffers` (opt-in) zeros pooled payload before pool-return; TLS defaults to
1.2/1.3 + CRL on. Full threat-model and findings in
[`security-audit.md`](./security-audit.md).

### Fuzzing
`tests/Mqtt.Client.FuzzTests` ships SharpFuzz + libFuzzer harnesses for decoder, codec
round-trip, and topic-trie. Crash inputs are auto-replayed by
`FuzzFindingsReproducerTests` on every unit-test run.

### Coverage
Unit: **81.1 %** line / **65.4 %** branch. Combined with integration: **86.4 % / 71.9 %**.
See [`coverage.md`](./coverage.md).
