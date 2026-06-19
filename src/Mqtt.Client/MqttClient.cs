// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Mqtt.Client;

/// <summary>
/// MQTT client. Channels-style API: <c>PublishAsync</c> / <c>TryPublish</c> for sending,
/// <c>SubscribeAsync</c> returning a <see cref="MqttSubscription"/> with a
/// <see cref="System.Threading.Channels.ChannelReader{T}"/> for receiving.
/// </summary>
public sealed class MqttClient : IAsyncDisposable
{
    private readonly MqttClientOptions _options;
    private readonly IMqttTransportFactory _transportFactory;
    private readonly ILogger _logger;
    private readonly MqttMetrics _metrics;
    private readonly IPersistentSessionStore _persistence;

    private readonly Channel<OutboundEnvelope> _outbound;
    private readonly TopicFilterTrie<MqttSubscription> _trie = new();
    private readonly object _subLock = new();
    private readonly PacketIdAllocator _packetIds = new();
    private readonly ConcurrentDictionary<ushort, AckCompletionSource> _pendingAcks
        = new();

    private readonly ConcurrentDictionary<uint, MqttSubscription> _subsById = new();
    private int _nextSubId;

    private TaskCompletionSource<AuthPacket>? _pendingAuth;

    private IMqttTransport? _transport;
    private Task? _readLoop;
    private Task? _writeLoop;
    private Task? _keepAliveLoop;
    private CancellationTokenSource? _loopCts;
    private int _state; // MqttConnectionState
    private int _manualDisconnect;

    /// <summary>
    /// Raised after a successful connect.
    /// </summary>
    public event EventHandler<MqttConnectResult>? Connected;

    /// <summary>
    /// Raised on disconnect (broker- or client-initiated, or transport failure).
    /// </summary>
    public event EventHandler<MqttDisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// Raised on every <see cref="MqttConnectionState"/> transition. Useful for fine-grained UI
    /// or observability hooks; <see cref="Connected"/> and <see cref="Disconnected"/> remain the
    /// recommended events for most callers.
    /// </summary>
    public event EventHandler<MqttConnectionState>? StateChanged;

    private void SetState(MqttConnectionState newState)
    {
        var oldState = (MqttConnectionState)Volatile.Read(ref _state);
        if (oldState == newState) return;
        Volatile.Write(ref _state, (int)newState);
        StateChanged?.Invoke(this, newState);
    }

    public MqttClient(
        MqttClientOptions options,
        ILoggerFactory? loggerFactory = null,
        IPersistentSessionStore? persistence = null)
        : this(options, transportFactory: null, loggerFactory, persistence) { }

    /// <summary>
    /// Internal ctor allowing tests to inject a fake <see cref="IMqttTransportFactory"/>.
    /// </summary>
    internal MqttClient(
        MqttClientOptions options,
        IMqttTransportFactory? transportFactory,
        ILoggerFactory? loggerFactory = null,
        IPersistentSessionStore? persistence = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<MqttClient>();
        _metrics = new MqttMetrics();
        _persistence = persistence ?? new InMemorySessionStore();
        _transportFactory = transportFactory ?? CreateTransportFactory(options);
        _outbound = Channel.CreateBounded<OutboundEnvelope>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public MqttConnectionState State => (MqttConnectionState)Volatile.Read(ref _state);

    /// <summary>
    /// Fluent builder entry point.
    /// </summary>
    public static MqttClientBuilder CreateBuilder() => new();

    private static IMqttTransportFactory CreateTransportFactory(MqttClientOptions o)
    {
        // Pause threshold scales with MaxIncomingPacketSize so a user who raises the cap also
        // gets enough buffer headroom; minimum 1 MiB to avoid pathologically small thresholds.
        var pauseThreshold = Math.Max(1024 * 1024, o.MaxIncomingPacketSize * 2);
        var wsPath = string.IsNullOrEmpty(o.WebSocketPath) ? "/mqtt" : o.WebSocketPath;
        return o.Transport switch
        {
            MqttTransportType.Tcp => new TcpTransportFactory(o.Host, o.Port, pauseThreshold),
            MqttTransportType.Tls => new TlsTransportFactory(
                o.Host,
                o.Port,
                ApplySecureTlsDefaults(o.Tls, o.Host)),
            MqttTransportType.WebSocket => new WebSocketTransportFactory(
                new Uri($"ws://{o.Host}:{(o.Port == 1883 ? 80 : o.Port)}{wsPath}")),
            MqttTransportType.WebSocketSecure => new WebSocketTransportFactory(
                new Uri($"wss://{o.Host}:{(o.Port == 1883 ? 443 : o.Port)}{wsPath}")),
            _ => throw new NotSupportedException($"Transport {o.Transport} is not implemented."),
        };
    }

    /// <summary>
    /// Returns secure defaults for
    /// <see cref="System.Net.Security.SslClientAuthenticationOptions"/>:
    /// TLS 1.2 minimum, CRL checking enabled, default OS chain validation (never disabled
    /// silently). Callers can still override every field via <see cref="MqttClientOptions.Tls"/>.
    /// </summary>
    private static System.Net.Security.SslClientAuthenticationOptions ApplySecureTlsDefaults(
        System.Net.Security.SslClientAuthenticationOptions? src, string host)
    {
        var tls = src ?? new System.Net.Security.SslClientAuthenticationOptions {
            TargetHost = host };
        if (string.IsNullOrEmpty(tls.TargetHost))
        {
            tls.TargetHost = host;
        }
        if (tls.EnabledSslProtocols == System.Security.Authentication.SslProtocols.None)
        {
#pragma warning disable CA5398 // explicit modern minimums; OS picks TLS 1.3 when available
#if NETSTANDARD2_1
            tls.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
#else
            tls.EnabledSslProtocols =
                System.Security.Authentication.SslProtocols.Tls12
                    | System.Security.Authentication.SslProtocols.Tls13;
#endif
#pragma warning restore CA5398
        }
        if (tls.CertificateRevocationCheckMode
            == System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck)
        {
            tls.CertificateRevocationCheckMode
                = System.Security.Cryptography.X509Certificates.X509RevocationMode.Online;
        }
        return tls;
    }

    public async Task<MqttConnectResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(
            ref _state,
            (int)MqttConnectionState.Connecting,
            (int)MqttConnectionState.Disconnected) != (int)MqttConnectionState.Disconnected)
        {
            throw new InvalidOperationException($"Cannot connect while state is {State}.");
        }
        Volatile.Write(ref _manualDisconnect, 0);

