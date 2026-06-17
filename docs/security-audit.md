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
| 4 | Medium | Codec | Var-int parser must not loop forever on malformed input. | **Verified** — `TryReadVarInt` bounds the multiplier at `128^3`; throws `MqttProtocolException` past that. Pinned by `tests/Mqtt.Client.FuzzTests` decoder harness and unit test `Decoder_throws_on_malformed_var_int_with_too_many_continuation_bytes`. |
| 5 | Medium | Dispatcher | `TopicFilterTrie` recursion depth equals topic-level count; deeply nested topics could blow the stack. | **Accepted** — MQTT spec caps topic length at 65535 bytes, levels separated by `/`. Practical max depth ~30k. The decoder rejects whole packets over `MaxIncomingPacketSize`, which transitively bounds incoming topic length. Not exploitable in defaults. |
| 6 | Medium | Pending acks | `_pendingAcks` ConcurrentDictionary could grow unbounded if broker never acks. | **Mitigated** — bounded by `ReceiveMaximum` (we advertise on CONNECT). Default 65535; lower for stricter limits. |
| 7 | Low | Outbound queue | Outbound `Channel<OutboundEnvelope>` bounded at 1024. | **By design** — capacity surfaces via `TryPublish` returning `false`. |
| 8 | Low | Auth | Password bytes stored in `MqttClientOptions.Password` as `byte[]` and never explicitly zeroed. | **Accepted** — .NET makes deterministic zeroing of managed bytes impossible; this matches `System.Net.Http` etc. Document for users handling secrets. |
| 9 | Info | DI | `MqttClient` registered as singleton — sharing across requests is intended. | **Documented** in `docs/samples.md`. |
| 10 | Info | Memory | Pooled `MqttBufferWriter` returns its rent on `Dispose`; verified by `using` everywhere in the encoder and `OutboundEnvelope.Dispose`. | **Verified** — IDisposableAnalyzers enabled. |

## v0.9 branch re-audit (additions from zero-copy publish + pipe tuning)

| # | Severity | Component | Finding | Status |
| --- | --- | --- | --- | --- |
| F1 | Low | `Encoder.EncodePublishHeader` | `headerLen + Payload.Length` could overflow `int` when payload is near 2 GiB → wire-corruption risk. | **Fixed** — `checked()` arithmetic; `PatchRemainingLength` still enforces the MQTT 268_435_455 cap as defence in depth. |
| F2 | Low | `OutboundEnvelope.Dispose` | `ArrayPool.Return` did not clear the rented buffer; payload bytes (potentially containing tokens / secrets) could be observed by the next pool tenant within the same process. | **Mitigated** — `MqttClientOptions.ClearPooledBuffers` opt-in (default `false`); when `true`, `ArrayPool.Return(arr, clearArray: true)`. Trade-off: small CPU cost; opt-in keeps the default fast path zero-overhead. |
| F3 | Low | `MqttClient.WriteRawAsync` | Chunk loop on `output.GetMemory(...)` could infinite-loop if a `PipeWriter` returned a zero-length buffer (contract violation, but defended for safety). | **Fixed** — defensive `throw new MqttConnectionException("PipeWriter returned zero-length buffer.")`. |
| F4 | Medium | Pipe `PauseWriterThreshold` | Hardcoded 1 MiB; if user raises `MaxIncomingPacketSize` above 1 MiB, the pipe would back up before buffering a full packet → deadlock. | **Fixed** — `MqttClient.CreateTransportFactory` derives the pause threshold from `Math.Max(1 MiB, MaxIncomingPacketSize × 2)` and plumbs it into `TcpTransportFactory` / `StreamPipeReader` / `StreamPipeWriter`. |
| F5 | Info | 8 KB minimum pipe segment | Per-connection memory footprint up by ~12 KB vs default segment size; reduces segment-allocation churn at large payloads (~50 % less per-message alloc at 64 KB receive). | **Accepted with rationale** — net positive for any session that handles more than a handful of messages. Documented. |
| F6 | Info | `PublishAsync` payload aliasing | `PublishAsync(ReadOnlyMemory<byte>)` retains the caller's memory until the wire write completes; mutation invalidates wire bytes. | **Documented** — `docs/samples.md` updated to note the contract; `TryPublish` copies via `ArrayPool` and is safe to mutate after return. |
| F7 | Verified | Outbound pause threshold | Same scaling applied to outbound `StreamPipeWriter` — back-pressures TCP send buffer at `MaxIncomingPacketSize × 2`. | **Fixed** along with F4. |
| F8 | Verified | `UnsubscribeOnServerAsync` infinite wait | The `MqttSubscription.DisposeAsync` callback awaited the UNSUBACK on `CancellationToken.None`. A broker that never acknowledged UNSUBSCRIBE would hang the disposing caller indefinitely. | **Fixed** — bounded wait via `OperationTimeout` linked to caller ct and `_loopCts.Token`, so client shutdown immediately unblocks any pending unsubscribe. Caught by unit testing during this audit. |

## Tooling re-run

- `dotnet build` with CA security rules (`CA21xx`/`CA31xx`/`CA53xx`) at error severity — **0 findings** on `src/`.
- `dotnet list package --vulnerable --include-transitive` — clean.
- `Roslynator` at error severity across the diff — clean.
- `IDisposableAnalyzers` — clean.
- Unit + integration + AOT all pass.

## Process

Re-run this assessment on every major release. New findings are added to the table
with status (Fixed / Mitigated / Accepted-with-rationale).
