# `src/Mqtt.Client/`

Single project, single NuGet (`Mqtt.Client`). Multi-TFM (`netstandard2.1`, `net8.0`,
`net9.0`, `net10.0`); the `net10.0` build is `IsAotCompatible=true` and ships
without a single trim/AOT warning.

Files are flat — there are no subfolders — so the public surface is small enough to
scan in one screen.

## Public surface (touched by every consumer)

| File | Purpose |
| --- | --- |
| `MqttClient.cs` | The client. Channels-style `PublishAsync` / `TryPublish` / `SubscribeAsync` / `DisconnectAsync`, hidden send + receive loops, auto-reconnect, vectored writes. |
| `MqttClientBuilder.cs` | Fluent builder. URI parsing for `mqtt://` / `mqtts://` / `ws://` / `wss://`; credentials, mTLS, keep-alive, last-will, reconnect, persistence, logging, SOCKS5 proxy. |
| `MqttClientOptions.cs` | Strongly-typed options bag. Includes `MaxIncomingPacketSize`, `ClearPooledBuffers`, `OperationTimeout`, `ReceiveMaximum`, etc. Plus `MqttTransportType` and `MqttReconnectPolicy`. |
| `MqttSubscription.cs` | Per-subscription `ChannelReader<MqttMessage>`. Configurable backpressure (Wait / DropOldest / DropNewest). |
| `MqttSubscriptionOptions.cs` | Per-subscribe overrides + `MqttOverflowMode`. |
| `MqttMessage.cs` | Inbound message DTO. Plus `MqttConnectResult`, `MqttPublishResult`. |
| `PublicTypes.cs` | `MqttPublishProperties`, `MqttLastWill`, `MqttUserProperty`. |
| `Enums.cs` | `MqttProtocolVersion`, `MqttQoS`, `MqttConnectionState`. |
| `Exceptions.cs` | `MqttProtocolException`, `MqttConnectionException`, `Socks5ProxyException`. |
| `Socks5ProxyOptions.cs` | SOCKS5 proxy configuration (`Host`, `Port`, `Username`, `Password`, `ResolveHostnamesRemotely`). |

## Codec (protocol)

| File | Purpose |
| --- | --- |
| `Encoder.cs` | `MqttPacketEncoder.EncodeXxx` — every MQTT 3.1.1 + 5.0 control packet. Includes `EncodePublishHeader` for the zero-copy publish path (writes only the fixed + variable header; payload is appended on the wire via a separate `PipeWriter` write). |
| `Decoder.cs` | `MqttPacketDecoder.TryDecode` — incremental decoder over `ReadOnlySequence<byte>`. Enforces `MqttClientOptions.MaxIncomingPacketSize`. |
| `Packets.cs` | Internal record types for each MQTT control packet. |
| `PacketType.cs`, `PropertyId.cs`, `ReasonCode.cs` | MQTT 3.1.1/5 enums (reason code is public; the others internal). |
| `BufferWriter.cs` | Pooled writer (`ArrayPool<byte>`) used by the encoder. |
| `SequenceReader.cs` | Allocation-free reader over `ReadOnlySequence<byte>` used by the decoder. |

## Transport

| File | Purpose |
| --- | --- |
| `IMqttTransport.cs` | `IMqttTransport` (PipeReader/PipeWriter pair) and `IMqttTransportFactory`. |
| `TcpTransport.cs` | TCP transport over `Socket` + `NetworkStream`. Includes `StreamPipeReader` / `StreamPipeWriter` Stream→Pipe pumps with tunable pause threshold. |
| `TlsTransport.cs` | TCP+TLS via `SslStream`; secure defaults (TLS 1.2/1.3, CRL on) applied in `MqttClient.ApplySecureTlsDefaults`. mTLS via `MqttClientBuilder.WithClientCertificate`. |
| `WebSocketTransport.cs` | `ClientWebSocket`-based transport with subprotocol `"mqtt"`. |
| `ISocketConnector.cs` | `ISocketConnector` seam (returns a connected `Socket` for TCP/TLS) + `SocketConnect` helper. |
| `DefaultConnector.cs` | `DefaultConnector` — direct (no-proxy) socket connector. |
| `Socks5Connector.cs` | `Socks5SocketConnector` — tunnels TCP/TLS connections through a SOCKS5 proxy (RFC 1928 / RFC 1929). |

## Internals

| File | Purpose |
| --- | --- |
| `PacketIdAllocator.cs` | Lock-free CAS bitmap allocator for MQTT packet IDs (1..65535). |
| `TopicFilterTrie.cs` | Allocation-free topic-filter trie (handles `+`, `#`, `$`-topic rule). Used by the dispatcher. |
| `TopicAliasManager.cs` | MQTT 5 outbound topic-alias manager. |
| `IPersistentSessionStore.cs` | Pluggable QoS 1/2 in-flight persistence. Default impl `InMemorySessionStore`. |

## Diagnostics

| File | Purpose |
| --- | --- |
| `Log.cs` | Source-generated `[LoggerMessage]` log entries (`MqttLog`). |
| `Metrics.cs` | `System.Diagnostics.Metrics` counters / histograms under meter `Mqtt.Client`. |
| `ActivitySource.cs` | OpenTelemetry-friendly `ActivitySource` named `Mqtt.Client`. |

## Dependency injection

| File | Purpose |
| --- | --- |
| `ServiceCollectionExtensions.cs` | `services.AddMqttClient(...)` + `AddMqttClientHostedReconnect()` (registers `MqttClientHostedService`). |

## Compatibility

`Polyfills.cs` provides `IsExternalInit` / `RequiredMemberAttribute` /
`SetsRequiredMembersAttribute` shims under `#if NETSTANDARD2_1` so the `init`
accessors and `required` modifier work on that target.