        MqttLog.Connecting(
            _logger,
            $"{_options.Host}:{_options.Port}",
            _options.ProtocolVersion,
            _options.ClientId);
        try
        {
            _transport = await _transportFactory.ConnectAsync(cancellationToken)
                .ConfigureAwait(false);
            _loopCts = new CancellationTokenSource();

            // Capture handler-supplied initial AUTH payload before encoding CONNECT.
            _initialAuthMethod = null;
            _initialAuthData = default;
            var handler = _options.ProtocolVersion == MqttProtocolVersion.V500
                ? _options.AuthenticationHandler
                : null;
            if (handler is not null)
            {
                var first = await handler.ContinueAsync(null, cancellationToken)
                    .ConfigureAwait(false);
                if (first.Kind == MqttAuthenticationResultKind.Abort)
                {
                    throw new MqttAuthenticationException(first.ReasonCode, first.ReasonString);
                }
                _initialAuthMethod = handler.Method;
                _initialAuthData = first.Data;
            }

            var connectPkt = BuildConnectPacket();
            var w = new MqttBufferWriter(256);
            try
            {
                MqttPacketEncoder.EncodeConnect(connectPkt, ref w);
                await WriteRawAsync(w.WrittenMemory, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                w.Dispose();
            }
            // Wait for CONNACK or run the AUTH exchange first.
            var connack = await ReadConnAckOrAuthAsync(handler, cancellationToken).ConfigureAwait(
                false);
            if (!connack.IsSuccess)
            {
                await CleanupAsync("CONNACK failure").ConfigureAwait(false);
                return connack;
            }
            SetState(MqttConnectionState.Connected);
            _readLoop = Task.Run(() => ReadLoopAsync(_loopCts!.Token));
            _writeLoop = Task.Run(() => WriteLoopAsync(_loopCts!.Token));
            if (_options.KeepAliveSeconds > 0)
            {
                _keepAliveLoop = Task.Run(() => KeepAliveLoopAsync(_loopCts!.Token));
            }
            MqttLog.Connected(
                _logger,
                _transport?.RemoteAddress ?? string.Empty,
                connack.SessionPresent);
            Connected?.Invoke(this, connack);
            return connack;
        }
        catch
        {
            await CleanupAsync("Connect failed").ConfigureAwait(false);
            throw;
        }
    }

    private ConnectPacket BuildConnectPacket() => new()
    {
        ProtocolVersion = _options.ProtocolVersion,
        ClientId = _options.ClientId,
        CleanStart = _options.CleanStart,
        KeepAliveSeconds = _options.KeepAliveSeconds,
        Username = _options.Username,
        Password = _options.Password,
        Will = _options.Will,
        ReceiveMaximum = _options.ProtocolVersion == MqttProtocolVersion.V500
            ? _options.ReceiveMaximum
            : null,
        TopicAliasMaximum = _options.ProtocolVersion == MqttProtocolVersion.V500
            && _options.TopicAliasMaximum > 0
            ? _options.TopicAliasMaximum
            : null,
        AuthenticationMethod = _initialAuthMethod,
        AuthenticationData = _initialAuthData,
    };

    // Initial auth values captured by ConnectAsync from the handler before encoding CONNECT.
    private string? _initialAuthMethod;
    private ReadOnlyMemory<byte> _initialAuthData;

    private async Task<MqttConnectResult> ReadConnAckOrAuthAsync(
        IMqttAuthenticationHandler? handler,
        CancellationToken ct)
    {
        var reader = _transport!.Input;
        var roundTrip = 0;
        while (!ct.IsCancellationRequested)
        {
            var result = await reader.ReadAsync(ct).ConfigureAwait(false);
            var buffer = result.Buffer;
            if (MqttPacketDecoder.TryDecode(
                buffer,
                _options.ProtocolVersion,
                _options.MaxIncomingPacketSize,
                out var packet,
                out _,
                out var consumed))
            {
                reader.AdvanceTo(consumed);
                if (packet is ConnAckPacket cack)
                {
                    return new MqttConnectResult
                    {
                        ReasonCode = cack.ReasonCode,
                        SessionPresent = cack.SessionPresent,
                        AssignedClientId = cack.AssignedClientId,
                        ServerKeepAlive = cack.ServerKeepAlive,
                        ReceiveMaximum = cack.ReceiveMaximum,
                        MaximumPacketSize = cack.MaximumPacketSize,
                        MaximumQoS = cack.MaximumQoS,
                        RetainAvailable = cack.RetainAvailable,
                        WildcardSubscriptionAvailable = cack.WildcardSubscriptionAvailable,
                        SharedSubscriptionAvailable = cack.SharedSubscriptionAvailable,
                        TopicAliasMaximum = cack.TopicAliasMaximum,
                        ReasonString = cack.ReasonString,
                        AuthenticationData = cack.AuthenticationData,
                    };
                }
                if (packet is AuthPacket auth)
                {
                    if (handler is null)
                    {
                        throw new MqttProtocolException(
                            "Broker sent AUTH but no authentication handler is configured.");
                    }
                    if (auth.ReasonCode != MqttReasonCode.ContinueAuthentication)
                    {
                        throw new MqttAuthenticationException(auth.ReasonCode, auth.ReasonString);
                    }
                    if (++roundTrip > _options.MaxAuthRoundTrips)
                    {
                        await SendDisconnectAsync(MqttReasonCode.ProtocolError, ct).ConfigureAwait(
                            false);
                        throw new MqttAuthenticationException(
                            MqttReasonCode.ProtocolError,
                            $"Exceeded MaxAuthRoundTrips ({_options.MaxAuthRoundTrips}).");
                    }
                    var next = await handler.ContinueAsync(auth.AuthenticationData, ct)
                        .ConfigureAwait(false);
                    if (next.Kind == MqttAuthenticationResultKind.Abort)
                    {
                        await SendDisconnectAsync(next.ReasonCode, ct).ConfigureAwait(false);
                        throw new MqttAuthenticationException(next.ReasonCode, next.ReasonString);
                    }
                    var rc = next.Kind == MqttAuthenticationResultKind.Final
                        ? MqttReasonCode.Success
                        : MqttReasonCode.ContinueAuthentication;
                    await SendAuthAsync(rc, handler.Method, next.Data, ct).ConfigureAwait(false);
                    continue;
                }
                throw new MqttProtocolException(
                    $"Expected CONNACK or AUTH, got {packet?.GetType().Name ?? "null"}.");
            }
            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
            {
                throw new MqttConnectionException("Broker closed connection before CONNACK.");
            }
        }
        throw new OperationCanceledException(ct);
    }

