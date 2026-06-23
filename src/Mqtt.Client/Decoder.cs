// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
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

        uint remainingLength;
        int lenBytes;
        var single = buffer.IsSingleSegment;
        // For a single-segment sequence FirstSpan is the whole buffer; capture it once and reuse it
        // for the fixed-header read and the PUBLISH fast path (FirstSpan/IsSingleSegment each decode
        // a SequencePosition, so we avoid repeating those accesses).
        var segment = single ? buffer.FirstSpan : default;
        if (single)
        {
            firstByte = segment[0];
            if (!TryReadVarIntSpan(segment, 1, out remainingLength, out lenBytes))
            {
                return false;
            }
        }
        else
        {
            var reader = new MqttSequenceReader(buffer);
            firstByte = reader.ReadByte();
            remainingLength = TryReadVarInt(ref reader, out lenBytes, out var lenSuccess);
            if (!lenSuccess) return false;
        }

        if (remainingLength > (uint)maxPacketSize)
        {
            throw new MqttProtocolException(
                $"Incoming packet remaining length ({remainingLength}) exceeds " +
                $"MaxIncomingPacketSize ({maxPacketSize}).");
        }

        var fixedHeaderBytes = 1 + lenBytes;
        if (buffer.Length < fixedHeaderBytes + remainingLength) return false;
        var type = (MqttPacketType)(firstByte >> 4);

        // Fast path: a PUBLISH that lives in a single contiguous buffer (the overwhelmingly common
        // case) is decoded straight from the segment span, skipping the second MqttSequenceReader
        // and the ReadOnlySequence.Slice that the general path constructs for the body.
        if (type == MqttPacketType.Publish && single)
        {
            packet = DecodePublishSingleSegment(
                firstByte, segment, buffer, fixedHeaderBytes, (int)remainingLength, version,
                poolPayload);
            consumed = buffer.GetPosition(fixedHeaderBytes + remainingLength);
            return true;
        }

        var payload = buffer.Slice(fixedHeaderBytes, remainingLength);
        var payloadReader = new MqttSequenceReader(payload);

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

    private static bool TryReadVarIntSpan(
        ReadOnlySpan<byte> span,
        int start,
        out uint value,
        out int byteCount)
    {
        value = 0;
        byteCount = 0;
        var multiplier = 1u;
        var i = start;
        while (true)
        {
            if (i >= span.Length)
            {
                return false;
            }
            var b = span[i++];
            byteCount++;
            value += (uint)(b & 0x7F) * multiplier;
            if ((b & 0x80) == 0)
            {
                return true;
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
#if NETSTANDARD2_1
        var payload = new byte[payloadLen];
#else
        // Skip the redundant zero-init: every byte is overwritten by the copy that follows.
        var payload = GC.AllocateUninitializedArray<byte>(payloadLen);
#endif
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

    private static PublishPacket DecodePublishSingleSegment(
        byte firstByte,
        ReadOnlySpan<byte> segment,
        in ReadOnlySequence<byte> buffer,
        int bodyOffset,
        int bodyLen,
        MqttProtocolVersion version,
        bool poolPayload)
    {
        var body = segment.Slice(bodyOffset, bodyLen);
        var qos = (MqttQoS)((firstByte >> 1) & 0x03);
        var pos = 0;

        var topicLen = ReadUInt16Span(body, ref pos);
        EnsureSpan(body, pos, topicLen);
        var topic = topicLen == 0
            ? string.Empty
            : Encoding.UTF8.GetString(body.Slice(pos, topicLen));
        pos += topicLen;

        ushort packetId = 0;
        if (qos != MqttQoS.AtMostOnce)
        {
            packetId = ReadUInt16Span(body, ref pos);
        }

        MqttPublishProperties? props = null;
        if (version == MqttProtocolVersion.V500)
        {
            var propLen = (int)ReadVarIntSpan(body, ref pos);
            if (propLen > 0)
            {
                EnsureSpan(body, pos, propLen);
                // Properties are comparatively rare; reuse the single-sourced sequence-based reader
                // over a sub-sequence bounded to the property block.
                var propReader = new MqttSequenceReader(buffer.Slice(bodyOffset + pos, propLen));
                props = ReadPublishProperties(ref propReader, propLen, poolPayload);
                pos += propLen;
            }
        }

        var payloadLen = bodyLen - pos;
        var retain = (firstByte & 0x01) != 0;
        var dup = (firstByte & 0x08) != 0;
        if (poolPayload)
        {
            // Zero-copy: leave the payload as a slice of the input buffer (the read loop copies it
            // into a pooled buffer before advancing the pipe reader).
            return new PublishPacket
            {
                Topic = topic,
                PacketId = packetId,
                QoS = qos,
                Retain = retain,
                Duplicate = dup,
                Payload = buffer.Slice(bodyOffset + pos, payloadLen),
                Properties = props,
            };
        }
#if NETSTANDARD2_1
        var payload = new byte[payloadLen];
#else
        var payload = GC.AllocateUninitializedArray<byte>(payloadLen);
#endif
        body.Slice(pos, payloadLen).CopyTo(payload);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadUInt16Span(ReadOnlySpan<byte> span, ref int pos)
    {
        if ((uint)(pos + 2) > (uint)span.Length)
        {
            throw new MqttProtocolException("Unexpected end of packet.");
        }
        var value = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(pos));
        pos += 2;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadVarIntSpan(ReadOnlySpan<byte> span, ref int pos)
    {
        // Hot path: a single-byte value (0..127) — covers property-length 0 and small lengths.
        if ((uint)pos < (uint)span.Length)
        {
            var b0 = span[pos];
            if ((b0 & 0x80) == 0)
            {
                pos++;
                return b0;
            }
        }
        return ReadVarIntSpanSlow(span, ref pos);
    }

    private static uint ReadVarIntSpanSlow(ReadOnlySpan<byte> span, ref int pos)
    {
        uint value = 0;
        var multiplier = 1u;
        while (true)
        {
            if ((uint)pos >= (uint)span.Length)
            {
                throw new MqttProtocolException("Unexpected end of packet.");
            }
            var b = span[pos++];
            value += (uint)(b & 0x7F) * multiplier;
            if ((b & 0x80) == 0)
            {
                return value;
            }
            multiplier *= 128;
            if (multiplier > 128 * 128 * 128)
            {
                throw new MqttProtocolException("Malformed variable byte integer.");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureSpan(ReadOnlySpan<byte> span, int pos, int length)
    {
        if (length < 0 || (uint)(pos + length) > (uint)span.Length)
        {
            throw new MqttProtocolException("Unexpected end of packet.");
        }
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
        // Subscription identifiers are usually 0 or 1; avoid the List allocation for the 1 case.
        uint firstSid = 0;
        var hasSid = false;
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
                case MqttPropertyId.SubscriptionIdentifier:
                    var sid = reader.ReadVarInt(out _);
                    if (sids is not null) { sids.Add(sid); }
                    else if (hasSid) { sids = new List<uint> { firstSid, sid }; }
                    else { firstSid = sid; hasSid = true; }
                    break;
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
            SubscriptionIdentifiers = (IReadOnlyList<uint>?)sids
                ?? (hasSid ? new[] { firstSid } : null),
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
