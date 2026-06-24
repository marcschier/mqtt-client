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

    /// <summary>Returns a copy with <see cref="CorrelationData"/> replaced (others preserved).</summary>
    internal MqttPublishProperties WithCorrelationData(ReadOnlyMemory<byte> correlationData)
        => new()
        {
            PayloadFormatIndicator = PayloadFormatIndicator,
            MessageExpiryInterval = MessageExpiryInterval,
            TopicAlias = TopicAlias,
            ResponseTopic = ResponseTopic,
            CorrelationData = correlationData,
            ContentType = ContentType,
            SubscriptionIdentifiers = SubscriptionIdentifiers,
            UserProperties = UserProperties,
        };

    /// <summary>Returns a copy with <see cref="TopicAlias"/> cleared (others preserved).</summary>
    internal MqttPublishProperties WithoutTopicAlias()
        => new()
        {
            PayloadFormatIndicator = PayloadFormatIndicator,
            MessageExpiryInterval = MessageExpiryInterval,
            TopicAlias = null,
            ResponseTopic = ResponseTopic,
            CorrelationData = CorrelationData,
            ContentType = ContentType,
            SubscriptionIdentifiers = SubscriptionIdentifiers,
            UserProperties = UserProperties,
        };
}

/// <summary>
/// Options for <see cref="MqttClient.RequestAsync"/> (MQTT 5 request/response).
/// </summary>
public sealed class MqttRequestOptions
{
    /// <summary>
    /// Topic the responder should reply on (set as the request's Response Topic). When null, the
    /// client uses a generated per-client response topic established on the first request.
    /// </summary>
    public string? ResponseTopic { get; set; }

    /// <summary>QoS for the request publish. Defaults to <see cref="MqttQoS.AtLeastOnce"/>.</summary>
    public MqttQoS QoS { get; set; } = MqttQoS.AtLeastOnce;

    /// <summary>
    /// Maximum time to await the response before a <see cref="TimeoutException"/>. When null, the
    /// client's <see cref="MqttClientOptions.OperationTimeout"/> is used.
    /// </summary>
    public TimeSpan? Timeout { get; set; }
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