    private async Task SendAuthAsync(
        MqttReasonCode rc,
        string method,
        ReadOnlyMemory<byte> data,
        CancellationToken ct)
    {
        var pkt = new AuthPacket
        {
            ReasonCode = rc,
            AuthenticationMethod = method,
            AuthenticationData = data,
        };
        var w = new MqttBufferWriter(64 + data.Length);
        try
        {
            MqttPacketEncoder.EncodeAuth(pkt, ref w);
            await WriteRawAsync(w.WrittenMemory, ct).ConfigureAwait(false);
        }
        finally
        {
            w.Dispose();
        }
    }

    private async Task SendDisconnectAsync(MqttReasonCode rc, CancellationToken ct)
    {
        try
        {
            var w = new MqttBufferWriter(8);
            try
            {
                MqttPacketEncoder.EncodeDisconnect(
                    new DisconnectPacket { ReasonCode = rc },
                    _options.ProtocolVersion,
                    ref w);
                await WriteRawAsync(w.WrittenMemory, ct).ConfigureAwait(false);
            }
            finally
            {
                w.Dispose();
            }
        }
        catch { /* best effort */ }
    }

    private async Task WriteRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
        => await WriteRawAsync(bytes, ReadOnlySequence<byte>.Empty, ct).ConfigureAwait(false);

