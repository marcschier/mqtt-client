// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Collections.Generic;
namespace Mqtt.Client;

/// <summary>
/// Tries to decode a single MQTT control packet from buffered bytes. Returns false when not enough
/// data is buffered to decode a full packet.
/// </summary>
internal static class MqttPacketDecoder
{
    /// <summary>
    /// Attempts to decode one packet.
    /// </summary>
    /// <param name="buffer">Buffer to read from.</param>
    /// <param name="version">Active protocol version.</param>
    /// <param name="packet">Decoded packet (one of the internal Packets types) on success.</param>
    /// <param name="firstByte">Fixed-header first byte on success.</param>
    /// <param name="consumed">
    /// Position after the consumed packet on success; unchanged on failure.
    /// </param>
    public static bool TryDecode(
        in ReadOnlySequence<byte> buffer,
        MqttProtocolVersion version,
        out object? packet,
        out byte firstByte,
        out SequencePosition consumed)
        => TryDecode(
            buffer,
            version,
            maxPacketSize: int.MaxValue,
            out packet,
            out firstByte,
            out consumed);

    /// <summary>
    /// Attempts to decode one packet, rejecting any whose advertised remaining length exceeds
    /// <paramref name="maxPacketSize"/>. This is the primary defense against a malicious or
    /// compromised broker exhausting client memory by advertising a huge length.
    /// </summary>
    public static bool TryDecode(
        in ReadOnlySequence<byte> buffer,
        MqttProtocolVersion version,
        int maxPacketSize,
        out object? packet,
        out byte firstByte,
        out SequencePosition consumed)
        => TryDecode(
            buffer, version, maxPacketSize, poolPayload: false,
            out packet, out firstByte, out consumed);

    /// <summary>
    /// As the maximum-packet-size overload, but when <paramref name="poolPayload"/> is true a
    /// decoded PUBLISH payload is left as a zero-copy slice of <paramref name="buffer"/> (the
    /// caller must copy it out before advancing the underlying reader). Used by the read loop when
    /// inbound buffer reuse is enabled.
    /// </summary>
    public static bool TryDecode(
        in ReadOnlySequence<byte> buffer,
        MqttProtocolVersion version,
        int maxPacketSize,
        bool poolPayload,
        out object? packet,
        out byte firstByte,
        out SequencePosition consumed)
    {
        packet = null;
        firstByte = 0;
        consumed = buffer.Start;

        if (buffer.Length < 2)
        {
            return false;
        }

        var reader = new MqttSequenceReader(buffer);
        firstByte = reader.ReadByte();

        var remainingLength = TryReadVarInt(ref reader, out var lenBytes, out var lenSuccess);
        if (!lenSuccess) return false;
        if (remainingLength > (uint)maxPacketSize)
        {
            throw new MqttProtocolException(
                $"Incoming packet remaining length ({remainingLength}) exceeds " +
                $"MaxIncomingPacketSize ({maxPacketSize}).");
        }
        if (reader.Remaining < remainingLength) return false;

        var fixedHeaderBytes = 1 + lenBytes;
        var payload = buffer.Slice(fixedHeaderBytes, remainingLength);
        var payloadReader = new MqttSequenceReader(payload);

        var type = (MqttPacketType)(firstByte >> 4);
        packet = type switch
        {
            MqttPacketType.ConnAck => DecodeConnAck(ref payloadReader, version),
            MqttPacketType.Publish => DecodePublish(
                firstByte,
                ref payloadReader,
                version,
                (int)remainingLength,
                poolPayload),
            MqttPacketType.PubAck => DecodeAck(ref payloadReader, version, (int)remainingLength),
            MqttPacketType.PubRec => DecodeAck(ref payloadReader, version, (int)remainingLength),
            MqttPacketType.PubRel => DecodeAck(ref payloadReader, version, (int)remainingLength),
            MqttPacketType.PubComp => DecodeAck(ref payloadReader, version, (int)remainingLength),
            MqttPacketType.SubAck => DecodeSubAck(ref payloadReader, version, (int)remainingLength),
            MqttPacketType.UnsubAck => DecodeUnsubAck(
                ref payloadReader,
                version,
                (int)remainingLength),
            MqttPacketType.PingResp => null,
            MqttPacketType.Disconnect => DecodeDisconnect(
                ref payloadReader,
                version,
                (int)remainingLength),
            MqttPacketType.Auth => DecodeAuth(ref payloadReader, (int)remainingLength),
            _ => throw new MqttProtocolException(
                $"Unexpected packet type from broker: 0x{(byte)type:X2}"),
        };
        consumed = buffer.GetPosition(fixedHeaderBytes + remainingLength);
        return true;
    }

