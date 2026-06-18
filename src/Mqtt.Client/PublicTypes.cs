// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Collections.Generic;

namespace Mqtt.Client;

/// <summary>
/// User property (MQTT v5 only). Multiple instances are allowed per packet.
/// </summary>
public readonly record struct MqttUserProperty(string Name, string Value);

/// <summary>
/// Optional MQTT v5 properties for an outbound or inbound PUBLISH.
/// </summary>
public sealed class MqttPublishProperties
{
    public byte? PayloadFormatIndicator { get; init; }
    public uint? MessageExpiryInterval { get; init; }
    public ushort? TopicAlias { get; init; }
    public string? ResponseTopic { get; init; }
    public ReadOnlyMemory<byte>? CorrelationData { get; init; }
    public string? ContentType { get; init; }
    public IReadOnlyList<uint>? SubscriptionIdentifiers { get; init; }
    public IReadOnlyList<MqttUserProperty>? UserProperties { get; init; }
}

/// <summary>
/// Last-will (testament) message published by the broker on ungraceful disconnect.
/// </summary>
public sealed class MqttLastWill
{
    public required string Topic { get; init; }

    /// <summary>
    /// Will payload bytes. May span multiple segments. Set via this property or the
    /// <see cref="PayloadMemory"/> convenience initializer.
    /// </summary>
    public ReadOnlySequence<byte> Payload { get; init; }

    /// <summary>
    /// Contiguous view of <see cref="Payload"/>, and a convenience initializer that wraps a
    /// <see cref="ReadOnlyMemory{T}"/> as the will payload.
    /// </summary>
    public ReadOnlyMemory<byte> PayloadMemory
    {
        get => Payload.IsSingleSegment ? Payload.First : Payload.ToArray();
        init => Payload = new ReadOnlySequence<byte>(value);
    }

    public MqttQoS QoS { get; init; }
    public bool Retain { get; init; }
    public uint? DelayIntervalSeconds { get; init; }   // v5
    public MqttPublishProperties? Properties { get; init; } // v5
}