    private async Task WriteRawAsync(
        ReadOnlyMemory<byte> headerBytes,
        ReadOnlySequence<byte> payload,
        CancellationToken ct)
    {
        var output = _transport!.Output;
        if (!headerBytes.IsEmpty)
        {
            headerBytes.CopyTo(output.GetMemory(headerBytes.Length));
            output.Advance(headerBytes.Length);
        }
        // Write each segment in slices that fit the pipe's current segment without forcing a copy
        // through an intermediate writer; PipeWriter pools segments internally.
        foreach (var segment in payload)
        {
            var remaining = segment;
            while (!remaining.IsEmpty)
            {
                var dst = output.GetMemory(Math.Min(remaining.Length, 16 * 1024));
                var n = Math.Min(dst.Length, remaining.Length);
                if (n == 0)
                {
                    // Defensive: PipeWriter contract guarantees GetMemory returns >= sizeHint, so
                    // this should never happen; throw rather than spin forever.
                    throw new MqttConnectionException("PipeWriter returned zero-length buffer.");
                }
                remaining.Span.Slice(0, n).CopyTo(dst.Span);
                output.Advance(n);
                remaining = remaining.Slice(n);
            }
        }
        _metrics.BytesSent.Add(headerBytes.Length + payload.Length);
        var result = await output.FlushAsync(ct).ConfigureAwait(false);
        if (result.IsCompleted) throw new MqttConnectionException("Transport closed during write.");
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var env in _outbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await WriteRawAsync(env.Bytes, env.Payload, ct).ConfigureAwait(false);
                    env.OnSent?.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    env.OnSent?.TrySetException(ex);
                    throw;
                }
                finally
                {
                    env.Dispose();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MqttLog.ConnectionLoopFailed(_logger, ex, "writer");
            FaultPending(ex);
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var reader = _transport!.Input;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;
                while (MqttPacketDecoder.TryDecode(
                    buffer,
                    _options.ProtocolVersion,
                    _options.MaxIncomingPacketSize,
                    poolPayload: true,
                    out var packet,
                    out var firstByte,
                    out var consumed))
                {
                    var before = buffer.Length;
                    // Awaited so an inline-handler delivery completes before we advance the pipe
                    // reader — the message payload borrows the receive buffer (true zero-copy).
                    await DispatchInboundAsync(firstByte, packet).ConfigureAwait(false);
                    buffer = buffer.Slice(consumed);
                    // Avoid an O(segments) walk of buffer.Slice(start,end).Length: the size of the
                    // packet we just consumed is the difference in the unread sequence's length.
                    _metrics.BytesReceived.Add(before - buffer.Length);
                }
                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted)
                {
                    break;
                }
            }
            await OnTransportClosedAsync(null).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MqttLog.ConnectionLoopFailed(_logger, ex, "reader");
            await OnTransportClosedAsync(ex).ConfigureAwait(false);
        }
    }

    private ValueTask DispatchInboundAsync(byte firstByte, object? packet)
    {
        var type = (MqttPacketType)(firstByte >> 4);
        switch (packet)
        {
            case PublishPacket pub:
                return HandleInboundPublishAsync(pub);
            case PubAckPacket ack when type == MqttPacketType.PubAck:
                CompletePending(ack.PacketId, ack);
                _packetIds.Release(ack.PacketId);
                break;
            case PubAckPacket rec when type == MqttPacketType.PubRec:
                EnqueuePubRel(rec.PacketId, MqttReasonCode.Success);
                break;
            case PubAckPacket rel when type == MqttPacketType.PubRel:
                EnqueuePubComp(rel.PacketId, MqttReasonCode.Success);
                break;
            case PubAckPacket comp when type == MqttPacketType.PubComp:
                CompletePending(comp.PacketId, comp);
                _packetIds.Release(comp.PacketId);
                break;
            case SubAckPacket sub:
                CompletePending(sub.PacketId, sub);
                _packetIds.Release(sub.PacketId);
                break;
            case UnsubAckPacket usub:
                CompletePending(usub.PacketId, usub);
                _packetIds.Release(usub.PacketId);
                break;
            case DisconnectPacket disc:
                _ = OnTransportClosedAsync(
                    new MqttConnectionException($"Broker DISCONNECT: {disc.ReasonCode}"));
                break;
            case AuthPacket auth:
                var pendingAuth = Volatile.Read(ref _pendingAuth);
                if (pendingAuth is null)
                {
                    _ = OnTransportClosedAsync(
                        new MqttProtocolException("Unsolicited AUTH from broker."));
                }
                else
                {
                    pendingAuth.TrySetResult(auth);
                }
                break;
            case null when type == MqttPacketType.PingResp:
                break;
        }
        return default;
    }

    private void EnqueuePubRel(ushort packetId, MqttReasonCode rc)
    {
        var w = new MqttBufferWriter(8);
        MqttPacketEncoder.EncodePubRel(packetId, rc, _options.ProtocolVersion, ref w);
        _ = EnqueueAsync(NewEnvelopeFrom(w));
    }

    private void EnqueuePubComp(ushort packetId, MqttReasonCode rc)
    {
        var w = new MqttBufferWriter(8);
        MqttPacketEncoder.EncodePubComp(packetId, rc, _options.ProtocolVersion, ref w);
        _ = EnqueueAsync(NewEnvelopeFrom(w));
    }

    private void CompletePending(ushort packetId, object value)
    {
        if (_pendingAcks.TryRemove(packetId, out var waiter)) waiter.TrySetResult(value);
    }

    private void FaultPending(Exception ex)
    {
        foreach (var kv in _pendingAcks)
        {
            kv.Value.TrySetException(ex);
        }
        _pendingAcks.Clear();
    }

    /// <summary>Inbound MQTT 5 topic-alias table (alias → topic). Single-writer (read loop) safe.</summary>
    private readonly Dictionary<ushort, string> _inboundAliases = new();

    // Reused across read-loop iterations (single-threaded) to collect matching subscriptions
    // without allocating a list per inbound message.
    private readonly List<MqttSubscription> _matchBuffer = new();
    private Action<MqttSubscription>? _collectMatch;

    private async ValueTask HandleInboundPublishAsync(PublishPacket pub)
    {
        _metrics.Receives.Add(1);

        // MQTT 5 inbound topic-alias expansion: an alias with a non-empty topic registers
        // (alias -> topic); an empty topic looks up the previously-registered alias.
        var topic = pub.Topic;
        if (_options.ProtocolVersion == MqttProtocolVersion.V500
            && pub.Properties?.TopicAlias is { } alias
            && alias > 0)
        {
            if (string.IsNullOrEmpty(topic))
            {
                if (!_inboundAliases.TryGetValue(alias, out var resolved))
                {
                    // Spec violation: broker sent an alias we never registered. Disconnect.
                    _ = OnTransportClosedAsync(
                        new MqttProtocolException(
                            $"Inbound topic alias {alias} is not registered."));
                    return;
                }
                topic = resolved;
            }
            else if (alias <= _options.TopicAliasMaximum)
            {
                _inboundAliases[alias] = topic;
            }
            else
            {
                _ = OnTransportClosedAsync(new MqttProtocolException(
                    $"Inbound topic alias {alias} exceeds advertised TopicAliasMaximum " +
                    $"({_options.TopicAliasMaximum})."));
                return;
            }
        }

        // QoS1: send PUBACK after handing to subscribers.
        if (pub.QoS == MqttQoS.AtLeastOnce)
        {
            EnqueuePubAck(pub.PacketId, MqttReasonCode.Success);
        }
        else if (pub.QoS == MqttQoS.ExactlyOnce)
        {
            // Simplified: ack with PUBREC; full QoS2 state machine is not yet implemented.
            EnqueuePubRec(pub.PacketId);
        }

        // Collect matching subscriptions into the reusable buffer (no per-message list alloc).
        _matchBuffer.Clear();
        var sids = pub.Properties?.SubscriptionIdentifiers;
        if (sids is { Count: > 0 })
        {
            // MQTT 5 fast-path: the broker echoed our SubscriptionIdentifier(s) — skip the trie.
            foreach (var id in sids)
            {
                if (_subsById.TryGetValue(id, out var s)) _matchBuffer.Add(s);
            }
        }
        else
        {
            _collectMatch ??= s => _matchBuffer.Add(s);
            _trie.Match(topic, _collectMatch);
        }

        if (_matchBuffer.Count == 0) return;

        // pub.Payload is a borrowed slice of the receive buffer (valid for this awaited dispatch).
        // Channel delivery copies it out (pooled by default, or GC-owned when retainable); a single
        // GC-owned copy is shared across all channel subscribers.
        MqttMessage? sharedGcOwned = null;
        foreach (var sub in _matchBuffer)
        {
            if (sub.IsInline)
            {
                // True zero-copy: hand the borrowed-slice message to the handler and await it.
                var inlineMsg = new MqttMessage
                {
                    Topic = topic,
                    Payload = pub.Payload,
                    QoS = pub.QoS,
                    Retain = pub.Retain,
                    Duplicate = pub.Duplicate,
                    Properties = pub.Properties,
                };
                try
                {
                    await sub.Handler!(inlineMsg).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    MqttLog.ConnectionLoopFailed(_logger, ex, "inline subscription handler");
                }
                continue;
            }

            MqttMessage channelMsg;
            if (_options.RetainableInboundMessages)
            {
                channelMsg = sharedGcOwned ??= BuildGcOwnedMessage(topic, pub);
            }
            else
            {
                channelMsg = BuildPooledMessage(topic, pub);
            }

            // Back-pressure flows naturally: when Wait mode is selected and full, this blocks the
            // read loop, which in turn applies TCP back-pressure.
            if (!sub.Writer!.TryWrite(channelMsg))
            {
                await sub.Writer.WriteAsync(channelMsg).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Builds a GC-owned message with a freshly allocated payload copy (and, if present, a copy of
    /// the v5 CorrelationData). Used when <see cref="MqttClientOptions.RetainableInboundMessages"/>
    /// is set; a single instance is shared across all channel subscribers for one PUBLISH.
    /// </summary>
    private static MqttMessage BuildGcOwnedMessage(string topic, PublishPacket pub)
    {
        var len = (int)pub.Payload.Length;
        var copy = new byte[len];
        pub.Payload.CopyTo(copy);
        return new MqttMessage
        {
            Topic = topic,
            Payload = new ReadOnlySequence<byte>(copy),
            QoS = pub.QoS,
            Retain = pub.Retain,
            Duplicate = pub.Duplicate,
            Properties = CopyBorrowedProperties(pub.Properties),
        };
    }

    /// <summary>
    /// Copies a borrowed inbound payload into a single freshly rented pooled buffer (payload bytes
    /// followed by any v5 CorrelationData) and wraps it in an owning <see cref="MqttMessage"/>. Each
    /// matching subscription gets its own copy, so there is no shared pooled buffer (no double-return).
    /// </summary>
    private static MqttMessage BuildPooledMessage(string topic, PublishPacket pub)
    {
        var payloadLen = (int)pub.Payload.Length;
        var corr = pub.Properties?.CorrelationData ?? default;
        var corrLen = corr.Length;
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(1, payloadLen + corrLen));
        pub.Payload.CopyTo(rented.AsSpan(0, payloadLen));

        var props = pub.Properties;
        if (corrLen > 0)
        {
            corr.Span.CopyTo(rented.AsSpan(payloadLen, corrLen));
            // CorrelationData now lives inside the message's pooled buffer (same lifetime).
            props = props!.WithCorrelationData(
                new ReadOnlyMemory<byte>(rented, payloadLen, corrLen));
        }

        return new MqttMessage
        {
            Topic = topic,
            Payload = new ReadOnlySequence<byte>(rented, 0, payloadLen),
            PooledArray = rented,
            QoS = pub.QoS,
            Retain = pub.Retain,
            Duplicate = pub.Duplicate,
            Properties = props,
        };
    }

    /// <summary>Materializes any borrowed CorrelationData into a GC-owned copy for retained messages.</summary>
    private static MqttPublishProperties? CopyBorrowedProperties(MqttPublishProperties? props)
    {
        if (props?.CorrelationData is not { Length: > 0 } corr) return props;
        return props.WithCorrelationData(corr.ToArray());
    }

    private void EnqueuePubAck(ushort packetId, MqttReasonCode rc)
    {
        var w = new MqttBufferWriter(8);
        MqttPacketEncoder.EncodePubAck(packetId, rc, _options.ProtocolVersion, ref w);
        _ = EnqueueAsync(NewEnvelopeFrom(w));
    }

    private void EnqueuePubRec(ushort packetId)
    {
        var w = new MqttBufferWriter(8);
        MqttPacketEncoder.EncodePubRec(
            packetId,
            MqttReasonCode.Success,
            _options.ProtocolVersion,
            ref w);
        _ = EnqueueAsync(NewEnvelopeFrom(w));
    }

    private async ValueTask EnqueueAsync(OutboundEnvelope env, CancellationToken ct = default)
    {
        try { await _outbound.Writer.WriteAsync(env, ct).ConfigureAwait(false); }
        catch (ChannelClosedException) { env
            .Dispose(); throw new MqttConnectionException("Client is not connected."); }
    }

    private async Task KeepAliveLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.KeepAliveSeconds * 0.8);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(interval, ct).ConfigureAwait(false); } catch { break; }
            try
            {
                var w = new MqttBufferWriter(2);
                MqttPacketEncoder.EncodePingReq(ref w);
                await EnqueueAsync(NewEnvelopeFrom(w), ct).ConfigureAwait(false);
            }
            catch { break; }
        }
    }

    private async Task OnTransportClosedAsync(Exception? exception)
    {
        if (Volatile.Read(ref _state) >= (int)MqttConnectionState.Disposed) return;
        var wasManual = Volatile.Read(ref _manualDisconnect) == 1;
        SetState(MqttConnectionState.Disconnected);
        var reason = exception?.Message ?? "Transport closed";
        MqttLog.Disconnected(_logger, _transport?.RemoteAddress, reason);
        Disconnected?.Invoke(this, new MqttDisconnectedEventArgs(reason, exception));
        FaultPending(
            new MqttConnectionException(
                reason, exception ?? new InvalidOperationException(reason)));
        await CleanupAsync(reason).ConfigureAwait(false);

        if (!wasManual && _options.Reconnect is { } policy)
        {
            _ = ReconnectLoopAsync(policy);
        }
    }

    private async Task ReconnectLoopAsync(MqttReconnectPolicy policy)
    {
        var delay = policy.InitialDelay;
        var attempt = 0;
        var rng = new Random();
        while (Volatile.Read(ref _state) == (int)MqttConnectionState.Disconnected &&
               Volatile.Read(ref _manualDisconnect) == 0)
        {
            attempt++;
            var jitter = 1 + (((rng.NextDouble() * 2) - 1) * policy.JitterFactor);
            var actual = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * jitter);
            MqttLog.Reconnecting(_logger, attempt, actual.TotalMilliseconds);
            try { await Task.Delay(actual).ConfigureAwait(false); } catch { return; }
            try
            {
                SetState(MqttConnectionState.Reconnecting);
                // ConnectAsync expects Disconnected → CAS to Connecting; reset state first.
                SetState(MqttConnectionState.Disconnected);
                var result = await ConnectAsync().ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    _metrics.Reconnects.Add(1);
                    return;
                }
            }
            catch (Exception ex)
            {
                MqttLog.ConnectionLoopFailed(_logger, ex, "reconnect attempt failed");
            }
            delay = TimeSpan.FromMilliseconds(Math.Min(
                delay.TotalMilliseconds * policy.BackoffFactor,
                policy.MaxDelay.TotalMilliseconds));
        }
    }

    private async Task CleanupAsync(string reason)
    {
        _ = reason;
        _loopCts?.Cancel();
        // Note: outbound channel is intentionally NOT completed here so queued publishes survive
        // a reconnect. The channel is completed in DisposeAsync.
        if (_transport is not null)
        {
            try { await _transport.DisposeAsync().ConfigureAwait(false); } catch { }
            _transport = null;
        }
    }

    /// <summary>
    /// Publishes a message at the requested QoS. For QoS&gt;0, awaits the ack.
    /// </summary>
    public ValueTask<MqttPublishResult> PublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        MqttQoS qos = MqttQoS.AtMostOnce,
        bool retain = false,
        MqttPublishProperties? properties = null,
        CancellationToken cancellationToken = default)
        => PublishAsync(
            topic, new ReadOnlySequence<byte>(payload), qos, retain, properties, cancellationToken);

    /// <summary>
    /// Publishes a message whose payload may span multiple buffer segments (e.g. pre-chunked or
    /// pipelined data) without first concatenating it. For QoS&gt;0, awaits the ack.
    /// </summary>
    public async ValueTask<MqttPublishResult> PublishAsync(
        string topic,
        ReadOnlySequence<byte> payload,
        MqttQoS qos = MqttQoS.AtMostOnce,
        bool retain = false,
        MqttPublishProperties? properties = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        MqttLog.Publishing(_logger, topic, qos, (int)payload.Length);
        _metrics.Publishes.Add(1);

        ushort packetId = qos == MqttQoS.AtMostOnce ? (ushort)0 : _packetIds.Allocate();
        var packet = new PublishPacket
        {
            Topic = topic,
            QoS = qos,
            Retain = retain,
            PacketId = packetId,
            Payload = payload,
            Properties = properties,
        };

        // Encode header only into a small rented buffer; the payload is sent as a separate
        // pipe write to avoid a large memcpy through MqttBufferWriter for big payloads.
        var w = new MqttBufferWriter(128);
        MqttPacketEncoder.EncodePublishHeader(packet, _options.ProtocolVersion, ref w);

        if (qos == MqttQoS.AtMostOnce)
        {
            await EnqueueAsync(NewEnvelopeFrom(w, payload), cancellationToken).ConfigureAwait(
                false);
            return new MqttPublishResult(MqttReasonCode.Success);
        }

        var waiter = AckCompletionSource.Rent(cancellationToken);
        _pendingAcks[packetId] = waiter;
        var startTs = Stopwatch.GetTimestamp();
        await EnqueueAsync(NewEnvelopeFrom(w, payload), cancellationToken).ConfigureAwait(false);
        var ack = (PubAckPacket?)await waiter.ValueTask.ConfigureAwait(false);
        var elapsedMs = (Stopwatch.GetTimestamp() - startTs) * 1000.0 / Stopwatch.Frequency;
        _metrics.PublishAckLatency.Record(elapsedMs);
        return new MqttPublishResult(ack?.ReasonCode ?? MqttReasonCode.Success, ack?.ReasonString);
    }

    /// <summary>
    /// Attempts to enqueue a QoS 0 publish without awaiting transmission. Returns false when the
    /// queue is full.
    /// </summary>
    public bool TryPublish(string topic, ReadOnlySpan<byte> payload, bool retain = false)
    {
        EnsureConnected();
        // For TryPublish we materialise the payload to a pooled byte[] so the envelope can
        // outlive the calling stack frame; the caller's span is not retained.
        var pooled = ArrayPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(pooled);
        var packet = new PublishPacket
        {
            Topic = topic,
            QoS = MqttQoS.AtMostOnce,
            Retain = retain,
            PacketId = 0,
            Payload = new ReadOnlySequence<byte>(pooled, 0, payload.Length),
        };
        var w = new MqttBufferWriter(128);
        MqttPacketEncoder.EncodePublishHeader(packet, _options.ProtocolVersion, ref w);
        var envelope = NewEnvelopeFrom(w, packet.Payload, pooled, _options.ClearPooledBuffers);
        if (_outbound.Writer.TryWrite(envelope))
        {
            _metrics.Publishes.Add(1);
            return true;
        }
        envelope.Dispose();
        return false;
    }

    /// <summary>
    /// Subscribes and returns a channel-backed <see cref="MqttSubscription"/> whose
    /// <see cref="MqttSubscription.Reader"/> yields inbound messages.
    /// </summary>
    public ValueTask<MqttSubscription> SubscribeAsync(
        string topicFilter,
        MqttSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
        => SubscribeCoreAsync(topicFilter, handler: null, options, cancellationToken);

    /// <summary>
    /// Subscribes with an inline handler invoked on the receive loop as each matching message
    /// arrives. The handler's <see cref="MqttMessage"/> payload is a true zero-copy slice of the
    /// receive buffer and is valid only for the duration of the returned <see cref="ValueTask"/> —
    /// do not retain it. Back-pressure flows to the broker while the handler runs.
    /// </summary>
    public ValueTask<MqttSubscription> SubscribeAsync(
        string topicFilter,
        Func<MqttMessage, ValueTask> handler,
        MqttSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        return SubscribeCoreAsync(topicFilter, handler, options, cancellationToken);
    }

    private async ValueTask<MqttSubscription> SubscribeCoreAsync(
        string topicFilter,
        Func<MqttMessage, ValueTask>? handler,
        MqttSubscriptionOptions? options,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        options ??= new MqttSubscriptionOptions { Capacity = _options.DefaultSubscriptionCapacity };
        MqttLog.Subscribing(_logger, topicFilter, options.QoS);

        // MQTT 5 shared subscriptions ($share/group/<filter>): strip the prefix when registering
        // in the local trie so inbound publishes match against the underlying topic filter, but
        // keep the original filter on the wire so the broker performs the shared-distribution.
        var matchFilter = StripSharedSubscriptionPrefix(topicFilter);

        var sub = new MqttSubscription(topicFilter, options, async s =>
        {
            var mf = StripSharedSubscriptionPrefix(s.TopicFilter);
            lock (_subLock) { _trie.Remove(mf, s); }
            if (s.Identifier is { } id) _subsById.TryRemove(id, out _);
            using var cts = new CancellationTokenSource(_options.OperationTimeout);
            try { await UnsubscribeOnServerAsync(s.TopicFilter, cts.Token).ConfigureAwait(
                false); } catch { }
        }, handler);
        lock (_subLock) { _trie.Add(matchFilter, sub); }

        uint? subId = null;
        if (_options.ProtocolVersion == MqttProtocolVersion.V500)
        {
            subId = (uint)Interlocked.Increment(ref _nextSubId);
            sub.Identifier = subId;
            _subsById[subId.Value] = sub;
        }

        var packetId = _packetIds.Allocate();
        var sp = new SubscribePacket
        {
            PacketId = packetId,
            Filters = new[] { new SubscribeFilter(
                topicFilter,
                options.QoS,
                options.NoLocal,
                options.RetainAsPublished) },
            SubscriptionIdentifier = subId,
        };
        var w = new MqttBufferWriter(64);
        MqttPacketEncoder.EncodeSubscribe(sp, _options.ProtocolVersion, ref w);
        var waiter = AckCompletionSource.Rent(cancellationToken);
        _pendingAcks[packetId] = waiter;
        await EnqueueAsync(NewEnvelopeFrom(w), cancellationToken).ConfigureAwait(false);
        await waiter.ValueTask.ConfigureAwait(false);
        return sub;
    }

    private async ValueTask UnsubscribeOnServerAsync(string topicFilter, CancellationToken ct)
    {
        if (State != MqttConnectionState.Connected) return;
        var packetId = _packetIds.Allocate();
        var u = new UnsubscribePacket { PacketId = packetId, Topics = new[] { topicFilter } };
        var w = new MqttBufferWriter(32);
        MqttPacketEncoder.EncodeUnsubscribe(u, _options.ProtocolVersion, ref w);
        // Bounded wait so a broker that never acks UNSUBSCRIBE doesn't hang the disposing
        // subscription. Local trie removal already happened; the server-side ack is best-effort.
        // Honor both the caller's ct and a sane timeout, *and* the client-shutdown signal so
        // disposing the client unblocks any pending unsubscribe immediately.
        using var timeoutCts = new CancellationTokenSource(_options.OperationTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            timeoutCts.Token,
            _loopCts?.Token ?? CancellationToken.None);
        var waiter = AckCompletionSource.Rent(linkedCts.Token);
        _pendingAcks[packetId] = waiter;
        await EnqueueAsync(NewEnvelopeFrom(w), ct).ConfigureAwait(false);
        try
        {
            await waiter.ValueTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Timed out or client shutdown — best-effort, give up the server-side ack.
            if (_pendingAcks.TryRemove(packetId, out _))
            {
                _packetIds.Release(packetId);
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _manualDisconnect, 1);
        if (State != MqttConnectionState.Connected) return;
        var w = new MqttBufferWriter(8);
        MqttPacketEncoder.EncodeDisconnect(
            new DisconnectPacket { ReasonCode = MqttReasonCode.Success },
            _options.ProtocolVersion,
            ref w);
        try { await EnqueueAsync(NewEnvelopeFrom(w), cancellationToken).ConfigureAwait(
            false); } catch { }
        await CleanupAsync("client disconnect").ConfigureAwait(false);
        SetState(MqttConnectionState.Disconnected);
    }

    /// <summary>
    /// MQTT 5 re-authentication on a live connection. Drives the same SASL-style exchange via
    /// <paramref name="handler"/> (or <see cref="MqttClientOptions.AuthenticationHandler"/> when
    /// null). Single-in-flight: a concurrent call throws <see cref="InvalidOperationException"/>.
    /// Returns the broker's final authentication data (if any).
    /// </summary>
    public async Task<ReadOnlyMemory<byte>?> ReauthenticateAsync(
        IMqttAuthenticationHandler? handler = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (_options.ProtocolVersion != MqttProtocolVersion.V500)
        {
            throw new InvalidOperationException("Re-authentication requires MQTT 5.");
        }
        handler ??= _options.AuthenticationHandler
            ?? throw new InvalidOperationException("No authentication handler configured.");

        var tcs = new TaskCompletionSource<AuthPacket>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _pendingAuth, tcs, null) != null)
        {
            throw new InvalidOperationException(
                "A re-authentication exchange is already in progress.");
        }
        try
        {
            var first = await handler.ContinueAsync(null, cancellationToken).ConfigureAwait(false);
            if (first.Kind == MqttAuthenticationResultKind.Abort)
            {
                await SendDisconnectAsync(first.ReasonCode, cancellationToken).ConfigureAwait(
                    false);
                throw new MqttAuthenticationException(first.ReasonCode, first.ReasonString);
            }
            await SendAuthAsync(
                MqttReasonCode.ReAuthenticate,
                handler.Method,
                first.Data,
                cancellationToken).ConfigureAwait(false);

            var roundTrip = 0;
            while (true)
            {
                using var reg = cancellationToken.Register(
                    static s => ((TaskCompletionSource<AuthPacket>)s!).TrySetCanceled(),
                    tcs);
                var inbound = await tcs.Task.ConfigureAwait(false);
                // Reset the TCS for the next inbound round-trip (if any).
                tcs = new TaskCompletionSource<AuthPacket>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _pendingAuth, tcs);

                if (inbound.ReasonCode == MqttReasonCode.Success)
                {
                    return inbound.AuthenticationData;
                }
                if (inbound.ReasonCode != MqttReasonCode.ContinueAuthentication)
                {
                    throw new MqttAuthenticationException(inbound.ReasonCode, inbound.ReasonString);
                }
                if (++roundTrip > _options.MaxAuthRoundTrips)
                {
                    await SendDisconnectAsync(MqttReasonCode.ProtocolError, cancellationToken)
                        .ConfigureAwait(false);
                    throw new MqttAuthenticationException(
                        MqttReasonCode.ProtocolError,
                        $"Exceeded MaxAuthRoundTrips ({_options.MaxAuthRoundTrips}).");
                }
                var next = await handler.ContinueAsync(
                    inbound.AuthenticationData,
                    cancellationToken).ConfigureAwait(false);
                if (next.Kind == MqttAuthenticationResultKind.Abort)
                {
                    await SendDisconnectAsync(next.ReasonCode, cancellationToken).ConfigureAwait(
                        false);
                    throw new MqttAuthenticationException(next.ReasonCode, next.ReasonString);
                }
                var rc = next.Kind == MqttAuthenticationResultKind.Final
                    ? MqttReasonCode.Success
                    : MqttReasonCode.ContinueAuthentication;
                await SendAuthAsync(rc, handler.Method, next.Data, cancellationToken)
                    .ConfigureAwait(false);
                if (next.Kind == MqttAuthenticationResultKind.Final)
                {
                    // We sent AUTH 0x00; broker may or may not respond — return on Final.
                    return null;
                }
            }
        }
        finally
        {
            Volatile.Write(ref _pendingAuth, null);
        }
    }

    private void EnsureConnected()
    {
        if (State != MqttConnectionState.Connected)
        {
            throw new InvalidOperationException($"Client is not connected (state={State}).");
        }
    }

    /// <summary>
    /// Strips the <c>$share/&lt;group&gt;/</c> prefix from a shared-subscription filter, returning
    /// the underlying topic filter used for local routing. Non-shared filters pass through.
    /// </summary>
    internal static string StripSharedSubscriptionPrefix(string topicFilter)
    {
        if (topicFilter is null || topicFilter.Length < 8) return topicFilter!;
        if (!topicFilter.StartsWith("$share/", StringComparison.Ordinal)) return topicFilter;
        var slash = topicFilter.IndexOf('/', 7);
        if (slash < 0 || slash == topicFilter.Length - 1) return topicFilter;
        return topicFilter.Substring(slash + 1);
    }

    public async ValueTask DisposeAsync()
    {
        SetState(MqttConnectionState.Disposed);
        Volatile.Write(ref _manualDisconnect, 1);
        _outbound.Writer.TryComplete();
        await CleanupAsync("dispose").ConfigureAwait(false);
        _loopCts?.Dispose();
        _metrics.Dispose();
    }

    /// <summary>
    /// An outbound packet pending serialization. Takes ownership of the encoder's rented header
    /// buffer (detached from the <see cref="MqttBufferWriter"/> struct) plus an optional pooled
    /// payload buffer; both go back to <c>ArrayPool&lt;byte&gt;.Shared</c> on Dispose.
    /// </summary>
    private static OutboundEnvelope NewEnvelopeFrom(MqttBufferWriter writer)
        => new(writer);

    private static OutboundEnvelope NewEnvelopeFrom(
        MqttBufferWriter writer,
        ReadOnlySequence<byte> payload,
        byte[]? pooledPayload = null,
        bool clearOnReturn = false)
        => new(writer, payload, pooledPayload, clearOnReturn);

    private readonly struct OutboundEnvelope : IDisposable
    {
        private readonly byte[] _headerBuffer;
        public OutboundEnvelope(MqttBufferWriter writer)
        {
            _headerBuffer = writer.DetachBuffer(out var len);
            Bytes = new ReadOnlyMemory<byte>(_headerBuffer, 0, len);
            Payload = ReadOnlySequence<byte>.Empty;
            PooledPayload = null;
            ClearOnReturn = false;
            OnSent = null;
        }
        public OutboundEnvelope(
            MqttBufferWriter writer,
            ReadOnlySequence<byte> payload,
            byte[]? pooledPayload = null,
            bool clearOnReturn = false,
            TaskCompletionSource<object?>? onSent = null)
        {
            _headerBuffer = writer.DetachBuffer(out var len);
            Bytes = new ReadOnlyMemory<byte>(_headerBuffer, 0, len);
            Payload = payload;
            PooledPayload = pooledPayload;
            ClearOnReturn = clearOnReturn;
            OnSent = onSent;
        }
        public ReadOnlyMemory<byte> Bytes { get; }
        public ReadOnlySequence<byte> Payload { get; }
        public byte[]? PooledPayload { get; }
        public bool ClearOnReturn { get; }
        public TaskCompletionSource<object?>? OnSent { get; }
        public void Dispose()
        {
            if (_headerBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_headerBuffer);
            }
            if (PooledPayload is not null)
            {
                ArrayPool<byte>.Shared
                    .Return(PooledPayload, clearArray: ClearOnReturn);
            }
        }
    }
}

public sealed class MqttDisconnectedEventArgs : EventArgs
{
    public MqttDisconnectedEventArgs(string reason, Exception? exception)
    {
        Reason = reason;
        Exception = exception;
    }
    public string Reason { get; }
    public Exception? Exception { get; }
}
