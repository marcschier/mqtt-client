// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Collections.Generic;
namespace Mqtt.Client;

internal sealed class ConnectPacket
{
    public required MqttProtocolVersion ProtocolVersion { get; init; }
    public required string ClientId { get; init; }
    public bool CleanStart { get; init; } = true;
    public ushort KeepAliveSeconds { get; init; } = 60;
    public string? Username { get; init; }
    public byte[]? Password { get; init; }
    public MqttLastWill? Will { get; init; }

    // v5-only:
    public uint? SessionExpiryInterval { get; init; }
    public ushort? ReceiveMaximum { get; init; }
    public uint? MaximumPacketSize { get; init; }
    public ushort? TopicAliasMaximum { get; init; }
    public bool? RequestResponseInformation { get; init; }
    public bool? RequestProblemInformation { get; init; }
    public string? AuthenticationMethod { get; init; }
    public byte[]? AuthenticationData { get; init; }
    public IReadOnlyList<MqttUserProperty>? UserProperties { get; init; }
}

internal sealed class ConnAckPacket
{
    public bool SessionPresent { get; init; }
    public MqttReasonCode ReasonCode { get; init; }

    // v5:
    public uint? SessionExpiryInterval { get; init; }
    public ushort? ReceiveMaximum { get; init; }
    public MqttQoS MaximumQoS { get; init; } = MqttQoS.ExactlyOnce;
    public bool RetainAvailable { get; init; } = true;
    public uint? MaximumPacketSize { get; init; }
    public string? AssignedClientId { get; init; }
    public ushort? TopicAliasMaximum { get; init; }
    public string? ReasonString { get; init; }
    public bool WildcardSubscriptionAvailable { get; init; } = true;
    public bool SubscriptionIdentifiersAvailable { get; init; } = true;
    public bool SharedSubscriptionAvailable { get; init; } = true;
    public ushort? ServerKeepAlive { get; init; }
    public string? ResponseInformation { get; init; }
    public string? ServerReference { get; init; }
    public string? AuthenticationMethod { get; init; }
    public byte[]? AuthenticationData { get; init; }
    public IReadOnlyList<MqttUserProperty>? UserProperties { get; init; }
}

internal sealed class PublishPacket
{
    public required string Topic { get; init; }
    public ushort PacketId { get; init; }    // 0 for QoS0
    public MqttQoS QoS { get; init; }
    public bool Retain { get; init; }
    public bool Duplicate { get; init; }
    public required ReadOnlyMemory<byte> Payload { get; init; }
    public MqttPublishProperties? Properties { get; init; }   // v5 only
}

internal sealed class PubAckPacket
{
    public required ushort PacketId { get; init; }
    public MqttReasonCode ReasonCode { get; init; } = MqttReasonCode.Success;
    public string? ReasonString { get; init; }
    public IReadOnlyList<MqttUserProperty>? UserProperties { get; init; }
}

internal sealed class SubscribePacket
{
    public required ushort PacketId { get; init; }
    public required IReadOnlyList<SubscribeFilter> Filters { get; init; }
    public uint? SubscriptionIdentifier { get; init; }     // v5
    public IReadOnlyList<MqttUserProperty>? UserProperties { get; init; }
}

internal readonly record struct SubscribeFilter(
    string Topic,
    MqttQoS QoS,
    bool NoLocal = false,
    bool RetainAsPublished = false,
    RetainHandling RetainHandling = RetainHandling.SendAtSubscribe);

internal enum RetainHandling : byte
{
    SendAtSubscribe = 0,
    SendIfNewSubscription = 1,
    DoNotSend = 2,
}

internal sealed class SubAckPacket
{
    public required ushort PacketId { get; init; }
    public required IReadOnlyList<MqttReasonCode> ReasonCodes { get; init; }
    public string? ReasonString { get; init; }
    public IReadOnlyList<MqttUserProperty>? UserProperties { get; init; }
}

internal sealed class UnsubscribePacket
{
    public required ushort PacketId { get; init; }
    public required IReadOnlyList<string> Topics { get; init; }
    public IReadOnlyList<MqttUserProperty>? UserProperties { get; init; }
}

internal sealed class UnsubAckPacket
{
    public required ushort PacketId { get; init; }
    public IReadOnlyList<MqttReasonCode>? ReasonCodes { get; init; }   // v5 only
    public string? ReasonString { get; init; }
    public IReadOnlyList<MqttUserProperty>? UserProperties { get; init; }
}

internal sealed class DisconnectPacket
{
    public MqttReasonCode ReasonCode { get; init; } = MqttReasonCode.Success;
    public uint? SessionExpiryInterval { get; init; }
    public string? ReasonString { get; init; }
    public string? ServerReference { get; init; }
    public IReadOnlyList<MqttUserProperty>? UserProperties { get; init; }
}

internal sealed class AuthPacket
{
    public MqttReasonCode ReasonCode { get; init; }
    public string? AuthenticationMethod { get; init; }
    public byte[]? AuthenticationData { get; init; }
    public string? ReasonString { get; init; }
    public IReadOnlyList<MqttUserProperty>? UserProperties { get; init; }
}