    private static uint TryReadVarInt(
        ref MqttSequenceReader reader,
        out int byteCount,
        out bool success)
    {
        uint value = 0;
        var multiplier = 1u;
        byteCount = 0;
        success = false;
        while (true)
        {
            if (reader.Remaining < 1) return 0;
            var b = reader.ReadByte();
            byteCount++;
            value += (uint)(b & 0x7F) * multiplier;
            if ((b & 0x80) == 0)
            {
                success = true;
                return value;
            }
            multiplier *= 128;
            if (multiplier > 128 * 128 * 128)
            {
                throw new MqttProtocolException("Malformed variable byte integer.");
            }
        }
    }

    private static ConnAckPacket DecodeConnAck(
        ref MqttSequenceReader reader,
        MqttProtocolVersion version)
    {
        var ackFlags = reader.ReadByte();
        var rc = (MqttReasonCode)reader.ReadByte();
        var ack = new ConnAckBuilder
        {
            SessionPresent = (ackFlags & 0x01) == 0x01,
            ReasonCode = rc,
        };

        if (version == MqttProtocolVersion.V500 && reader.Remaining > 0)
        {
            var propLen = (int)reader.ReadVarInt(out _);
            var end = reader.Consumed + propLen;
            while (reader.Consumed < end)
            {
                var id = (MqttPropertyId)reader.ReadByte();
                switch (id)
                {
                    case MqttPropertyId.SessionExpiryInterval: ack.SessionExpiryInterval = reader
                        .ReadUInt32BigEndian(); break;
                    case MqttPropertyId.ReceiveMaximum: ack.ReceiveMaximum = reader
                        .ReadUInt16BigEndian(); break;
                    case MqttPropertyId.MaximumQoS: ack.MaximumQoS = (MqttQoS)reader
                        .ReadByte(); break;
                    case MqttPropertyId.RetainAvailable: ack.RetainAvailable = reader
                        .ReadByte() == 1; break;
                    case MqttPropertyId.MaximumPacketSize: ack.MaximumPacketSize = reader
                        .ReadUInt32BigEndian(); break;
                    case MqttPropertyId.AssignedClientIdentifier: ack.AssignedClientId = reader
                        .ReadString(); break;
                    case MqttPropertyId.TopicAliasMaximum: ack.TopicAliasMaximum = reader
                        .ReadUInt16BigEndian(); break;
                    case MqttPropertyId.ReasonString: ack.ReasonString = reader.ReadString(); break;
                    case MqttPropertyId.UserProperty: ack.AddUserProperty(
                        reader.ReadString(),
                        reader.ReadString()); break;
                    case MqttPropertyId.WildcardSubscriptionAvailable: ack
                        .WildcardSubscriptionAvailable = reader.ReadByte() == 1; break;
                    case MqttPropertyId.SubscriptionIdentifiersAvailable: ack
                        .SubscriptionIdentifiersAvailable = reader
                        .ReadByte() == 1; break;
                    case MqttPropertyId.SharedSubscriptionAvailable: ack.SharedSubscriptionAvailable
                        = reader.ReadByte() == 1; break;
                    case MqttPropertyId.ServerKeepAlive: ack.ServerKeepAlive = reader
                        .ReadUInt16BigEndian(); break;
                    case MqttPropertyId.ResponseInformation: ack.ResponseInformation = reader
                        .ReadString(); break;
                    case MqttPropertyId.ServerReference: ack.ServerReference = reader
                        .ReadString(); break;
                    case MqttPropertyId.AuthenticationMethod: ack.AuthenticationMethod = reader
                        .ReadString(); break;
                    case MqttPropertyId.AuthenticationData: ack.AuthenticationData = reader
                        .ReadBinaryData(); break;
                    default: throw new MqttProtocolException(
                        $"Unexpected CONNACK property 0x{(byte)id:X2}");
                }
            }
        }
        else if (version == MqttProtocolVersion.V311)
        {
            // 3.1.1 reason codes map differently (return codes 0..5). Re-map.
            ack.ReasonCode = MapV311ConnAckReturnCode((byte)rc);
        }
        return ack.Build();
    }

    private static MqttReasonCode MapV311ConnAckReturnCode(byte rc) => rc switch
    {
        0 => MqttReasonCode.Success,
        1 => MqttReasonCode.UnsupportedProtocolVersion,
        2 => MqttReasonCode.ClientIdentifierNotValid,
        3 => MqttReasonCode.ServerUnavailable,
        4 => MqttReasonCode.BadUserNameOrPassword,
        5 => MqttReasonCode.NotAuthorized,
        _ => MqttReasonCode.UnspecifiedError,
    };

