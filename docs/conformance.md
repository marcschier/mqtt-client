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
| 3.3 | PUBLISH (QoS 2) — inbound | ✅ Exactly-once receiver: replies PUBREC, delivers the message once, de-duplicates a redelivered PUBLISH (re-acks PUBREC without re-dispatching), and answers PUBREL with PUBCOMP (`MqttClient._inboundQoS2`). Tested in `InboundQoS2Tests` |
| 3.4 | PUBACK | ✅ |
| 3.5 / 3.6 / 3.7 | PUBREC / PUBREL / PUBCOMP packets | ✅ codec + dispatcher (`EnqueuePubRel` / `EnqueuePubComp`) |
| 3.8 | SUBSCRIBE | ✅ |
| 3.9 | SUBACK | ✅ |
| 3.10 | UNSUBSCRIBE | ✅ with bounded UNSUBACK wait |
| 3.11 | UNSUBACK | ✅ |
| 3.12 | PINGREQ | ✅ keep-alive loop drives it |
| 3.13 | PINGRESP | ✅ |
| 3.14 | DISCONNECT | ✅ |
| 4.3.3 | QoS 2 exactly-once delivery guarantee | ✅ Outbound (PUBREC → PUBREL → await PUBCOMP) and inbound (PUBREC, single delivery + redelivery dedup → PUBCOMP); `InboundQoS2Tests`, `PublishSubscribeTests` |
| 4.4 | Message redelivery after reconnect (DUP) | ✅ when persistence is configured (`WithPersistence`): in-flight QoS 1/2 publishes are saved, resent with DUP = 1 and their packet ids on a Session-Present reconnect, and the original awaiter completes on the post-reconnect ack; `PersistenceRedeliveryTests` |
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
| 4.10 (helper) | Request/response (Response Topic + Correlation Data) | ✅ `MqttClient.RequestAsync` publishes with a Response Topic + unique Correlation Data and completes on the correlated reply over a lazily-established response subscription; `MqttRequestOptions` (topic/QoS/timeout); `RequestResponseTests` |
| 3.2.2.x | Honour CONNACK limits (Maximum QoS, Retain Available, Maximum Packet Size, Topic Alias Maximum) | ✅ enforced on the publish path per `MqttClientOptions.BrokerLimitBehavior` (`Reject` throws / `Adapt` downgrades QoS + drops retain/alias; oversized packets always throw); `BrokerLimitsTests` |
| 4.9 | Receive Maximum flow control | ✅ outbound in-flight QoS&gt;0 bounded to the broker's advertised Receive Maximum via `MqttClientOptions.ReceiveMaximumBehavior` (`Backpressure` default / `Reject`); `BrokerLimitsTests` |
| 4.4 | Session state persistence + redelivery | ✅ `IPersistentSessionStore` / `FileSessionStore` are wired into the client (opt-in via `WithPersistence`): outbound QoS 1/2 publishes are saved on send, removed on their terminal ack, and redelivered with DUP on a Session-Present reconnect; inbound QoS 2 receipt ids are persisted (companion `IPersistentInboundQoS2Store`) so de-dup survives reconnect; clean-session reconnect discards both. `PersistenceRedeliveryTests` |

## Client features beyond the wire spec

These are not MQTT normative requirements but affect how credentials and reconnects behave:

- **Auto-reconnect** with exponential backoff + jitter (`MqttReconnectPolicy`).
- **Async credential reload on reconnect** — `IMqttCredentialsProvider` is consulted on every (re)connect, so rotated passwords (SAS tokens, refreshed JWT/OAuth bearers) are presented each time.
- **Kubernetes service-account-token auth** — `KubernetesServiceAccountTokenCredentialsProvider` reads the SAT from a mounted file and reconnects (via `MqttClient.ReconnectAsync` + `IMqttCredentialsChangeNotifier`) when the kubelet rotates it.

## Remaining work

All previously-tracked conformance gaps (inbound QoS 2 exactly-once, session persistence + redelivery, Receive Maximum flow control, honouring CONNACK limits, and the request/response helper) are now implemented and tested. The only items below ✅ are:

- **Shared subscriptions (4.8.2)** — 🟡 the `$share/<group>/` prefix is stripped and the underlying filter is subscribed (unit-tested), but end-to-end broker fan-out is not integration-tested. Closing this is a test-coverage task (a broker that distributes a shared group across multiple subscribers), not a code gap.

Possible future enhancements (beyond strict conformance): a configurable per-client response-topic scheme for `RequestAsync`, and persisting MQTT 5 publish properties in `FileSessionStore` (currently best-effort — properties are not serialised to disk).
