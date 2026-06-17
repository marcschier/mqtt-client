// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using Mqtt.Client.Protocol;

namespace Mqtt.Client;

/// <summary>
/// A single inbound MQTT message delivered to a subscriber.
/// </summary>
public sealed class MqttMessage
{
    public required string Topic { get; init; }
    public required ReadOnlyMemory<byte> Payload { get; init; }
    public MqttQoS QoS { get; init; }
    public bool Retain { get; init; }
    public bool Duplicate { get; init; }
    public MqttPublishProperties? Properties { get; init; }
}

/// <summary>Result of a QoS&gt;0 publish (carries the broker reason code for MQTT v5).</summary>
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

/// <summary>Outcome of CONNECT.</summary>
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
    public bool IsSuccess => (byte)ReasonCode < 0x80;
}