    private static PublishPacket DecodePublish(
        byte firstByte,
        ref MqttSequenceReader reader,
        MqttProtocolVersion version,
        int totalLen,
        bool poolPayload)
    {
        var dup = (firstByte & 0x08) != 0;
        var qos = (MqttQoS)((firstByte >> 1) & 0x03);
        var retain = (firstByte & 0x01) != 0;
        var topic = reader.ReadString();
        ushort packetId = 0;
        if (qos != MqttQoS.AtMostOnce)
        {
            packetId = reader.ReadUInt16BigEndian();
        }

        MqttPublishProperties? props = null;
        if (version == MqttProtocolVersion.V500)
        {
            var propLen = (int)reader.ReadVarInt(out _);
            if (propLen > 0)
            {
                props = ReadPublishProperties(ref reader, propLen, poolPayload);
            }
        }
        var payloadLen = totalLen - (int)reader.Consumed;
        if (poolPayload)
        {
            // Leave the payload as a zero-copy slice of the input buffer. The read loop copies it
            // into a pooled per-subscription buffer before advancing the pipe reader.
            return new PublishPacket
            {
                Topic = topic,
                PacketId = packetId,
                QoS = qos,
                Retain = retain,
                Duplicate = dup,
                Payload = reader.ReadSequence(payloadLen),
                Properties = props,
            };
        }
        var payload = new byte[payloadLen];
        reader.ReadSequence(payloadLen).CopyTo(payload);
        return new PublishPacket
        {
            Topic = topic,
            PacketId = packetId,
            QoS = qos,
            Retain = retain,
            Duplicate = dup,
            PayloadMemory = payload,
            Properties = props,
        };
    }

    private static MqttPublishProperties ReadPublishProperties(
        ref MqttSequenceReader reader,
        int propLen,
        bool sliceMode)
    {
        var end = reader.Consumed + propLen;
        byte? pfi = null;
        uint? me = null;
        ushort? ta = null;
        string? rt = null;
        ReadOnlyMemory<byte>? cd = null;
        string? ct = null;
        List<uint>? sids = null;
        List<MqttUserProperty>? ups = null;

        while (reader.Consumed < end)
        {
            var id = (MqttPropertyId)reader.ReadByte();
            switch (id)
            {
                case MqttPropertyId.PayloadFormatIndicator: pfi = reader.ReadByte(); break;
                case MqttPropertyId.MessageExpiryInterval: me = reader.ReadUInt32BigEndian(); break;
                case MqttPropertyId.TopicAlias: ta = reader.ReadUInt16BigEndian(); break;
                case MqttPropertyId.ResponseTopic: rt = reader.ReadString(); break;
                // In slice mode CorrelationData is a borrowed slice of the receive buffer (the read
                // loop copies it out for channel delivery); otherwise a standalone copy.
                case MqttPropertyId.CorrelationData:
                    cd = sliceMode ? reader.ReadBinaryDataMemory() : reader.ReadBinaryData();
                    break;
                case MqttPropertyId.ContentType: ct = reader.ReadString(); break;
                case MqttPropertyId.SubscriptionIdentifier: (sids ??= new List<uint>()).Add(
                    reader.ReadVarInt(out _)); break;
                case MqttPropertyId.UserProperty: (ups ??= new List<MqttUserProperty>()).Add(
                    new(reader.ReadString(), reader.ReadString())); break;
                default: throw new MqttProtocolException(
                    $"Unexpected PUBLISH property 0x{(byte)id:X2}");
            }
        }
        return new MqttPublishProperties
        {
            PayloadFormatIndicator = pfi,
            MessageExpiryInterval = me,
            TopicAlias = ta,
            ResponseTopic = rt,
            CorrelationData = cd,
            ContentType = ct,
            SubscriptionIdentifiers = sids,
            UserProperties = ups,
        };
    }

