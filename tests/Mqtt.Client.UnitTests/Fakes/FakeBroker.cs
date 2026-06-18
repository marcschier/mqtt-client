// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
namespace Mqtt.Client.UnitTests;

/// <summary>
/// Helper that drives a <see cref="FakePipeTransport"/> as if it were an MQTT broker. Tests
/// call <see cref="ReadPacketAsync"/> to consume what the client sent, then write canned
/// responses via the typed <c>Send*</c> methods.
/// </summary>
internal sealed class FakeBroker
{
    private readonly FakePipeTransport _transport;
    private readonly MqttProtocolVersion _version;

    public FakeBroker(
        FakePipeTransport transport,
        MqttProtocolVersion version = MqttProtocolVersion.V500)
    {
        _transport = transport;
        _version = version;
    }

    /// <summary>
    /// Reads and decodes the next AUTH packet sent by the client (also handles CONNECT).
    /// </summary>
    public async Task<object?> ReadDecodedPacketAsync(CancellationToken ct = default)
    {
        var reader = _transport.FromClient;
        while (true)
        {
            var result = await reader.ReadAsync(ct).ConfigureAwait(false);
            var buffer = result.Buffer;
            if (MqttPacketDecoder.TryDecode(
                buffer,
                _version,
                out var packet,
                out _,
                out var consumed))
            {
                reader.AdvanceTo(consumed);
                return packet;
            }
            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
            {
                throw new InvalidOperationException("Pipe closed before a packet arrived.");
            }
        }
    }

    public async Task<ClientPacket> ReadPacketAsync(CancellationToken ct = default)
    {
        var reader = _transport.FromClient;
        while (true)
        {
            var result = await reader.ReadAsync(ct).ConfigureAwait(false);
            var buffer = result.Buffer;
            if (TryParseClientPacket(buffer, out var packet, out var consumed))
            {
                reader.AdvanceTo(consumed);
                return packet;
            }
            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
            {
                throw new InvalidOperationException("Pipe closed before a full packet arrived.");
            }
        }
    }

    private static bool TryParseClientPacket(
        in ReadOnlySequence<byte> buffer,
        out ClientPacket packet,
        out SequencePosition consumed)
    {
        packet = default;
        consumed = buffer.Start;
        if (buffer.Length < 2) return false;

        var seqReader = new MqttSequenceReader(buffer);
        var firstByte = seqReader.ReadByte();
        var remaining = seqReader.ReadVarInt(out var lenBytes);
        if (seqReader.Remaining < remaining) return false;

        // Locate body slice for selected packets that need packetId or topic.
        var headerBytes = 1 + lenBytes;
        var bodySlice = buffer.Slice(headerBytes, remaining);
        var bodyReader = new MqttSequenceReader(bodySlice);

        ushort packetId = 0;
        var type = (firstByte >> 4) & 0x0F;
        switch (type)
        {
            case 3:  // PUBLISH
                {
                    var topic = bodyReader.ReadString();
                    var qos = (firstByte >> 1) & 0x03;
                    if (qos > 0)
                    {
                        packetId = bodyReader.ReadUInt16BigEndian();
                    }
                    packet = new ClientPacket(firstByte, packetId, topic);
                    break;
                }
            case 4: case 5: case 6: case 7:  // PUBACK / PUBREC / PUBREL / PUBCOMP
            case 8: case 10: // SUBSCRIBE / UNSUBSCRIBE
                packetId = bodyReader.ReadUInt16BigEndian();
                packet = new ClientPacket(firstByte, packetId, null);
                break;
            default:
                packet = new ClientPacket(firstByte, 0, null);
                break;
        }
        consumed = buffer.GetPosition(headerBytes + remaining);
        return true;
    }

    public readonly record struct ClientPacket(byte FirstByte, ushort PacketId, string? Topic)
    {
        public int Type => (FirstByte >> 4) & 0x0F;
        public Mqtt.Client.MqttQoS QoS => (Mqtt.Client.MqttQoS)((FirstByte >> 1) & 0x03);
    }

