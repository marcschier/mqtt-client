// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Collections.Generic;
using Mqtt.Client.Buffers;
using Mqtt.Client.Protocol.Packets;

namespace Mqtt.Client.Protocol;

/// <summary>Stateless encoder for MQTT control packets (3.1.1 and 5.0).</summary>
internal static class MqttPacketEncoder
{
    private const byte CleanStartFlag = 0x02;
    private const byte WillFlag = 0x04;
    private const int WillQoSShift = 3;
    private const byte WillRetainFlag = 0x20;
    private const byte PasswordFlag = 0x40;
    private const byte UsernameFlag = 0x80;

    public static void EncodeConnect(ConnectPacket packet, MqttBufferWriter writer)
    {
        var v5 = packet.ProtocolVersion == MqttProtocolVersion.V500;
        const byte firstByte = (byte)((byte)MqttPacketType.Connect << 4);
        var hdrOffset = writer.WriteFixedHeaderStart(firstByte);
        var payloadStart = writer.WrittenCount;

        writer.WriteString("MQTT");
        writer.WriteByte(v5 ? (byte)5 : (byte)4);

        byte flags = 0;
        if (packet.CleanStart) flags |= CleanStartFlag;
        if (packet.Will is not null)
        {
            flags |= WillFlag;
            flags |= (byte)((byte)packet.Will.QoS << WillQoSShift);
            if (packet.Will.Retain) flags |= WillRetainFlag;
        }
        if (packet.Username is not null) flags |= UsernameFlag;
        if (packet.Password is not null) flags |= PasswordFlag;
        writer.WriteByte(flags);
        writer.WriteUInt16BigEndian(packet.KeepAliveSeconds);

        if (v5)
        {
            var props = new MqttBufferWriter(64); try {
            if (packet.SessionExpiryInterval is { } se) { props.WriteByte(
                (byte)MqttPropertyId.SessionExpiryInterval); props
                .WriteUInt32BigEndian(se); }
            if (packet.ReceiveMaximum is { } rm) { props.WriteByte(
                (byte)MqttPropertyId.ReceiveMaximum); props.WriteUInt16BigEndian(rm); }
            if (packet.MaximumPacketSize is { } mp) { props.WriteByte(
                (byte)MqttPropertyId.MaximumPacketSize); props.WriteUInt32BigEndian(
                mp); }
            if (packet.TopicAliasMaximum is { } ta) { props.WriteByte(
                (byte)MqttPropertyId.TopicAliasMaximum); props.WriteUInt16BigEndian(
                ta); }
            if (packet.RequestResponseInformation is { } rri) { props.WriteByte(
                (byte)MqttPropertyId.RequestResponseInformation); props
                .WriteByte((byte)(rri ? 1 : 0)); }
            if (packet.RequestProblemInformation is { } rpi) { props.WriteByte(
                (byte)MqttPropertyId.RequestProblemInformation); props
                .WriteByte((byte)(rpi ? 1 : 0)); }
            if (packet.AuthenticationMethod is { } am) { props.WriteByte(
                (byte)MqttPropertyId.AuthenticationMethod); props.WriteString(
                am); }
            if (packet.AuthenticationData is { } ad) { props.WriteByte(
                (byte)MqttPropertyId.AuthenticationData); props.WriteBinaryData(
                ad); }
            WriteUserProperties(props, packet.UserProperties);
            writer.WriteVarInt((uint)props.WrittenCount);
            writer.WriteBytes(props.WrittenSpan);
            } finally { props.Dispose(); }
        }

        writer.WriteString(packet.ClientId);
        if (packet.Will is not null)
        {
            if (v5)
            {
                var wp = new MqttBufferWriter(32); try {
                if (packet.Will.DelayIntervalSeconds is { } d) { wp.WriteByte(
                    (byte)MqttPropertyId.WillDelayInterval); wp
                    .WriteUInt32BigEndian(d); }
                if (packet.Will.Properties is { } wprops) WritePublishProperties(wp, wprops);
                writer.WriteVarInt((uint)wp.WrittenCount);
                writer.WriteBytes(wp.WrittenSpan);
            } finally { wp.Dispose(); }
            }
            writer.WriteString(packet.Will.Topic);
            writer.WriteBinaryData(packet.Will.Payload.Span);
        }
        if (packet.Username is not null) writer.WriteString(packet.Username);
        if (packet.Password is not null) writer.WriteBinaryData(packet.Password);

        writer.PatchRemainingLength(hdrOffset, writer.WrittenCount - payloadStart);
    }

