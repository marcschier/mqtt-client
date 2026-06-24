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
    // Holds an injected store, or null. The default in-memory store is created on demand (no hot
    // path consumes it today), so a fresh client doesn't pay for one it may never use.
    private readonly IPersistentSessionStore? _persistence;
    // Optional inbound QoS 2 receipt persistence (a store that also implements the companion
    // interface), so exactly-once de-dup survives a Session-Present reconnect.
    private readonly IPersistentInboundQoS2Store? _qos2Store;

    private readonly Channel<OutboundEnvelope> _outbound;
    private readonly TopicFilterTrie<MqttSubscription> _trie = new();
    private readonly object _subLock = new();
    private readonly PacketIdAllocator _packetIds = new();
    private readonly ConcurrentDictionary<ushort, AckCompletionSource> _pendingAcks
        = new();

    private readonly ConcurrentDictionary<uint, MqttSubscription> _subsById = new();
    private int _nextSubId;

    private TaskCompletionSource<AuthPacket>? _pendingAuth;

    // Broker-advertised limits captured from CONNACK on each (re)connect. Defaults are the MQTT 5
    // spec defaults applied when the broker omits the property: Receive Maximum 65535, Maximum QoS
    // 2, retain available, no packet-size limit, no inbound topic alias.
    private int _serverReceiveMaximum = ushort.MaxValue;
    private uint? _serverMaximumPacketSize;
    private MqttQoS _serverMaximumQoS = MqttQoS.ExactlyOnce;
    private bool _serverRetainAvailable = true;
    private ushort _serverTopicAliasMaximum;

    // Bounds outbound in-flight QoS>0 publishes to the broker's advertised Receive Maximum.
    // Recreated from _serverReceiveMaximum on each connect; null means unbounded (broker allows the
    // 65535 maximum, so no practical limit). A publish acquires a slot before sending and releases
    // it when its terminal ack (PUBACK / PUBCOMP) completes the awaited operation.
    private SemaphoreSlim? _inflightQuota;

    private IMqttTransport? _transport;
    private Task? _readLoop;
    private Task? _writeLoop;
    private Task? _keepAliveLoop;
    private CancellationTokenSource? _loopCts;
    private int _state; // MqttConnectionState
    private int _manualDisconnect;
    // Serializes ReconnectAsync so overlapping credential-change signals don't stack reconnects.
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);

    // After sending DISCONNECT, wait up to this long for the broker to close the TCP connection
    // (making it the active closer) before forcing the close ourselves. Keeps our ephemeral port
    // out of TIME_WAIT so rapid connect/disconnect cycles don't exhaust the local port range.
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(2);

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
        _persistence = persistence;
        _qos2Store = persistence as IPersistentInboundQoS2Store;
        _transportFactory = transportFactory ?? CreateTransportFactory(options);
        _outbound = Channel.CreateBounded<OutboundEnvelope>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        // Reconnect to present freshly-loaded credentials when an observable provider signals a
        // change (e.g. a rotated Kubernetes service-account token).
        if (_options.CredentialsProvider is IMqttCredentialsChangeNotifier notifier)
        {
            notifier.CredentialsChanged += OnCredentialsChanged;
        }
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
        var connector = CreateSocketConnector(o);
        return o.Transport switch
        {
            MqttTransportType.Tcp => new TcpTransportFactory(
                o.Host, o.Port, pauseThreshold, connector),
            MqttTransportType.Tls => new TlsTransportFactory(
                o.Host,
                o.Port,
                ApplySecureTlsDefaults(o.Tls, o.Host),
                connector),
            MqttTransportType.WebSocket => o.Proxy is null
                ? new WebSocketTransportFactory(
                    new Uri($"ws://{o.Host}:{(o.Port == 1883 ? 80 : o.Port)}{wsPath}"))
                : throw new NotSupportedException(
                    "A SOCKS5 proxy is not supported for WebSocket transports."),
            MqttTransportType.WebSocketSecure => o.Proxy is null
                ? new WebSocketTransportFactory(
                    new Uri($"wss://{o.Host}:{(o.Port == 1883 ? 443 : o.Port)}{wsPath}"))
                : throw new NotSupportedException(
                    "A SOCKS5 proxy is not supported for WebSocket transports."),
            _ => throw new NotSupportedException($"Transport {o.Transport} is not implemented."),
        };
    }

    private static ISocketConnector CreateSocketConnector(MqttClientOptions o)
        => o.Proxy is null
            ? DefaultConnector.Instance
            : new Socks5SocketConnector(o.Proxy);

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

            // Resolve the username/password to present in CONNECT. A configured provider is
            // consulted on every connect (initial and each reconnect via ReconnectLoopAsync), so
            // freshly-loaded credentials are used each time; otherwise the static options apply.
            _resolvedUsername = _options.Username;
            _resolvedPassword = _options.Password;
            if (_options.CredentialsProvider is { } credentialsProvider)
            {
                var credentials = await credentialsProvider
                    .GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
                _resolvedUsername = credentials.Username;
                _resolvedPassword = credentials.Password;
            }

            var connectPkt = BuildConnectPacket();
            var pw = new PipeBufferWriter(_transport!.Output, 256);
            MqttPacketEncoder.EncodeConnect(connectPkt, ref pw);
            var written = pw.WrittenCount;
            pw.Commit();
            await FlushOutputAsync(written, cancellationToken).ConfigureAwait(false);
            // Wait for CONNACK or run the AUTH exchange first.
            var connack = await ReadConnAckOrAuthAsync(handler, cancellationToken).ConfigureAwait(
                false);
            if (!connack.IsSuccess)
            {
                await CleanupAsync("CONNACK failure").ConfigureAwait(false);
                return connack;
            }
            CaptureServerLimits(connack);
            // Reset per-connection inbound QoS 2 receipt state before the read loop starts (runs
            // single-threaded here). On a Session-Present reconnect, restore the recorded ids from
            // the persistent store so a redelivered inbound PUBLISH is still de-duplicated.
            _inboundQoS2.Clear();
            if (_qos2Store is not null && connack.SessionPresent)
            {
                foreach (var id in await _qos2Store.ListReceivedQoS2Async().ConfigureAwait(false))
                {
                    _inboundQoS2.Add(id);
                }
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
            if (_persistence is not null)
            {
                if (connack.SessionPresent)
                {
                    // Broker kept the session: resend in-flight publishes with DUP set.
                    await RedeliverPersistedAsync().ConfigureAwait(false);
                }
                else
                {
                    // Clean session: prior in-flight publishes are gone — abandon and clear.
                    DiscardPersistedPublishes();
                    await _persistence.ClearAsync().ConfigureAwait(false);
                }
            }
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
        Username = _resolvedUsername,
        Password = _resolvedPassword,
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

    // Username/password resolved by ConnectAsync (from CredentialsProvider when set, else the
    // static options) and consumed by BuildConnectPacket. Re-resolved on every (re)connect.
    private string? _resolvedUsername;
    private byte[]? _resolvedPassword;

    // Capture the broker's effective limits from CONNACK. Called on every successful (re)connect
    // before the client is marked Connected, so the publish path always sees current values.
    private void CaptureServerLimits(MqttConnectResult connack)
    {
        _serverReceiveMaximum = connack.ReceiveMaximum ?? ushort.MaxValue;
        _serverMaximumPacketSize = connack.MaximumPacketSize;
        _serverMaximumQoS = connack.MaximumQoS;
        _serverRetainAvailable = connack.RetainAvailable;
        _serverTopicAliasMaximum = connack.TopicAliasMaximum ?? 0;
        // Size the outbound in-flight quota to the broker's Receive Maximum. The 65535 maximum
        // imposes no practical bound, so leave it null to keep the publish hot path allocation-free.
        // Created once and reused across reconnects: a publish parked across a reconnect (with
        // persistence) keeps its slot on the same semaphore, so swapping/disposing it here would
        // make its later Release throw. A differing Receive Maximum on a later reconnect is ignored.
        if (_inflightQuota is null && _serverReceiveMaximum < ushort.MaxValue)
        {
            _inflightQuota = new SemaphoreSlim(_serverReceiveMaximum, _serverReceiveMaximum);
        }
    }

    // Enforces the broker's advertised CONNACK limits on an outbound PUBLISH. In Reject mode a
    // violation throws at the call site; in Adapt mode the QoS is downgraded and an unavailable
    // retain flag or over-limit topic alias is dropped. Maximum Packet Size always throws — an
    // oversized packet cannot be adapted.
    private (MqttQoS qos, bool retain, MqttPublishProperties? props) ApplyBrokerLimits(
        string topic, MqttQoS qos, bool retain, long payloadLength, MqttPublishProperties? props)
    {
        var adapt = _options.BrokerLimitBehavior == MqttBrokerLimitBehavior.Adapt;

        if (qos > _serverMaximumQoS)
        {
            if (!adapt)
            {
                throw new MqttProtocolException(
                    $"QoS {(int)qos} exceeds the broker's Maximum QoS ({(int)_serverMaximumQoS}).");
            }
            MqttLog.BrokerLimitAdapted(
                _logger, "MaximumQoS", $"{(int)qos}->{(int)_serverMaximumQoS}");
            qos = _serverMaximumQoS;
        }

        if (retain && !_serverRetainAvailable)
        {
            if (!adapt)
            {
                throw new MqttProtocolException(
                    "Broker does not support retained messages (Retain Available = 0).");
            }
            MqttLog.BrokerLimitAdapted(_logger, "RetainAvailable", "retain dropped");
            retain = false;
        }

        if (props?.TopicAlias is { } alias && alias > _serverTopicAliasMaximum)
        {
            if (!adapt)
            {
                throw new MqttProtocolException(
                    $"Topic alias {alias} exceeds the broker's Topic Alias Maximum " +
                    $"({_serverTopicAliasMaximum}).");
            }
            MqttLog.BrokerLimitAdapted(_logger, "TopicAliasMaximum", "alias dropped");
            props = props.WithoutTopicAlias();
        }

        if (_serverMaximumPacketSize is { } max)
        {
            var size = MqttOutboundValidation.ComputePublishPacketSize(
                topic, payloadLength, qos, props, _options.ProtocolVersion);
            if (size > max)
            {
                throw new MqttProtocolException(
                    $"Encoded PUBLISH size {size} exceeds the broker's Maximum Packet Size " +
                    $"({max}).");
            }
        }

        return (qos, retain, props);
    }

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
        var pw = new PipeBufferWriter(_transport!.Output, 64 + data.Length);
        MqttPacketEncoder.EncodeAuth(pkt, ref pw);
        var written = pw.WrittenCount;
        pw.Commit();
        await FlushOutputAsync(written, ct).ConfigureAwait(false);
    }

    private async Task SendDisconnectAsync(MqttReasonCode rc, CancellationToken ct)
    {
        try
        {
            var pw = new PipeBufferWriter(_transport!.Output, 8);
            MqttPacketEncoder.EncodeDisconnect(
                new DisconnectPacket { ReasonCode = rc },
                _options.ProtocolVersion,
                ref pw);
            var written = pw.WrittenCount;
            pw.Commit();
            await FlushOutputAsync(written, ct).ConfigureAwait(false);
        }
        catch { /* best effort */ }
    }

    private async Task WriteEnvelopeAsync(OutboundEnvelope env, CancellationToken ct)
    {
        var output = _transport!.Output;
        var version = _options.ProtocolVersion;
        var writer = new PipeBufferWriter(output, SizeHint(env));
        var payload = ReadOnlySequence<byte>.Empty;
        switch (env.Kind)
        {
            case OutboundKind.Publish:
                var pub = (PublishPacket)env.Packet!;
                MqttPacketEncoder.EncodePublishHeader(pub, version, ref writer);
                payload = pub.Payload;
                break;
            case OutboundKind.PubAck:
                MqttPacketEncoder.EncodePubAck(env.PacketId, env.ReasonCode, version, ref writer);
                break;
            case OutboundKind.PubRec:
                MqttPacketEncoder.EncodePubRec(env.PacketId, env.ReasonCode, version, ref writer);
                break;
            case OutboundKind.PubRel:
                MqttPacketEncoder.EncodePubRel(env.PacketId, env.ReasonCode, version, ref writer);
                break;
            case OutboundKind.PubComp:
                MqttPacketEncoder.EncodePubComp(env.PacketId, env.ReasonCode, version, ref writer);
                break;
            case OutboundKind.Subscribe:
                MqttPacketEncoder.EncodeSubscribe(
                    (SubscribePacket)env.Packet!, version, ref writer);
                break;
            case OutboundKind.Unsubscribe:
                MqttPacketEncoder.EncodeUnsubscribe(
                    (UnsubscribePacket)env.Packet!, version, ref writer);
                break;
            case OutboundKind.PingReq:
                MqttPacketEncoder.EncodePingReq(ref writer);
                break;
            case OutboundKind.Disconnect:
                MqttPacketEncoder.EncodeDisconnect(
                    (DisconnectPacket)env.Packet!, version, ref writer);
                break;
        }
        var headerLen = writer.WrittenCount;
        writer.Commit();
        if (!payload.IsEmpty)
        {
            WritePayloadSegments(output, payload);
        }
        _metrics.BytesSent.Add(headerLen + payload.Length);
        var result = await output.FlushAsync(ct).ConfigureAwait(false);
        if (result.IsCompleted) throw new MqttConnectionException("Transport closed during write.");
    }

    private static int SizeHint(in OutboundEnvelope env)
        => env.Kind == OutboundKind.Publish && env.Packet is PublishPacket p
            ? p.Topic.Length + 48
            : 64;

    private static void WritePayloadSegments(PipeWriter output, in ReadOnlySequence<byte> payload)
    {
        // Copy each segment into the pipe via the framework's chunked writer, which fills the
        // current pooled segment before requesting a new one. Requesting a fixed large sizeHint
        // (e.g. GetMemory(payloadLen)) instead forces a fresh segment whenever it doesn't fit the
        // current segment's remaining space, adding a per-publish allocation around buffer-size
        // boundaries.
        foreach (var segment in payload)
        {
            output.Write(segment.Span);
        }
    }

    /// <summary>
    /// Records the sent byte count and flushes the transport output. Used by the synchronous send
    /// paths that encode a packet directly into the <see cref="PipeWriter"/> via
    /// <see cref="PipeBufferWriter"/> and have already committed it.
    /// </summary>
    private async Task FlushOutputAsync(int byteCount, CancellationToken ct)
    {
        _metrics.BytesSent.Add(byteCount);
        var result = await _transport!.Output.FlushAsync(ct).ConfigureAwait(false);
        if (result.IsCompleted)
        {
            throw new MqttConnectionException("Transport closed during write.");
        }
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var env in _outbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await WriteEnvelopeAsync(env, ct).ConfigureAwait(false);
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
                OnPublishCompleted(ack.PacketId);
                break;
            case PubAckPacket rec when type == MqttPacketType.PubRec:
                EnqueuePubRel(rec.PacketId, MqttReasonCode.Success);
                break;
            case PubAckPacket rel when type == MqttPacketType.PubRel:
                // Exactly-once release: forget the recorded identifier and answer with PUBCOMP.
                return HandlePubRelAsync(rel.PacketId);
            case PubAckPacket comp when type == MqttPacketType.PubComp:
                CompletePending(comp.PacketId, comp);
                _packetIds.Release(comp.PacketId);
                OnPublishCompleted(comp.PacketId);
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
        => _ = EnqueueAsync(OutboundEnvelope.ForAck(OutboundKind.PubRel, packetId, rc));

    private void EnqueuePubComp(ushort packetId, MqttReasonCode rc)
        => _ = EnqueueAsync(OutboundEnvelope.ForAck(OutboundKind.PubComp, packetId, rc));

    // Inbound QoS 2 release: forget the recorded identifier (and its persisted receipt), then
    // answer PUBCOMP. Runs on the read loop.
    private async ValueTask HandlePubRelAsync(ushort packetId)
    {
        _inboundQoS2.Remove(packetId);
        if (_qos2Store is not null)
        {
            await _qos2Store.RemoveReceivedQoS2Async(packetId).ConfigureAwait(false);
        }
        EnqueuePubComp(packetId, MqttReasonCode.Success);
    }

    private void CompletePending(ushort packetId, object value)
    {
        if (_pendingAcks.TryRemove(packetId, out var waiter)) waiter.TrySetResult(value);
    }

    private void FaultPending(Exception ex)
    {
        foreach (var kv in _pendingAcks)
        {
            // When persistence is enabled, an in-flight QoS>0 publish is kept parked across the
            // disconnect: it is redelivered on a Session-Present reconnect and the original awaiter
            // completes on the post-reconnect ack. Fault only the rest (subscribe/unsubscribe).
            if (_persistence is not null && _persistedPublishIds.ContainsKey(kv.Key))
            {
                continue;
            }
            if (_pendingAcks.TryRemove(kv.Key, out var waiter))
            {
                waiter.TrySetException(ex);
            }
        }
    }

    // In-flight QoS>0 publish identifiers currently saved in the persistent store. Used to keep the
    // matching awaiters parked across a disconnect (see FaultPending) and to resend on reconnect.
    private readonly ConcurrentDictionary<ushort, bool> _persistedPublishIds = new();

    private void OnPublishCompleted(ushort packetId)
    {
        // Remove a terminally-acked publish from the persistent store (best-effort; the store is
        // idempotent — a missed removal is resent once more on the next reconnect and re-acked).
        if (_persistence is null) return;
        if (_persistedPublishIds.TryRemove(packetId, out _))
        {
            _ = _persistence.RemovePendingPublishAsync(packetId);
        }
    }

    // Resend QoS>0 publishes that were in flight when the previous connection dropped, after a
    // Session-Present reconnect: reserve their packet ids, mark DUP, and enqueue in id order.
    private async Task RedeliverPersistedAsync()
    {
        var pending = await _persistence!.ListPendingPublishesAsync().ConfigureAwait(false);
        if (pending.Count == 0) return;
        var list = new List<(ushort PacketId, MqttMessage Message)>(pending);
        list.Sort((a, b) => a.PacketId.CompareTo(b.PacketId));
        foreach (var (id, message) in list)
        {
            _packetIds.Reserve(id);
            _persistedPublishIds[id] = true;
            var packet = new PublishPacket
            {
                Topic = message.Topic,
                QoS = message.QoS,
                Retain = message.Retain,
                PacketId = id,
                Payload = message.Payload,
                Properties = message.Properties,
                Duplicate = true,
            };
            await EnqueueAsync(OutboundEnvelope.ForPublish(packet)).ConfigureAwait(false);
        }
    }

    // Clean session after (re)connect: the broker kept no record of prior in-flight publishes, so
    // abandon any parked awaiters and clear the store. Claims each id individually (rather than a
    // bulk Clear) so a publish issued concurrently after the read loop started is not disturbed.
    private void DiscardPersistedPublishes()
    {
        foreach (var id in _persistedPublishIds.Keys)
        {
            if (!_persistedPublishIds.TryRemove(id, out _)) continue;
            if (_pendingAcks.TryRemove(id, out var waiter))
            {
                waiter.TrySetException(new MqttConnectionException(
                    "Session not present after reconnect; in-flight publish discarded."));
            }
            _packetIds.Release(id);
        }
    }

    private static MqttMessage ToPersistedMessage(PublishPacket p) => new()
    {
        Topic = p.Topic,
        // Copy the payload so the persisted message owns its bytes independent of the caller buffer.
        Payload = new ReadOnlySequence<byte>(p.Payload.ToArray()),
        QoS = p.QoS,
        Retain = p.Retain,
        Properties = p.Properties,
    };

    /// <summary>Inbound MQTT 5 topic-alias table (alias → topic). Single-writer (read loop) safe.</summary>
    private readonly Dictionary<ushort, string> _inboundAliases = new();

    // Inbound QoS 2 receipt state: packet identifiers received in a PUBLISH but not yet released by
    // a PUBREL. Used to deliver an exactly-once message a single time and de-duplicate a redelivered
    // PUBLISH. Single-writer (read loop) safe, like _inboundAliases.
    private readonly HashSet<ushort> _inboundQoS2 = new();

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
            // Exactly-once. A redelivered identifier (already recorded, not yet released by PUBREL)
            // is re-acked with PUBREC but not dispatched a second time.
            if (!_inboundQoS2.Add(pub.PacketId))
            {
                EnqueuePubRec(pub.PacketId);
                return;
            }
            // First receipt: persist the identifier before PUBREC (so the de-dup survives a crash),
            // then answer PUBREC and deliver once.
            if (_qos2Store is not null)
            {
                await _qos2Store.SaveReceivedQoS2Async(pub.PacketId).ConfigureAwait(false);
            }
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
        => _ = EnqueueAsync(OutboundEnvelope.ForAck(OutboundKind.PubAck, packetId, rc));

    private void EnqueuePubRec(ushort packetId)
        => _ = EnqueueAsync(
            OutboundEnvelope.ForAck(OutboundKind.PubRec, packetId, MqttReasonCode.Success));

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
                await EnqueueAsync(OutboundEnvelope.ForPingReq(), ct).ConfigureAwait(false);
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
        if (!wasManual)
        {
            // A user-initiated DisconnectAsync now drains to the broker's EOF through this path;
            // suppress the "unexpected disconnect" notifications it owns so the observable behavior
            // matches a direct manual disconnect (the caller already knows it disconnected).
            Disconnected?.Invoke(this, new MqttDisconnectedEventArgs(reason, exception));
            FaultPending(
                new MqttConnectionException(
                    reason, exception ?? new InvalidOperationException(reason)));
        }
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
        // The write loop encodes the header directly into the pipe; validate up front so malformed
        // input still throws synchronously at this call site.
        MqttOutboundValidation.ValidatePublish(topic, payload.Length, properties);
        (qos, retain, properties) =
            ApplyBrokerLimits(topic, qos, retain, payload.Length, properties);
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

        if (qos == MqttQoS.AtMostOnce)
        {
            await EnqueueAsync(OutboundEnvelope.ForPublish(packet), cancellationToken)
                .ConfigureAwait(false);
            return new MqttPublishResult(MqttReasonCode.Success);
        }

        // Receive Maximum flow control: bound in-flight QoS>0 publishes to the broker's quota.
        var quota = _inflightQuota;
        if (quota is not null)
        {
            try
            {
                if (_options.ReceiveMaximumBehavior == MqttReceiveMaximumBehavior.Backpressure)
                {
                    await quota.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (!quota.Wait(0))
                {
                    throw new MqttProtocolException(
                        "In-flight QoS>0 publishes have reached the broker's Receive Maximum.");
                }
            }
            catch
            {
                _packetIds.Release(packetId);
                throw;
            }
        }

        var waiter = AckCompletionSource.Rent(cancellationToken);
        var startTs = Stopwatch.GetTimestamp();
        try
        {
            // Persist the in-flight publish before it goes on the wire so it can be redelivered
            // after a Session-Present reconnect. Mark the id in _persistedPublishIds BEFORE
            // registering the waiter, so a concurrent disconnect's FaultPending consistently keeps
            // it parked (await-continuity) rather than faulting it mid-persist.
            if (_persistence is not null)
            {
                _persistedPublishIds[packetId] = true;
                await _persistence.SavePendingPublishAsync(packetId, ToPersistedMessage(packet))
                    .ConfigureAwait(false);
            }
            _pendingAcks[packetId] = waiter;
            await EnqueueAsync(OutboundEnvelope.ForPublish(packet), cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Setup failed before the publish reached a parked-and-acked state (e.g. the store
            // write or the enqueue threw): fully unwind so no quota slot or packet id leaks.
            _pendingAcks.TryRemove(packetId, out _);
            _persistedPublishIds.TryRemove(packetId, out _);
            _packetIds.Release(packetId);
            quota?.Release();
            throw;
        }
        // From here the in-flight machinery (ack dispatch / FaultPending / discard) owns the packet
        // id; this method only releases the Receive-Maximum quota slot it acquired.
        try
        {
            var ack = (PubAckPacket?)await waiter.ValueTask.ConfigureAwait(false);
            var elapsedMs = (Stopwatch.GetTimestamp() - startTs) * 1000.0 / Stopwatch.Frequency;
            _metrics.PublishAckLatency.Record(elapsedMs);
            return new MqttPublishResult(
                ack?.ReasonCode ?? MqttReasonCode.Success, ack?.ReasonString);
        }
        finally
        {
            quota?.Release();
        }
    }

    /// <summary>
    /// Attempts to enqueue a QoS 0 publish without awaiting transmission. Returns false when the
    /// queue is full.
    /// </summary>
    public bool TryPublish(string topic, ReadOnlySpan<byte> payload, bool retain = false)
    {
        EnsureConnected();
        MqttOutboundValidation.ValidatePublish(topic, payload.Length, null);
        (_, retain, _) = ApplyBrokerLimits(topic, MqttQoS.AtMostOnce, retain, payload.Length, null);
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
        var envelope = OutboundEnvelope.ForPublish(packet, pooled, _options.ClearPooledBuffers);
        if (_outbound.Writer.TryWrite(envelope))
        {
            _metrics.Publishes.Add(1);
            return true;
        }
        envelope.Dispose();
        return false;
    }

    // ---- MQTT 5 request/response helper ----

    private readonly ConcurrentDictionary<string, TaskCompletionSource<MqttMessage>>
        _pendingRequests = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _responseSubGate = new(1, 1);
    private MqttSubscription? _responseSub;
    private string? _responseTopic;

    /// <summary>
    /// Sends an MQTT 5 request and awaits the correlated response. Publishes the payload to
    /// <paramref name="requestTopic"/> with a Response Topic and a unique Correlation Data, then
    /// completes when a message carrying the same Correlation Data arrives on the response topic.
    /// The client lazily subscribes to a shared response topic (override via
    /// <see cref="MqttRequestOptions.ResponseTopic"/>) on the first request.
    /// </summary>
    public async ValueTask<MqttMessage> RequestAsync(
        string requestTopic,
        ReadOnlyMemory<byte> payload,
        MqttRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MqttRequestOptions();
        var responseTopic = await EnsureResponseSubscriptionAsync(options, cancellationToken)
            .ConfigureAwait(false);

        var correlation = Guid.NewGuid().ToByteArray();
        var key = Convert.ToBase64String(correlation);
        var tcs = new TaskCompletionSource<MqttMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[key] = tcs;
        try
        {
            var props = new MqttPublishProperties
            {
                ResponseTopic = responseTopic,
                CorrelationData = correlation,
            };
            await PublishAsync(
                requestTopic, payload, options.QoS, retain: false, props, cancellationToken)
                .ConfigureAwait(false);

            var timeout = options.Timeout ?? _options.OperationTimeout;
            using var timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(timeout, timeoutCts.Token);
            var completed = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);
            if (completed != tcs.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"No response on '{responseTopic}' within {timeout}.");
            }
            timeoutCts.Cancel();   // stop the timer
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingRequests.TryRemove(key, out _);
        }
    }

    private async ValueTask<string> EnsureResponseSubscriptionAsync(
        MqttRequestOptions options, CancellationToken ct)
    {
        await _responseSubGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_responseSub is not null)
            {
                return _responseTopic!;
            }
            var topic = options.ResponseTopic ?? $"mqtt-client/{Guid.NewGuid():N}/response";
            _responseSub = await SubscribeAsync(
                topic, OnResponseMessageAsync, cancellationToken: ct).ConfigureAwait(false);
            _responseTopic = topic;
            return topic;
        }
        finally
        {
            _responseSubGate.Release();
        }
    }

    private ValueTask OnResponseMessageAsync(MqttMessage message)
    {
        if (message.Properties?.CorrelationData is { } cd)
        {
            var key = Convert.ToBase64String(cd.ToArray());
            if (_pendingRequests.TryRemove(key, out var tcs))
            {
                // The inline payload/correlation are valid only inside this handler — hand the
                // awaiting caller a fully owned copy.
                tcs.TrySetResult(CopyResponse(message));
            }
        }
        return default;
    }

    private static MqttMessage CopyResponse(MqttMessage m) => new()
    {
        Topic = m.Topic,
        PayloadMemory = m.PayloadMemory.ToArray(),
        QoS = m.QoS,
        Retain = m.Retain,
        Duplicate = m.Duplicate,
        Properties = m.Properties is { } p
            ? new MqttPublishProperties
            {
                PayloadFormatIndicator = p.PayloadFormatIndicator,
                MessageExpiryInterval = p.MessageExpiryInterval,
                ResponseTopic = p.ResponseTopic,
                CorrelationData = p.CorrelationData?.ToArray(),
                ContentType = p.ContentType,
                SubscriptionIdentifiers = p.SubscriptionIdentifiers,
                UserProperties = p.UserProperties,
            }
            : null,
    };

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
        MqttOutboundValidation.ValidateTopicFilter(topicFilter);
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
        var waiter = AckCompletionSource.Rent(cancellationToken);
        _pendingAcks[packetId] = waiter;
        await EnqueueAsync(
            OutboundEnvelope.ForPacket(OutboundKind.Subscribe, sp), cancellationToken)
            .ConfigureAwait(false);
        await waiter.ValueTask.ConfigureAwait(false);
        return sub;
    }

    private async ValueTask UnsubscribeOnServerAsync(string topicFilter, CancellationToken ct)
    {
        if (State != MqttConnectionState.Connected) return;
        var packetId = _packetIds.Allocate();
        var u = new UnsubscribePacket { PacketId = packetId, Topics = new[] { topicFilter } };
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
        await EnqueueAsync(OutboundEnvelope.ForPacket(OutboundKind.Unsubscribe, u), ct)
            .ConfigureAwait(false);
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

    /// <summary>
    /// Forces a reconnect that re-reads credentials via the configured
    /// <see cref="MqttClientOptions.CredentialsProvider"/> — used to present rotated credentials
    /// (e.g. a refreshed Kubernetes service-account token). Gracefully disconnects the current
    /// session and re-establishes it. The MQTT session is dropped; with
    /// <see cref="MqttClientOptions.CleanStart"/> = <c>false</c> the broker restores subscriptions.
    /// <para>
    /// Must be called while <see cref="State"/> is <see cref="MqttConnectionState.Connected"/>
    /// (throws otherwise); concurrent calls are serialized. If the reconnect's connect attempt
    /// fails, the configured auto-reconnect policy (if any) takes over and the original exception is
    /// rethrown.
    /// </para>
    /// </summary>
    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await _reconnectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = State;
            if (state != MqttConnectionState.Connected)
            {
                throw new InvalidOperationException(
                    $"ReconnectAsync requires a Connected client; current state is {state}.");
            }
            // Graceful manual teardown (stops the loops and suppresses their own reconnect), then a
            // fresh connect that re-reads credentials.
            await DisconnectAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The failed connect leaves the state at Connecting; reset it and let the configured
                // auto-reconnect policy re-establish (mirrors an unexpected drop) instead of sticking.
                SetState(MqttConnectionState.Disconnected);
                Volatile.Write(ref _manualDisconnect, 0);
                if (_options.Reconnect is { } policy)
                {
                    _ = ReconnectLoopAsync(policy);
                }
                throw;
            }
        }
        finally
        {
            _reconnectGate.Release();
        }
    }

    private void OnCredentialsChanged(object? sender, EventArgs e)
        => _ = ReconnectOnCredentialsChangedAsync();

    private async Task ReconnectOnCredentialsChangedAsync()
    {
        // A reconnect is only needed for a live session; a not-yet-connected client reads the new
        // credentials on its next connect anyway. Failures self-heal via the auto-reconnect loop.
        if (State != MqttConnectionState.Connected) return;
        try
        {
            await ReconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MqttLog.ConnectionLoopFailed(_logger, ex, "credential-change reconnect");
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _manualDisconnect, 1);
        if (State != MqttConnectionState.Connected) return;
        var disconnect = new DisconnectPacket { ReasonCode = MqttReasonCode.Success };
        try
        {
            await EnqueueAsync(
                OutboundEnvelope.ForPacket(OutboundKind.Disconnect, disconnect), cancellationToken)
                .ConfigureAwait(false);
        }
        catch { }
        // Let the broker close the TCP connection in response to our DISCONNECT so it is the active
        // closer and our ephemeral port is not parked in TIME_WAIT (otherwise rapid connect/
        // disconnect churn exhausts the local port range -> SocketException 10048). The read loop
        // completes when it observes the broker's EOF; bounded so a broker that doesn't close
        // promptly still disconnects.
        if (_readLoop is { } readLoop)
        {
#if NETSTANDARD2_1
            await Task.WhenAny(readLoop, Task.Delay(GracefulCloseTimeout, cancellationToken))
                .ConfigureAwait(false);
#else
            // Bounded wait for the broker's close without allocating a Task.Delay timer-task plus a
            // Task.WhenAny promise: a linked CTS with CancelAfter trips the wait on timeout/cancel.
            using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            graceCts.CancelAfter(GracefulCloseTimeout);
            try { await readLoop.WaitAsync(graceCts.Token).ConfigureAwait(false); }
            catch { /* best-effort graceful close; proceed to cleanup regardless */ }
#endif
        }
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
        if (_options.CredentialsProvider is IMqttCredentialsChangeNotifier notifier)
        {
            notifier.CredentialsChanged -= OnCredentialsChanged;
        }
        _outbound.Writer.TryComplete();
        await CleanupAsync("dispose").ConfigureAwait(false);
        _loopCts?.Dispose();
        _reconnectGate.Dispose();
        _inflightQuota?.Dispose();
        _responseSubGate.Dispose();
        foreach (var kv in _pendingRequests)
        {
            kv.Value.TrySetException(new ObjectDisposedException(nameof(MqttClient)));
        }
        _pendingRequests.Clear();
        _metrics.Dispose();
        // The client owns the lifetime of the credentials provider it was given (most providers are
        // stateless and not disposable; a watching provider — e.g. the Kubernetes token provider —
        // releases its file watcher here).
        switch (_options.CredentialsProvider)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    /// <summary>
    /// Identifies the control packet an <see cref="OutboundEnvelope"/> carries so the write loop can
    /// encode it directly into the transport <see cref="PipeWriter"/>.
    /// </summary>
    private enum OutboundKind : byte
    {
        Publish = 0,
        PubAck = 1,
        PubRec = 2,
        PubRel = 3,
        PubComp = 4,
        Subscribe = 5,
        Unsubscribe = 6,
        PingReq = 7,
        Disconnect = 8,
    }

    /// <summary>
    /// A queued outbound control packet. Carries the packet data (not pre-encoded bytes) so the
    /// single write loop encodes it directly into the <see cref="PipeWriter"/>; acks travel as an
    /// inline id + reason code (no object), other packets reference their already-allocated packet.
    /// Only an optional pooled payload buffer is owned and returned to the pool on Dispose.
    /// </summary>
    private readonly struct OutboundEnvelope : IDisposable
    {
        private OutboundEnvelope(
            OutboundKind kind,
            ushort packetId,
            MqttReasonCode reasonCode,
            object? packet,
            byte[]? pooledPayload,
            bool clearOnReturn,
            TaskCompletionSource<object?>? onSent)
        {
            Kind = kind;
            PacketId = packetId;
            ReasonCode = reasonCode;
            Packet = packet;
            PooledPayload = pooledPayload;
            ClearOnReturn = clearOnReturn;
            OnSent = onSent;
        }

        public OutboundKind Kind { get; }
        public ushort PacketId { get; }
        public MqttReasonCode ReasonCode { get; }
        public object? Packet { get; }
        public byte[]? PooledPayload { get; }
        public bool ClearOnReturn { get; }
        public TaskCompletionSource<object?>? OnSent { get; }

        public static OutboundEnvelope ForPublish(
            PublishPacket packet,
            byte[]? pooledPayload = null,
            bool clearOnReturn = false)
            => new(OutboundKind.Publish, 0, default, packet, pooledPayload, clearOnReturn, null);

        public static OutboundEnvelope ForAck(OutboundKind kind, ushort packetId, MqttReasonCode rc)
            => new(kind, packetId, rc, null, null, false, null);

        public static OutboundEnvelope ForPacket(OutboundKind kind, object packet)
            => new(kind, 0, default, packet, null, false, null);

        public static OutboundEnvelope ForPingReq()
            => new(OutboundKind.PingReq, 0, default, null, null, false, null);

        public void Dispose()
        {
            if (PooledPayload is not null)
            {
                ArrayPool<byte>.Shared.Return(PooledPayload, clearArray: ClearOnReturn);
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