    public async Task SendConnAckAsync(
        MqttReasonCode rc = MqttReasonCode.Success,
        bool sessionPresent = false,
        CancellationToken ct = default)
    {
        // Build a minimal CONNACK on the wire (our codec encodes its own packets; manual layout here).
        using var w = new MqttBufferWriter(8);
        w.WriteByte(0x20);                 // CONNACK packet type
        if (_version == MqttProtocolVersion.V500)
        {
            w.WriteByte(3);                // remaining length
            w.WriteByte((byte)(sessionPresent ? 1 : 0));
            w.WriteByte((byte)rc);
            w.WriteByte(0);                // properties length 0
        }
        else
        {
            w.WriteByte(2);
            w.WriteByte((byte)(sessionPresent ? 1 : 0));
            w.WriteByte((byte)rc);
        }
        await SendBytesAsync(w.WrittenMemory, ct).ConfigureAwait(false);
    }

    public Task SendPubAckAsync(
        ushort packetId,
        MqttReasonCode rc = MqttReasonCode.Success,
        CancellationToken ct = default)
        => SendAckLikeAsync(0x40, packetId, rc, ct);
    public Task SendPubRecAsync(ushort packetId, CancellationToken ct = default)
        => SendAckLikeAsync(0x50, packetId, MqttReasonCode.Success, ct);
    public Task SendPubCompAsync(ushort packetId, CancellationToken ct = default)
        => SendAckLikeAsync(0x70, packetId, MqttReasonCode.Success, ct);

    public async Task SendSubAckAsync(
        ushort packetId,
        MqttReasonCode rc,
        CancellationToken ct = default)
    {
        using var w = new MqttBufferWriter(8);
        w.WriteByte(0x90);
        if (_version == MqttProtocolVersion.V500)
        {
            w.WriteByte(4);
            w.WriteUInt16BigEndian(packetId);
            w.WriteByte(0);                // properties length
            w.WriteByte((byte)rc);
        }
        else
        {
            w.WriteByte(3);
            w.WriteUInt16BigEndian(packetId);
            w.WriteByte((byte)rc);
        }
        await SendBytesAsync(w.WrittenMemory, ct).ConfigureAwait(false);
    }

    public async Task SendUnsubAckAsync(ushort packetId, CancellationToken ct = default)
    {
        using var w = new MqttBufferWriter(8);
        w.WriteByte(0xB0);
        if (_version == MqttProtocolVersion.V500)
        {
            w.WriteByte(4);
            w.WriteUInt16BigEndian(packetId);
            w.WriteByte(0);
            w.WriteByte((byte)MqttReasonCode.Success);
        }
        else
        {
            w.WriteByte(2);
            w.WriteUInt16BigEndian(packetId);
        }
        await SendBytesAsync(w.WrittenMemory, ct).ConfigureAwait(false);
    }

