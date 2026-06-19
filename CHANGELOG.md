# Changelog

All notable changes to **Mqtt.Client** are recorded here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [SemVer](https://semver.org/) (post-1.0).

## [Unreleased]

### Added
- **SOCKS5 proxy support** (RFC 1928) for the TCP and TLS transports, with optional RFC 1929
  username/password authentication. Configure via `MqttClientBuilder.WithSocks5Proxy(...)` or
  `MqttClientOptions.Proxy` (`Socks5ProxyOptions`). Broker host names are resolved at the proxy
  by default (remote DNS); set `ResolveHostnamesRemotely = false` to resolve locally. WebSocket
  transports do not support SOCKS5. New public `Socks5ProxyException` surfaces proxy failures.
- `tests/README.md`, `src/README.md` — per-folder content guides.
- `PublishAsync(string, ReadOnlySequence<byte>, …)` overload — publish payloads that span
  multiple buffer segments (pre-chunked / pipelined data) without first concatenating them.
- `MqttMessage.PayloadMemory` and `MqttLastWill.PayloadMemory` — contiguous `ReadOnlyMemory<byte>`
  views and convenience initializers over the new sequence-typed `Payload`.
- **Inline-handler subscriptions:** `SubscribeAsync(string, Func<MqttMessage, ValueTask>, …)`
  delivers each message on the receive loop with a **true zero-copy** payload (a slice of the
  receive buffer, valid only inside the handler). Back-pressure flows to the broker while it runs.

### Changed
- **Codec performance.** The encoder no longer reserves 4 bytes for every remaining-length field and
  shifts the body to fit (it reserves 1 and grows in place only for large packets), and MQTT 5
  property blocks are written **inline** behind a 1-byte length placeholder instead of into a second
  pooled `MqttBufferWriter` that was copied in — removing a pool rent + copy per properties-bearing
  packet. The decoder reads contiguous (single-segment) packets directly from the segment span via
  `BinaryPrimitives`, falling back to `SequenceReader<byte>` only for multi-segment input, and avoids
  a `List<uint>` allocation for the common single subscription identifier. Result (256 B PUBLISH,
  same hardware): encode ≈ 100 → 60 ns and per-encode allocation 64 → 0–32 B; small-packet decode
  ≈ 229 → 158 ns. All AOT-clean (no `unsafe`/suppressions).
- **API (breaking, pre-1.0):** the default payload type is now `ReadOnlySequence<byte>`.
  `MqttMessage.Payload` and `MqttLastWill.Payload` changed from `ReadOnlyMemory<byte>` to
  `ReadOnlySequence<byte>`; the `ReadOnlyMemory<byte>` form is available via the `PayloadMemory`
  property/initializer and the `PublishAsync(ReadOnlyMemory<byte>)` overload (unchanged signature).
- **Inbound payloads are pooled by default (breaking behavior).** Channel-delivered `MqttMessage`s
  now own an `ArrayPool<byte>` buffer and **must be disposed** after use; the decoder no longer
  allocates a `byte[]` per PUBLISH. Set `MqttClientOptions.RetainableInboundMessages = true` to
  restore garbage-collected payloads that may be retained freely (replaces the former
  `ReuseInboundBuffers` opt-in, with the default inverted). MQTT 5 `CorrelationData` rides in the
  same pooled buffer.
- QoS > 0 publish, SUBSCRIBE and UNSUBSCRIBE acks await a pooled `IValueTaskSource`
  (`AckCompletionSource`) instead of allocating a `TaskCompletionSource<object?>` + `Task` per
  operation. Publish-ack latency timing uses `Stopwatch.GetTimestamp()` (no `Stopwatch` allocation).
- Outbound MQTT 5 authentication data is held as `ReadOnlyMemory<byte>` and written directly (no
  `byte[]` copy); `FileSessionStore` writes through a pooled buffer.

### Notes
- The receive hot path now allocates **no `byte[]`**: inline handlers are fully zero-copy, and the
  channel path copies into pooled buffers. Remaining copies are confined to data that escapes to the
  caller with indefinite lifetime (inbound auth data, persisted-message reads, one-time password
  encoding) and are documented in source.
- No new third-party dependency: the pooling/`IValueTaskSource` techniques (inspired by
  [.NEXT](https://github.com/dotnet/dotNext)) are implemented on BCL primitives
  (`ManualResetValueTaskSourceCore<T>`, `ArrayPool<T>`, `ReadOnlySequence<T>`) that are available on
  every target framework (incl. `netstandard2.1`) and NativeAOT-clean.

## [0.9.3] — 2026-06-17

### Added
- `src/README.md` and `tests/README.md` describing every file / project.
- Unit tests for subscription overflow modes (Wait / DropOldest / DropNewest), decoder property branches for SUBACK/UNSUBACK/DISCONNECT/AUTH, sequence-reader segmented input.

### Changed
- `MqttClient.ReadLoopAsync` tracks bytes consumed via a running counter delta instead of `buffer.Slice(start, end).Length` (no segment walk).
- `OutboundEnvelope` is now a `readonly struct` (was a sealed class). Saves ~50 B per publish; verified at 256 B QoS 0.

### Coverage
- Unit: 76.6 % → **81.1 %** line, 58.3 % → **65.4 %** branch.
- Combined (+ integration): 83.1 % → **86.4 %** line, 65.2 % → **71.9 %** branch.

## [0.9.2] — 2026-06-17

### Added
- `FakePipeTransport` + `FakeBroker` test infrastructure — in-process `IMqttTransport` over paired `Pipe`s so `MqttClient` can be driven end-to-end without a real broker.
- 8 new unit-test files (51 tests added): MqttClient via fake, codec all-packets, persistence, exceptions/event args, DI extensions, subscription extensions, builder additional, decoder malformed-input.
- `MqttClientOptions.ClearPooledBuffers` (opt-in) — zero pooled payload buffers on `ArrayPool.Return` for callers handling secrets.
- Security audit findings F1–F8 documented in `docs/security-audit.md` (post-v0.9 branch re-audit).

### Fixed
- **Real bug** — `UnsubscribeOnServerAsync` waited forever on `CancellationToken.None` for UNSUBACK. Bounded wait now linked to `OperationTimeout` and client-shutdown CTS so disposing the client or hitting the operation timeout unblocks any pending unsubscribe.
- `EncodePublishHeader` length arithmetic now `checked()` (defence in depth on top of `PatchRemainingLength`'s 268,435,455 cap).
- `WriteRawAsync` chunk loop defensively throws on a zero-length `PipeWriter.GetMemory` result (would otherwise spin forever per contract violation).

### Changed
- Pipe `PauseWriterThreshold` now scales with `MaxIncomingPacketSize × 2` (with a 1 MiB floor). Previously hardcoded 1 MiB; raising `MaxIncomingPacketSize` above 1 MiB now works without deadlocking.

### Coverage
- Unit: 36.4 % → **76.6 %**; combined: 65.7 % → **83.1 %**. Tests: 30 → **81**.

## [0.9.1] — 2026-06-17

### Changed (performance)
- **Zero-copy publish** — `MqttPacketEncoder.EncodePublishHeader` writes only the fixed + variable header (with payload length pre-counted into the var-int). `MqttClient.PublishAsync` / `TryPublish` then submit the payload as a separate `PipeWriter` write, eliminating the per-publish memcpy through `MqttBufferWriter`.
- 8 KB minimum pipe segment size in `StreamPipeReader` / `StreamPipeWriter` to reduce segment churn on large inbound payloads.
- `SubscribeReceiveBenchmark` made fair (pre-built `MqttApplicationMessage` on both paths; previously the Mqtt.Client side rebuilt per iteration, inflating its alloc count).

### Benchmarks (vs MQTTnet 5.x, 256 B payload)
- Publish QoS 0: 0.19× → **0.12×** time.
- Publish QoS 1: 0.84× → **0.95×** time.
- Publish QoS 2: 0.85× → **0.97×** time.
- Subscribe receive: 1.07× → **0.99×** time, alloc 1.62× (at 64 KB) → **0.75×**.
- Connect: 1.05× → **0.86×** time.

## [0.9] — 2026-06-17

### Added
- Initial public release. High-performance, low-allocation MQTT 3.1.1 + 5.0 client for .NET.
- Multi-TFM: `netstandard2.1`, `net8.0`, `net9.0`, `net10.0` (NativeAOT-clean on net10.0).
- Channels-style API (`MqttSubscription.Reader: ChannelReader<MqttMessage>`, `TryPublish` / `PublishAsync`).
- TCP, TLS, WebSocket, Secure WebSocket transports.
- QoS 0/1/2 publish; auto-reconnect with exponential backoff; MQTT 5 outbound topic alias.
- Fluent `MqttClientBuilder`, source-generated logging, `System.Diagnostics.Metrics`, ActivitySource, DI extensions.
- BenchmarkDotNet comparison vs MQTTnet 5.x; SharpFuzz harnesses (Linux); unit + integration + AOT smoke + fuzz reproducer suites.
- Secure defaults: TLS 1.2/1.3, CRL on, `MaxIncomingPacketSize` capped at 1 MiB.
- `Nerdbank.GitVersioning` for `version.json`-driven versioning; pack with SourceLink + symbols.

[Unreleased]: https://github.com/marcschier/mqtt-client/compare/v0.9.3...HEAD
[0.9.3]: https://github.com/marcschier/mqtt-client/releases/tag/v0.9.3
[0.9.2]: https://github.com/marcschier/mqtt-client/releases/tag/v0.9.2
[0.9.1]: https://github.com/marcschier/mqtt-client/releases/tag/v0.9.1
[0.9]: https://github.com/marcschier/mqtt-client/releases/tag/v0.9