    private static PubAckPacket DecodeAck(
        ref MqttSequenceReader reader,
        MqttProtocolVersion version,
        int totalLen)
    {
        var pid = reader.ReadUInt16BigEndian();
        var rc = MqttReasonCode.Success;
        string? reason = null;
        List<MqttUserProperty>? ups = null;
        if (version == MqttProtocolVersion.V500 && totalLen >= 3)
        {
            rc = (MqttReasonCode)reader.ReadByte();
            if (totalLen > 3 && reader.Remaining > 0)
            {
                var propLen = (int)reader.ReadVarInt(out _);
                var end = reader.Consumed + propLen;
                while (reader.Consumed < end)
                {
                    var id = (MqttPropertyId)reader.ReadByte();
                    switch (id)
                    {
                        case MqttPropertyId.ReasonString: reason = reader.ReadString(); break;
                        case MqttPropertyId.UserProperty: (ups ??= new List<MqttUserProperty>())
                            .Add(
                                new(reader.ReadString(), reader.ReadString())); break;
                        default: throw new MqttProtocolException(
                            $"Unexpected ack property 0x{(byte)id:X2}");
                    }
                }
            }
        }
        return new PubAckPacket {
            PacketId = pid,
            ReasonCode = rc,
            ReasonString = reason,
            UserProperties = ups };
    }

    private static SubAckPacket DecodeSubAck(
        ref MqttSequenceReader reader,
        MqttProtocolVersion version,
        int totalLen)
    {
        var pid = reader.ReadUInt16BigEndian();
        string? reason = null;
        List<MqttUserProperty>? ups = null;
        if (version == MqttProtocolVersion.V500)
        {
            var propLen = (int)reader.ReadVarInt(out _);
            var end = reader.Consumed + propLen;
            while (reader.Consumed < end)
            {
                var id = (MqttPropertyId)reader.ReadByte();
                switch (id)
                {
                    case MqttPropertyId.ReasonString: reason = reader.ReadString(); break;
                    case MqttPropertyId.UserProperty: (ups ??= new List<MqttUserProperty>()).Add(
                        new(reader.ReadString(), reader.ReadString())); break;
                    default: throw new MqttProtocolException(
                        $"Unexpected SUBACK property 0x{(byte)id:X2}");
                }
            }
        }
        var rcCount = totalLen - (int)reader.Consumed;
        var rcs = new MqttReasonCode[rcCount];
        for (var i = 0; i < rcCount; i++) rcs[i] = (MqttReasonCode)reader.ReadByte();
        return new SubAckPacket {
            PacketId = pid,
            ReasonCodes = rcs,
            ReasonString = reason,
            UserProperties = ups };
    }

    private static UnsubAckPacket DecodeUnsubAck(
        ref MqttSequenceReader reader,
        MqttProtocolVersion version,
        int totalLen)
    {
        var pid = reader.ReadUInt16BigEndian();
        if (version == MqttProtocolVersion.V311)
        {
            return new UnsubAckPacket { PacketId = pid };
        }
        string? reason = null;
        List<MqttUserProperty>? ups = null;
        var propLen = (int)reader.ReadVarInt(out _);
        var end = reader.Consumed + propLen;
        while (reader.Consumed < end)
        {
            var id = (MqttPropertyId)reader.ReadByte();
            switch (id)
            {
                case MqttPropertyId.ReasonString: reason = reader.ReadString(); break;
                case MqttPropertyId.UserProperty: (ups ??= new List<MqttUserProperty>()).Add(
                    new(reader.ReadString(), reader.ReadString())); break;
                default: throw new MqttProtocolException(
                    $"Unexpected UNSUBACK property 0x{(byte)id:X2}");
            }
        }
        var rcCount = totalLen - (int)reader.Consumed;
        var rcs = new MqttReasonCode[rcCount];
        for (var i = 0; i < rcCount; i++) rcs[i] = (MqttReasonCode)reader.ReadByte();
        return new UnsubAckPacket {
            PacketId = pid,
            ReasonCodes = rcs,
            ReasonString = reason,
            UserProperties = ups };
    }

    private static DisconnectPacket DecodeDisconnect(
        ref MqttSequenceReader reader,
        MqttProtocolVersion version,
        int totalLen)
    {
        if (version == MqttProtocolVersion.V311 || totalLen == 0)
        {
            return new DisconnectPacket();
        }
        var rc = (MqttReasonCode)reader.ReadByte();
        string? reason = null, serverRef = null;
        uint? se = null;
        List<MqttUserProperty>? ups = null;
        if (totalLen > 1 && reader.Remaining > 0)
        {
            var propLen = (int)reader.ReadVarInt(out _);
            var end = reader.Consumed + propLen;
            while (reader.Consumed < end)
            {
                var id = (MqttPropertyId)reader.ReadByte();
                switch (id)
                {
                    case MqttPropertyId.SessionExpiryInterval: se = reader
                        .ReadUInt32BigEndian(); break;
                    case MqttPropertyId.ReasonString: reason = reader.ReadString(); break;
                    case MqttPropertyId.ServerReference: serverRef = reader.ReadString(); break;
                    case MqttPropertyId.UserProperty: (ups ??= new List<MqttUserProperty>()).Add(
                        new(reader.ReadString(), reader.ReadString())); break;
                    default: throw new MqttProtocolException(
                        $"Unexpected DISCONNECT property 0x{(byte)id:X2}");
                }
            }
        }
        return new DisconnectPacket {
            ReasonCode = rc,
            ReasonString = reason,
            ServerReference = serverRef,
            SessionExpiryInterval = se,
            UserProperties = ups };
    }