    public async Task SendPublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        MqttQoS qos = MqttQoS.AtMostOnce,
        ushort packetId = 0,
        CancellationToken ct = default)
    {
        var packet = new PublishPacket
        {
            Topic = topic,
            QoS = qos,
            PacketId = packetId,
            Payload = payload,
        };
        using var w = new MqttBufferWriter(payload.Length + 32);
        MqttPacketEncoder.EncodePublish(packet, _version, w);
        await SendBytesAsync(w.WrittenMemory, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// MQTT 5 variant: sends a PUBLISH with a TopicAlias property. Use to test the client's
    /// inbound alias table (caller passes the real topic on registration; empty topic to resolve).
    /// </summary>
    public async Task SendPublishWithAliasAsync(
        string topic,
        ushort alias,
        ReadOnlyMemory<byte> payload,
        MqttQoS qos = MqttQoS.AtMostOnce,
        ushort packetId = 0,
        CancellationToken ct = default)
    {
        var packet = new PublishPacket
        {
            Topic = topic,
            QoS = qos,
            PacketId = packetId,
            Payload = payload,
            Properties = new MqttPublishProperties { TopicAlias = alias },
        };
        using var w = new MqttBufferWriter(payload.Length + 32);
        MqttPacketEncoder.EncodePublish(packet, _version, w);
        await SendBytesAsync(w.WrittenMemory, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// MQTT 5 variant: sends a PUBLISH with one or more SubscriptionIdentifier properties.
    /// </summary>
    public async Task SendPublishWithSubIdsAsync(
        string topic,
        IReadOnlyList<uint> subscriptionIds,
        ReadOnlyMemory<byte> payload,
        MqttQoS qos = MqttQoS.AtMostOnce,
        ushort packetId = 0,
        CancellationToken ct = default)
    {
        var packet = new PublishPacket
        {
            Topic = topic,
            QoS = qos,
            PacketId = packetId,
            Payload = payload,
            Properties = new MqttPublishProperties { SubscriptionIdentifiers = subscriptionIds },
        };
        using var w = new MqttBufferWriter(payload.Length + 32);
        MqttPacketEncoder.EncodePublish(packet, _version, w);
        await SendBytesAsync(w.WrittenMemory, ct).ConfigureAwait(false);
    }

    public async Task SendPingRespAsync(CancellationToken ct = default)
    {
        var bytes = new byte[] { 0xD0, 0x00 };
        await SendBytesAsync(bytes, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an AUTH packet from broker to client.
    /// </summary>
    public async Task SendAuthAsync(
        MqttReasonCode rc,
        string method,
        ReadOnlyMemory<byte> data,
        CancellationToken ct = default)
    {
        var pkt = new AuthPacket
        {
            ReasonCode = rc,
            AuthenticationMethod = method,
            AuthenticationData = data.IsEmpty ? null : data.ToArray(),
        };
        using var w = new MqttBufferWriter(64 + data.Length);
        MqttPacketEncoder.EncodeAuth(pkt, w);
        await SendBytesAsync(w.WrittenMemory, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends CONNACK with optional AuthenticationMethod/Data for the auth flow.
    /// </summary>
    public async Task SendConnAckWithAuthAsync(
        MqttReasonCode rc,
        string? method = null,
        ReadOnlyMemory<byte> data = default,
        CancellationToken ct = default)
    {
        // CONNACK with v5 properties (method/data) — encode manually since the client codec is one-way.
        using var props = new MqttBufferWriter(64 + data.Length);
        if (method is not null)
        {
            props.WriteByte(0x15); // AuthenticationMethod
            props.WriteString(method);
        }
        if (!data.IsEmpty)
        {
            props.WriteByte(0x16); // AuthenticationData
            props.WriteBinaryData(data.Span);
        }
        var propsBytes = props.WrittenMemory;
        var propsLen = propsBytes.Length;

        using var w = new MqttBufferWriter(8 + propsLen);
        w.WriteByte(0x20);
        var remaining = 2 + VarIntSize(propsLen) + propsLen;
        w.WriteVarInt((uint)remaining);
        w.WriteByte(0); // session present
        w.WriteByte((byte)rc);
        w.WriteVarInt((uint)propsLen);
        if (propsLen > 0) w.WriteBytes(propsBytes.Span);
        await SendBytesAsync(w.WrittenMemory, ct).ConfigureAwait(false);
    }

    private static int VarIntSize(int value)
    {
        if (value < 128) return 1;
        if (value < 16384) return 2;
        if (value < 2097152) return 3;
        return 4;
    }

    private async Task SendAckLikeAsync(
        byte firstByte,
        ushort packetId,
        MqttReasonCode rc,
        CancellationToken ct)
    {
        using var w = new MqttBufferWriter(8);
        w.WriteByte(firstByte);
        if (_version == MqttProtocolVersion.V500 && rc != MqttReasonCode.Success)
        {
            w.WriteByte(3);
            w.WriteUInt16BigEndian(packetId);
            w.WriteByte((byte)rc);
        }
        else
        {
            w.WriteByte(2);
            w.WriteUInt16BigEndian(packetId);
        }
        await SendBytesAsync(w.WrittenMemory, ct).ConfigureAwait(false);
    }

    private async Task SendBytesAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        var w = _transport.ToClient;
        bytes.CopyTo(w.GetMemory(bytes.Length));
        w.Advance(bytes.Length);
        await w.FlushAsync(ct).ConfigureAwait(false);
    }
}
