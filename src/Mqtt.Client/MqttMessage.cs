// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Threading;
namespace Mqtt.Client;

/// <summary>
/// A single inbound MQTT message delivered to a subscriber.
/// </summary>
/// <remarks>
/// When <see cref="MqttClientOptions.ReuseInboundBuffers"/> is enabled, the payload is backed by a
/// pooled buffer that is returned when the message is disposed. In that mode consumers MUST dispose
/// each message after use and MUST NOT access <see cref="Payload"/>/<see cref="PayloadMemory"/>
/// afterwards. In the default mode the payload is garbage-collected and <see cref="Dispose"/> is a
/// no-op, so disposal is optional and the payload may be retained indefinitely.
/// </remarks>
public sealed class MqttMessage : IDisposable
{
    private int _disposed;

    public required string Topic { get; init; }

    /// <summary>
    /// Payload bytes. May span multiple segments. Use <see cref="PayloadMemory"/> for a contiguous
    /// view (zero-copy when the payload is a single segment, which is the default).
    /// </summary>
    public ReadOnlySequence<byte> Payload { get; init; }

    /// <summary>
    /// Contiguous view of <see cref="Payload"/>. Zero-copy when single-segment; otherwise copies
    /// into a newly allocated array. Also usable as an initializer to set the payload from memory.
    /// </summary>
    public ReadOnlyMemory<byte> PayloadMemory
    {
        get => Payload.IsSingleSegment ? Payload.First : Payload.ToArray();
        init => Payload = new ReadOnlySequence<byte>(value);
    }

    /// <summary>Pooled backing array to return on dispose (null in the default, GC-owned mode).</summary>
    internal byte[]? PooledArray { get; init; }

    public MqttQoS QoS { get; init; }
    public bool Retain { get; init; }
    public bool Duplicate { get; init; }
    public MqttPublishProperties? Properties { get; init; }

    /// <summary>
    /// Returns the pooled payload buffer (if any) to the shared pool. Idempotent. No-op when the
    /// payload is garbage-collected (the default mode).
    /// </summary>
    public void Dispose()
    {
        if (PooledArray is { } array && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            ArrayPool<byte>.Shared.Return(array);
        }
    }
}

/// <summary>
/// Result of a QoS&gt;0 publish (carries the broker reason code for MQTT v5).
/// </summary>
public readonly struct MqttPublishResult
{
    public MqttPublishResult(MqttReasonCode reasonCode, string? reasonString = null)
    {
        ReasonCode = reasonCode;
        ReasonString = reasonString;
    }

    public MqttReasonCode ReasonCode { get; }
    public string? ReasonString { get; }
    public bool IsSuccess => (byte)ReasonCode < 0x80;
}

/// <summary>
/// Outcome of CONNECT.
/// </summary>
public sealed class MqttConnectResult
{
    public required MqttReasonCode ReasonCode { get; init; }
    public bool SessionPresent { get; init; }
    public string? AssignedClientId { get; init; }
    public ushort? ServerKeepAlive { get; init; }
    public ushort? ReceiveMaximum { get; init; }
    public uint? MaximumPacketSize { get; init; }
    public MqttQoS MaximumQoS { get; init; } = MqttQoS.ExactlyOnce;
    public bool RetainAvailable { get; init; } = true;
    public bool WildcardSubscriptionAvailable { get; init; } = true;
    public bool SharedSubscriptionAvailable { get; init; } = true;
    public ushort? TopicAliasMaximum { get; init; }
    public string? ReasonString { get; init; }
    public byte[]? AuthenticationData { get; init; }
    public bool IsSuccess => (byte)ReasonCode < 0x80;
}
