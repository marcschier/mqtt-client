// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Net.Security;

namespace Mqtt.Client;

/// <summary>Options controlling client connection and protocol behavior.</summary>
public sealed class MqttClientOptions
{
    /// <summary>Broker host name (TCP/WS hostname).</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Broker port. Defaults: 1883 (TCP), 8883 (TLS), 80 (WS), 443 (WSS).</summary>
    public int Port { get; set; } = 1883;

    /// <summary>Underlying transport.</summary>
    public MqttTransportType Transport { get; set; } = MqttTransportType.Tcp;

    /// <summary>
    /// Optional TLS configuration (used for <see cref="MqttTransportType.Tls"/> /
    /// <see cref="MqttTransportType.WebSocketSecure"/>).
    /// </summary>
    public SslClientAuthenticationOptions? Tls { get; set; }

    /// <summary>Protocol version to use.</summary>
    public MqttProtocolVersion ProtocolVersion { get; set; } = MqttProtocolVersion.V500;

    /// <summary>
    /// Client identifier sent to the broker. Empty string lets the broker assign one (MQTT 5).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Whether to start a clean session.</summary>
    public bool CleanStart { get; set; } = true;

    /// <summary>Keep-alive interval, seconds. 0 disables.</summary>
    public ushort KeepAliveSeconds { get; set; } = 60;

    /// <summary>Username for password auth.</summary>
    public string? Username { get; set; }

    /// <summary>Password for password auth.</summary>
    public byte[]? Password { get; set; }

    /// <summary>Last-will message.</summary>
    public MqttLastWill? Will { get; set; }

    /// <summary>Auto-reconnect policy. Null = no auto-reconnect (manual).</summary>
    public MqttReconnectPolicy? Reconnect { get; set; } = MqttReconnectPolicy.Exponential();

    /// <summary>
    /// Maximum number of in-flight QoS&gt;0 publishes from this client (default 65535).
    /// </summary>
    public ushort ReceiveMaximum { get; set; } = ushort.MaxValue;

    /// <summary>
    /// Default outbound channel capacity per subscription (used when none specified).
    /// </summary>
    public int DefaultSubscriptionCapacity { get; set; } = 1024;

    /// <summary>Optional path for WebSocket transports (default: <c>/mqtt</c>).</summary>
    public string? WebSocketPath { get; set; }

    /// <summary>
    /// Maximum size in bytes of any single incoming packet from the broker. Packets exceeding
    /// this size cause the client to disconnect with <c>PacketTooLarge</c>. Defaults to 1 MiB —
    /// large enough for typical IoT payloads, small enough to prevent a malicious or
    /// compromised broker from exhausting client memory by advertising a huge remaining length.
    /// Set higher if you legitimately need larger payloads.
    /// </summary>
    public int MaxIncomingPacketSize { get; set; } = 1 * 1024 * 1024;

    /// <summary>
    /// MQTT 5 inbound topic-alias capacity. Advertised in CONNECT as <c>TopicAliasMaximum</c>.
    /// Brokers may then send PUBLISH packets with a numeric alias instead of the full topic,
    /// reducing wire bytes for hot topics. 0 (default) disables inbound aliases.
    /// </summary>
    public ushort TopicAliasMaximum { get; set; }

    /// <summary>
    /// When true, payload bytes are zeroed before their pooled buffer is returned to the
    /// <see cref="System.Buffers.ArrayPool{T}"/> in <see cref="MqttClient.TryPublish"/>. Default
    /// is <c>false</c> for performance; set to <c>true</c> if your payloads carry secrets and you
    /// want to minimise the window during which they could be observed by another pool tenant.
    /// </summary>
    public bool ClearPooledBuffers { get; set; }

    /// <summary>Network operation timeout used for connect/disconnect/publish acks.</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>MQTT 5 enhanced authentication handler. When non-null on a v5 connect, the
    /// client drives the SASL-style multi-round-trip auth exchange with the broker.</summary>
    public IMqttAuthenticationHandler? AuthenticationHandler { get; set; }

    /// <summary>
    /// Maximum number of inbound AUTH 0x18 roundtrips per handshake before the client aborts the
    /// exchange with Protocol Error. Defends against broker-driven loops.
    /// </summary>
    public int MaxAuthRoundTrips { get; set; } = 5;
}

public enum MqttTransportType
{
    Tcp = 0,
    Tls = 1,
    WebSocket = 2,
    WebSocketSecure = 3,
}

/// <summary>Reconnect strategy.</summary>
public sealed class MqttReconnectPolicy
{
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);
    public double BackoffFactor { get; init; } = 2.0;
    public double JitterFactor { get; init; } = 0.2;

    public static MqttReconnectPolicy Exponential() => new();

    public static MqttReconnectPolicy Fixed(TimeSpan delay) => new()
    {
        InitialDelay = delay,
        MaxDelay = delay,
        BackoffFactor = 1.0,
        JitterFactor = 0,
    };
}
