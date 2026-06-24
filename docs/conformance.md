# MQTT spec conformance — Mqtt.Client (1.0.0)

Maps OASIS MQTT 3.1.1 and 5.0 normative chapters to the implementation in `Mqtt.Client`. Citations point at the implementing code and the test that exercises it.

## Status legend

- ✅ Implemented + tested (unit, integration, or fuzz harness)
- 🟡 Implemented, but with a known limitation or missing test coverage
- ❌ Not implemented yet — tracked in [Remaining work](#remaining-work)

## MQTT 3.1.1 ([OASIS 3.1.1](http://docs.oasis-open.org/mqtt/mqtt/v3.1.1/os/mqtt-v3.1.1-os.html))

| Section | Feature | Status |
| --- | --- | --- |
| 1.5.x | Data representations (var int, UTF-8 string, binary data) | ✅ `MqttBufferWriter` / `MqttSequenceReader`, fuzzed by `DecoderHarness` |
| 3.1 | CONNECT | ✅ `MqttPacketEncoder.EncodeConnect` |
| 3.2 | CONNACK (incl. v3.1.1 return-code mapping to reason codes) | ✅ `MqttPacketDecoder.DecodeConnAck` + unit test `ConnAck_v311_maps_return_codes` |
| 3.3 | PUBLISH (QoS 0/1) | ✅ end-to-end integration tests |
| 3.3 | PUBLISH (QoS 2) — outbound | ✅ PUBREC → PUBREL → await PUBCOMP; the publish completes only on PUBCOMP (`MqttClient.DispatchInboundAsync`). End-to-end tested (`PublishSubscribeTests` QoS2) |
| 3.3 | PUBLISH (QoS 2) — inbound | 🟡 Simplified: the client answers PUBREC and responds to PUBREL with PUBCOMP, but delivers the message immediately with **no exactly-once dedup** of a redelivered PUBLISH (`MqttClient.cs` inbound handler). Tracked as `conf-qos2-inbound` |
| 3.4 | PUBACK | ✅ |
| 3.5 / 3.6 / 3.7 | PUBREC / PUBREL / PUBCOMP packets | ✅ codec + dispatcher (`EnqueuePubRel` / `EnqueuePubComp`) |
| 3.8 | SUBSCRIBE | ✅ |
| 3.9 | SUBACK | ✅ |
| 3.10 | UNSUBSCRIBE | ✅ with bounded UNSUBACK wait |
| 3.11 | UNSUBACK | ✅ |
| 3.12 | PINGREQ | ✅ keep-alive loop drives it |
| 3.13 | PINGRESP | ✅ |
| 3.14 | DISCONNECT | ✅ |
| 4.3.3 | QoS 2 exactly-once delivery guarantee | 🟡 Outbound only; inbound dedup is missing (see PUBLISH QoS 2 inbound above) |
| 4.4 | Message redelivery after reconnect (DUP) | ❌ In-flight QoS 1/2 publishes are faulted on disconnect and not resent; tracked as `conf-persistence` |
| 4.6 | Last-will (incl. v5 will properties / will delay) | ✅ encoder + builder API; broker performs delivery |
| 4.7 | Topic names / wildcards / `$` topics | ✅ `TopicFilterTrie` + property-based fuzz harness (`TopicTrieHarness`) |
| 4.7.3 | Retained messages | 🟡 client passes the `retain` flag end-to-end; broker honours storage |

## MQTT 5.0 ([OASIS 5.0](https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html))

| Section | Feature | Status |
| --- | --- | --- |
| 2.2.2 | Properties (incl. UserProperty) | ✅ |
| 3.1.2.11 | CONNECT properties (Session expiry, Receive Max, Max packet size, Topic alias max, Auth method/data) | ✅ |
| 3.2.2 | CONNACK properties (Assigned client id, Server keep alive, Reason string, …) | ✅ + unit tests |
| 3.3.2.3 | PUBLISH properties (Topic alias, Response topic, Correlation data, Subscription ids, Content type, Payload format, Message expiry) | ✅ + unit test `Publish_v5_with_full_properties_roundtrips` |
| 3.4 | PUBACK reason codes + properties | ✅ |
| 3.5 / 3.6 / 3.7 | PUBREC / PUBREL / PUBCOMP reason codes + properties | ✅ |
| 3.8.3.1 | SUBSCRIBE options (NoLocal, RetainAsPublished, Retain Handling) | ✅ |
| 3.14 | DISCONNECT with reason code + ServerReference + SessionExpiryInterval | ✅ + unit test |
| 3.15 | AUTH (enhanced authentication: initial + live re-auth) | ✅ `IMqttAuthenticationHandler` drives the SASL-style exchange on connect and on `ReauthenticateAsync`; tested in `AuthenticationTests` (incl. `Reauthenticate_*`) |
| 4.7.2 (outbound) | Topic alias outbound | ✅ `TopicAliasManager` + `TopicAliasManagerTests` |
| 4.7.2 (inbound) | Topic alias inbound expansion | ✅ `MqttClient._inboundAliases` register/resolve; tested `Inbound_topic_alias_registers_and_resolves` |
| 4.8.2 | Shared subscriptions (`$share/<group>/<filter>`) | 🟡 client strips the `$share/<group>/` prefix and subscribes to the underlying filter (`StripSharedSubscriptionPrefix`, unit-tested); end-to-end broker fan-out is not integration-tested |
| 4.10 | Subscription identifier | ✅ assigned per v5 subscription; inbound dispatch via the `_subsById` fast-path; tested (`Subscription_identifier_*`) |
| 3.2.2.x | Honour CONNACK limits (Maximum QoS, Retain Available, Maximum Packet Size, Topic Alias Maximum) | ❌ advertised values are read but not enforced on outbound; tracked as `conf-broker-limits` |
| 4.9 | Receive Maximum flow control | ❌ advertised in CONNECT and read from CONNACK, but outbound in-flight QoS>0 is not bounded to it; tracked as `conf-receive-max` |
| 4.4 | Session state persistence + redelivery | ❌ `IPersistentSessionStore` / `FileSessionStore` exist but are not yet wired into the client; tracked as `conf-persistence` |

## Client features beyond the wire spec

These are not MQTT normative requirements but affect how credentials and reconnects behave:

- **Auto-reconnect** with exponential backoff + jitter (`MqttReconnectPolicy`).
- **Async credential reload on reconnect** — `IMqttCredentialsProvider` is consulted on every (re)connect, so rotated passwords (SAS tokens, refreshed JWT/OAuth bearers) are presented each time.
- **Kubernetes service-account-token auth** — `KubernetesServiceAccountTokenCredentialsProvider` reads the SAT from a mounted file and reconnects (via `MqttClient.ReconnectAsync` + `IMqttCredentialsChangeNotifier`) when the kubelet rotates it.

## Remaining work

Conformance gaps, highest-impact first. Each is its own change (code + tests + a flip of the rows above to ✅).

1. **`conf-qos2-inbound` — inbound QoS 2 exactly-once.** Track received packet identifiers between an inbound PUBLISH and its PUBREL, deliver the message exactly once, dedup a redelivered PUBLISH (re-ack PUBREC without re-dispatching), and release the state on PUBREL → PUBCOMP.
2. **`conf-persistence` — session persistence + redelivery on reconnect.** Wire `IPersistentSessionStore`: save pending QoS 1/2 publishes on send, remove them on their terminal ack, and on a reconnect with Session Present reload the pending set, resend with DUP = 1, and restore the packet-identifier reservations. (Largest item; may be phased — outbound redelivery first, then inbound QoS 2 receipt state.)
3. **`conf-receive-max` — Receive Maximum flow control.** Bound outbound in-flight QoS>0 publishes with a quota sized to the broker's advertised Receive Maximum: acquire before sending, release on the terminal ack (PUBACK / PUBCOMP).
4. **`conf-broker-limits` — honour CONNACK limits.** Reject or cap outbound by Maximum Packet Size, downgrade or reject publishes above Maximum QoS, reject `retain` when Retain Available is false, and cap outbound topic aliases to the broker's Topic Alias Maximum.
5. **`conf-request-response` *(optional)* — request/response helper.** An ergonomic API over the already-supported Response Topic / Correlation Data / Request Response Information properties.