    private static AuthPacket DecodeAuth(ref MqttSequenceReader reader, int totalLen)
    {
        if (totalLen == 0) return new AuthPacket { ReasonCode = MqttReasonCode.Success };
        var rc = (MqttReasonCode)reader.ReadByte();
        string? method = null, reason = null;
        byte[]? data = null;
        List<MqttUserProperty>? ups = null;
        if (totalLen > 1 && reader.Remaining > 0)
        {
            var propLen = (int)reader.ReadVarInt(out _);
            var end = reader.Consumed + propLen;
            while (reader.Consumed < end)
            {
                var id = (MqttPropertyId)reader.ReadByte();
                switch (id)
                {
                    case MqttPropertyId.AuthenticationMethod: method = reader.ReadString(); break;
                    case MqttPropertyId.AuthenticationData: data = reader.ReadBinaryData(); break;
                    case MqttPropertyId.ReasonString: reason = reader.ReadString(); break;
                    case MqttPropertyId.UserProperty: (ups ??= new List<MqttUserProperty>()).Add(
                        new(reader.ReadString(), reader.ReadString())); break;
                    default: throw new MqttProtocolException(
                        $"Unexpected AUTH property 0x{(byte)id:X2}");
                }
            }
        }
        return new AuthPacket {
            ReasonCode = rc,
            AuthenticationMethod = method,
            AuthenticationData = data,
            ReasonString = reason,
            UserProperties = ups };
    }

    /// <summary>
    /// Mutable builder used while parsing a CONNACK to avoid an allocation per property.
    /// </summary>
    private struct ConnAckBuilder
    {
        public bool SessionPresent;
        public MqttReasonCode ReasonCode;
        public uint? SessionExpiryInterval;
        public ushort? ReceiveMaximum;
        public MqttQoS MaximumQoS;
        public bool RetainAvailable;
        public uint? MaximumPacketSize;
        public string? AssignedClientId;
        public ushort? TopicAliasMaximum;
        public string? ReasonString;
        public bool WildcardSubscriptionAvailable;
        public bool SubscriptionIdentifiersAvailable;
        public bool SharedSubscriptionAvailable;
        public ushort? ServerKeepAlive;
        public string? ResponseInformation;
        public string? ServerReference;
        public string? AuthenticationMethod;
        public byte[]? AuthenticationData;
        private List<MqttUserProperty>? _userProps;

        public ConnAckBuilder()
        {
            MaximumQoS = MqttQoS.ExactlyOnce;
            RetainAvailable = true;
            WildcardSubscriptionAvailable = true;
            SubscriptionIdentifiersAvailable = true;
            SharedSubscriptionAvailable = true;
        }

        public void AddUserProperty(string name, string value)
        {
            (_userProps ??= new List<MqttUserProperty>()).Add(new MqttUserProperty(name, value));
        }

        public ConnAckPacket Build() => new()
        {
            SessionPresent = SessionPresent,
            ReasonCode = ReasonCode,
            SessionExpiryInterval = SessionExpiryInterval,
            ReceiveMaximum = ReceiveMaximum,
            MaximumQoS = MaximumQoS,
            RetainAvailable = RetainAvailable,
            MaximumPacketSize = MaximumPacketSize,
            AssignedClientId = AssignedClientId,
            TopicAliasMaximum = TopicAliasMaximum,
            ReasonString = ReasonString,
            WildcardSubscriptionAvailable = WildcardSubscriptionAvailable,
            SubscriptionIdentifiersAvailable = SubscriptionIdentifiersAvailable,
            SharedSubscriptionAvailable = SharedSubscriptionAvailable,
            ServerKeepAlive = ServerKeepAlive,
            ResponseInformation = ResponseInformation,
            ServerReference = ServerReference,
            AuthenticationMethod = AuthenticationMethod,
            AuthenticationData = AuthenticationData,
            UserProperties = _userProps,
        };
    }
}
