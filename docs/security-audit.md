# Security assessment

_Last reviewed: 2026-06-17._

This document captures the threat model and the security audit performed against
`Mqtt.Client`. Findings are tracked in the table below.

## Threat model

### Assets

1. **Client process memory** — heap, stacks, file handles, sockets.
2. **Authentication credentials** — usernames, passwords, client certificates,
   MQTT 5 enhanced-auth tokens.
3. **Application data** — payloads carried over PUBLISH packets.
4. **TLS session integrity** — broker identity and confidentiality of bytes on
   the wire.

### Attackers (capabilities)

| Attacker | Capabilities |
| --- | --- |
| Network MITM | Can read/modify/drop bytes between the client and the broker. |
| Malicious broker | Operates a broker the client willingly connects to (e.g. compromised endpoint); can send arbitrary PUBLISH, CONNACK, DISCONNECT, AUTH, ACK, PINGRESP packets. |
| Malicious peer | Can publish to topics this client is subscribed to. |
| Untrusted local code | Outside scope. The library is a network client, not a sandbox. |

### STRIDE-lite (per component)

| Component | Spoofing | Tampering | Repudiation | Info disclosure | DoS | EoP |
| --- | --- | --- | --- | --- | --- | --- |
| **Transport (TLS)** | Broker cert validation enforced; mTLS optional | TLS integrity | n/a | TLS confidentiality | TCP-level only | n/a |
| **Codec** | n/a | Decoder bounded by `MaxIncomingPacketSize` | n/a | n/a | Var-int overflow guarded; max packet size enforced | n/a |
| **Dispatcher** | n/a | Trie match is alloc-free + deterministic | n/a | n/a | Per-subscription bounded channel | n/a |
| **Subscription** | n/a | n/a | n/a | n/a | `MqttOverflowMode` lets caller choose Wait/Drop | n/a |
| **Pending acks** | n/a | n/a | n/a | n/a | Bounded by `ReceiveMaximum` (default 65535) | n/a |

## Findings

| # | Severity | Component | Finding | Status |
| --- | --- | --- | --- | --- |
| 1 | High | TLS | Default TLS used `SslProtocols.None` (OS picks; may include TLS 1.0/1.1 on legacy machines). | **Fixed** — `ApplySecureTlsDefaults` pins to TLS 1.2/1.3 (TLS 1.2 only on netstandard2.1). |
| 2 | High | TLS | Certificate revocation checking defaulted off. | **Fixed** — defaults to `X509RevocationMode.Online`. Override via `WithTls(...)`. |
| 3 | High | Codec | Decoder accepted arbitrarily large `Remaining Length` (up to 256 MiB per the spec) without a client cap → malicious broker could exhaust client memory. | **Fixed** — `MqttClientOptions.MaxIncomingPacketSize` (default 1 MiB) enforced in `MqttPacketDecoder.TryDecode`. |
| 4 | Medium | Codec | Var-int parser must not loop forever on malformed input. | **Verified** — `TryReadVarInt` bounds the multiplier at `128^3`; throws `MqttProtocolException` past that. Pinned by `tests/Mqtt.Client.FuzzTests` decoder harness. |
| 5 | Medium | Dispatcher | `TopicFilterTrie` recursion depth equals topic-level count; deeply nested topics could blow the stack. | **Accepted** — MQTT spec caps topic length at 65535 bytes, levels separated by `/`. Practical max depth ~30k. The decoder rejects whole packets over `MaxIncomingPacketSize`, which transitively bounds incoming topic length. Not exploitable in defaults. |
| 6 | Medium | Pending acks | `_pendingAcks` ConcurrentDictionary could grow unbounded if broker never acks. | **Mitigated** — bounded by `ReceiveMaximum` (we advertise on CONNECT). Default 65535; lower for stricter limits. |
| 7 | Low | Outbound queue | Outbound `Channel<OutboundEnvelope>` bounded at 1024. | **By design** — capacity surfaces via `TryPublish` returning `false`. |
| 8 | Low | Auth | Password bytes stored in `MqttClientOptions.Password` as `byte[]` and never explicitly zeroed. | **Accepted** — .NET makes deterministic zeroing of managed bytes impossible; this matches `System.Net.Http` etc. Document for users handling secrets. |
| 9 | Info | DI | `MqttClient` registered as singleton — sharing across requests is intended. | **Documented** in `docs/samples.md`. |
| 10 | Info | Memory | Pooled `MqttBufferWriter` returns its rent on `Dispose`; verified by `using` everywhere in the encoder and `OutboundEnvelope.Dispose`. | **Verified** — IDisposableAnalyzers enabled. |

## Tooling

- **CA-rules security sweep** (`CA21xx`/`CA31xx`/`CA53xx`) — enabled at error severity
  in `.editorconfig`; no findings on `src/`.
- **`dotnet list package --vulnerable --include-transitive`** — run in CI on every
  PR; build fails on any reported CVE.
- **`gitleaks`** — runs in CI on every push/PR; fails the build on any secret pattern.
- **SharpFuzz + libFuzzer** — `tests/Mqtt.Client.FuzzTests` runs the decoder, codec
  round-trip, and topic-trie harnesses for ~30 s per harness on every PR; longer
  `workflow_dispatch` runs available. Any discovered crash is pinned by a reproducer
  test in `tests/Mqtt.Client.UnitTests/FuzzFindingsReproducerTests.cs`.

## Process

Re-run this assessment on every major release. New findings are added to the table
with status (Fixed / Mitigated / Accepted-with-rationale).