    public static void EncodePublish(
        PublishPacket packet,
        MqttProtocolVersion version,
        MqttBufferWriter writer)
        => EncodePublishCore(packet, version, writer, includePayload: true);

    /// <summary>
    /// Encodes only the fixed + variable header of a PUBLISH (omitting the payload bytes); the
    /// remaining-length field is patched as if the payload had been written. Use together with a
    /// vectored write of the actual payload to avoid copying large payloads through the writer.
    /// </summary>
    public static void EncodePublishHeader(
        PublishPacket packet,
        MqttProtocolVersion version,
        MqttBufferWriter writer)
        => EncodePublishCore(packet, version, writer, includePayload: false);

    private static void EncodePublishCore(
        PublishPacket packet,
        MqttProtocolVersion version,
        MqttBufferWriter writer,
        bool includePayload)
    {
        var v5 = version == MqttProtocolVersion.V500;
        byte firstByte = (byte)((byte)MqttPacketType.Publish << 4);
        if (packet.Duplicate) firstByte |= 0x08;
        firstByte |= (byte)((byte)packet.QoS << 1);
        if (packet.Retain) firstByte |= 0x01;

        var hdrOffset = writer.WriteFixedHeaderStart(firstByte);
        var headerStart = writer.WrittenCount;

        writer.WriteString(packet.Topic);
        if (packet.QoS != MqttQoS.AtMostOnce)
        {
            writer.WriteUInt16BigEndian(packet.PacketId);
        }
        if (v5)
        {
            var props = new MqttBufferWriter(32); try {
            if (packet.Properties is { } pp) WritePublishProperties(props, pp);
            writer.WriteVarInt((uint)props.WrittenCount);
            writer.WriteBytes(props.WrittenSpan);
            } finally { props.Dispose(); }
        }

        var headerLen = writer.WrittenCount - headerStart;
        if (includePayload)
        {
            writer.WriteBytes(packet.Payload.Span);
            writer.PatchRemainingLength(hdrOffset, writer.WrittenCount - headerStart);
        }
        else
        {
            // checked: caller-supplied Payload.Length is untrusted; PatchRemainingLength also
            // enforces the MQTT 268_435_455 cap, but defending in depth here as well.
            writer.PatchRemainingLength(hdrOffset, checked(headerLen + packet.Payload.Length));
        }
    }

    private static void WritePublishProperties(MqttBufferWriter p, MqttPublishProperties pp)
    {
        if (pp.PayloadFormatIndicator is { } pfi) { p.WriteByte(
            (byte)MqttPropertyId.PayloadFormatIndicator); p.WriteByte(pfi); }
        if (pp.MessageExpiryInterval is { } me) { p.WriteByte(
            (byte)MqttPropertyId.MessageExpiryInterval); p.WriteUInt32BigEndian(me); }
        if (pp.TopicAlias is { } ta) { p.WriteByte((byte)MqttPropertyId.TopicAlias); p
            .WriteUInt16BigEndian(ta); }
        if (pp.ResponseTopic is { } rt) { p.WriteByte((byte)MqttPropertyId.ResponseTopic); p
            .WriteString(rt); }
        if (pp.CorrelationData is { } cd) { p.WriteByte((byte)MqttPropertyId.CorrelationData); p
            .WriteBinaryData(cd.Span); }
        if (pp.ContentType is { } ct) { p.WriteByte((byte)MqttPropertyId.ContentType); p
            .WriteString(ct); }
        if (pp.SubscriptionIdentifiers is { } sids)
        {
            foreach (var id in sids) { p.WriteByte((byte)MqttPropertyId.SubscriptionIdentifier); p
                .WriteVarInt(id); }
        }
        WriteUserProperties(p, pp.UserProperties);
    }

    public static void EncodePubAck(
        PubAckPacket packet,
        MqttProtocolVersion version,
        MqttBufferWriter writer)
        => EncodeAckLike(
            MqttPacketType.PubAck,
            packet.PacketId,
            packet.ReasonCode,
            packet.ReasonString,
            packet.UserProperties,
            version,
            writer);
    public static void EncodePubRec(
        ushort packetId,
        MqttReasonCode rc,
        MqttProtocolVersion v,
        MqttBufferWriter w)
        => EncodeAckLike(MqttPacketType.PubRec, packetId, rc, null, null, v, w);
    public static void EncodePubRel(
        ushort packetId,
        MqttReasonCode rc,
        MqttProtocolVersion v,
        MqttBufferWriter w)
        => EncodeAckLike(
            MqttPacketType.PubRel,
            packetId,
            rc,
            null,
            null,
            v,
            w,
            fixedHeaderFlags: 0x02);
    public static void EncodePubComp(
        ushort packetId,
        MqttReasonCode rc,
        MqttProtocolVersion v,
        MqttBufferWriter w)
        => EncodeAckLike(MqttPacketType.PubComp, packetId, rc, null, null, v, w);

    private static void EncodeAckLike(
        MqttPacketType type,
        ushort packetId,
        MqttReasonCode rc,
        string? reasonString,
        IReadOnlyList<MqttUserProperty>? userProps,
        MqttProtocolVersion version,
        MqttBufferWriter writer,
        byte fixedHeaderFlags = 0)
    {
        var v5 = version == MqttProtocolVersion.V500;
        var firstByte = (byte)(((byte)type << 4) | fixedHeaderFlags);
        var hdrOffset = writer.WriteFixedHeaderStart(firstByte);
        var payloadStart = writer.WrittenCount;
        writer.WriteUInt16BigEndian(packetId);
        if (v5)
        {
            writer.WriteByte((byte)rc);
            var props = new MqttBufferWriter(16);
            try
            {
                if (!string.IsNullOrEmpty(reasonString)) { props.WriteByte(
                    (byte)MqttPropertyId.ReasonString); props.WriteString(
                    reasonString!); }
                WriteUserProperties(props, userProps);
                if (props.WrittenCount > 0)
                {
                    writer.WriteVarInt((uint)props.WrittenCount);
                    writer.WriteBytes(props.WrittenSpan);
                }
            }
            finally { props.Dispose(); }
        }
        writer.PatchRemainingLength(hdrOffset, writer.WrittenCount - payloadStart);
    }

    public static void EncodeSubscribe(
        SubscribePacket packet,
        MqttProtocolVersion version,
        MqttBufferWriter writer)
    {
        var v5 = version == MqttProtocolVersion.V500;
        const byte firstByte = (byte)(((byte)MqttPacketType.Subscribe << 4) | 0x02);
        var hdrOffset = writer.WriteFixedHeaderStart(firstByte);
        var payloadStart = writer.WrittenCount;

        writer.WriteUInt16BigEndian(packet.PacketId);
        if (v5)
        {
            var props = new MqttBufferWriter(16); try {
            if (packet.SubscriptionIdentifier is { } sid) { props.WriteByte(
                (byte)MqttPropertyId.SubscriptionIdentifier); props.WriteVarInt(
                sid); }
            WriteUserProperties(props, packet.UserProperties);
            writer.WriteVarInt((uint)props.WrittenCount);
            writer.WriteBytes(props.WrittenSpan);
            } finally { props.Dispose(); }
        }
        foreach (var f in packet.Filters)
        {
            writer.WriteString(f.Topic);
            byte opts = (byte)f.QoS;
            if (v5)
            {
                if (f.NoLocal) opts |= 0x04;
                if (f.RetainAsPublished) opts |= 0x08;
                opts |= (byte)((byte)f.RetainHandling << 4);
            }
            writer.WriteByte(opts);
        }
        writer.PatchRemainingLength(hdrOffset, writer.WrittenCount - payloadStart);
    }

    public static void EncodeUnsubscribe(
        UnsubscribePacket packet,
        MqttProtocolVersion version,
        MqttBufferWriter writer)
    {
        var v5 = version == MqttProtocolVersion.V500;
        const byte firstByte = (byte)(((byte)MqttPacketType.Unsubscribe << 4) | 0x02);
        var hdrOffset = writer.WriteFixedHeaderStart(firstByte);
        var payloadStart = writer.WrittenCount;
        writer.WriteUInt16BigEndian(packet.PacketId);
        if (v5)
        {
            var props = new MqttBufferWriter(16); try {
            WriteUserProperties(props, packet.UserProperties);
            writer.WriteVarInt((uint)props.WrittenCount);
            writer.WriteBytes(props.WrittenSpan);
            } finally { props.Dispose(); }
        }
        foreach (var t in packet.Topics) writer.WriteString(t);
        writer.PatchRemainingLength(hdrOffset, writer.WrittenCount - payloadStart);
    }

    public static void EncodePingReq(MqttBufferWriter writer)
    {
        writer.WriteByte((byte)((byte)MqttPacketType.PingReq << 4));
        writer.WriteByte(0);
    }

    public static void EncodeDisconnect(
        DisconnectPacket packet,
        MqttProtocolVersion version,
        MqttBufferWriter writer)
    {
        var v5 = version == MqttProtocolVersion.V500;
        const byte firstByte = (byte)((byte)MqttPacketType.Disconnect << 4);
        if (!v5)
        {
            writer.WriteByte(firstByte);
            writer.WriteByte(0);
            return;
        }
        var hdrOffset = writer.WriteFixedHeaderStart(firstByte);
        var payloadStart = writer.WrittenCount;
        writer.WriteByte((byte)packet.ReasonCode);
        var props = new MqttBufferWriter(16);
        try
        {
            if (packet.SessionExpiryInterval is { } se) { props.WriteByte(
                (byte)MqttPropertyId.SessionExpiryInterval); props
                .WriteUInt32BigEndian(se); }
            if (!string.IsNullOrEmpty(packet.ReasonString)) { props.WriteByte(
                (byte)MqttPropertyId.ReasonString); props.WriteString(
                packet.ReasonString!); }
            if (!string.IsNullOrEmpty(packet.ServerReference)) { props.WriteByte(
                (byte)MqttPropertyId.ServerReference); props.WriteString(
                packet.ServerReference!); }
            WriteUserProperties(props, packet.UserProperties);
            if (props.WrittenCount > 0)
            {
                writer.WriteVarInt((uint)props.WrittenCount);
                writer.WriteBytes(props.WrittenSpan);
            }
        }
        finally { props.Dispose(); }
        writer.PatchRemainingLength(hdrOffset, writer.WrittenCount - payloadStart);
    }

    public static void EncodeAuth(AuthPacket packet, MqttBufferWriter writer)
    {
        const byte firstByte = (byte)((byte)MqttPacketType.Auth << 4);
        var hdrOffset = writer.WriteFixedHeaderStart(firstByte);
        var payloadStart = writer.WrittenCount;
        writer.WriteByte((byte)packet.ReasonCode);
        var props = new MqttBufferWriter(32); try {
        if (packet.AuthenticationMethod is { } am) { props.WriteByte(
            (byte)MqttPropertyId.AuthenticationMethod); props.WriteString(am); }
        if (packet.AuthenticationData is { } ad) { props.WriteByte(
            (byte)MqttPropertyId.AuthenticationData); props.WriteBinaryData(ad); }
        if (!string.IsNullOrEmpty(packet.ReasonString)) { props.WriteByte(
            (byte)MqttPropertyId.ReasonString); props.WriteString(
            packet.ReasonString!); }
        WriteUserProperties(props, packet.UserProperties);
        writer.WriteVarInt((uint)props.WrittenCount);
        writer.WriteBytes(props.WrittenSpan);
            } finally { props.Dispose(); }
        writer.PatchRemainingLength(hdrOffset, writer.WrittenCount - payloadStart);
    }

    private static void WriteUserProperties(
        MqttBufferWriter p,
        IReadOnlyList<MqttUserProperty>? userProps)
    {
        if (userProps is null) return;
        for (var i = 0; i < userProps.Count; i++)
        {
            p.WriteByte((byte)MqttPropertyId.UserProperty);
            p.WriteString(userProps[i].Name);
            p.WriteString(userProps[i].Value);
        }
    }
}
