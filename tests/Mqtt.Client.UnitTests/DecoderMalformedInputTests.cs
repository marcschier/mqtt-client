// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
using Mqtt.Client.Buffers;
using Mqtt.Client.Protocol;
using Mqtt.Client.Protocol.Packets;

namespace Mqtt.Client.UnitTests;

public class DecoderMalformedInputTests
{
    [Test]
    public async Task Decoder_throws_on_malformed_var_int_with_too_many_continuation_bytes()
    {
        // 5 continuation bytes (high bit set) is malformed (max 4).
        var bytes = new byte[] { 0x30, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F };
        await Assert.That(() =>
        {
            MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(bytes), MqttProtocolVersion.V500,
                out _, out _, out _);
        }).Throws<MqttProtocolException>();
    }

    [Test]
    public async Task Decoder_returns_false_on_too_short_for_var_int()
    {
        // Only one byte with continuation bit; needs another byte for the var-int.
        var bytes = new byte[] { 0x30, 0x80 };
        var ok = MqttPacketDecoder.TryDecode(
            new ReadOnlySequence<byte>(bytes),
            MqttProtocolVersion.V500,
            out _,
            out _,
            out _);
        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task ConnAck_v311_maps_return_codes()
    {
        // 0x20 = CONNACK, length 2, session-present 0, return code 5 (NotAuthorized in v3.1.1).
        var bytes = new byte[] { 0x20, 0x02, 0x00, 0x05 };
        MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(bytes), MqttProtocolVersion.V311,
            out var packet, out _, out _);
        var connack = (ConnAckPacket)packet!;
        await Assert.That(connack.ReasonCode).IsEqualTo(MqttReasonCode.NotAuthorized);
    }

    [Test]
    public async Task Decode_PingResp_yields_null_packet()
    {
        var bytes = new byte[] { 0xD0, 0x00 };
        var ok = MqttPacketDecoder.TryDecode(
            new ReadOnlySequence<byte>(bytes),
            MqttProtocolVersion.V500,
            out var packet,
            out var firstByte,
            out _);
        await Assert.That(ok).IsTrue();
        await Assert.That(packet).IsNull();
        await Assert.That(firstByte).IsEqualTo((byte)0xD0);
    }

    [Test]
    public async Task BufferWriter_VarInt_round_trip_max_value()
    {
        using var w = new MqttBufferWriter(8);
        w.WriteVarInt(268_435_455);
        await Assert.That(w.WrittenCount).IsEqualTo(4);
        var r = new MqttSequenceReader(new ReadOnlySequence<byte>(w.WrittenMemory));
        var decoded = r.ReadVarInt(out var bytes);
        await Assert.That(decoded).IsEqualTo(268_435_455u);
        await Assert.That(bytes).IsEqualTo(4);
    }

    [Test]
    public async Task BufferWriter_VarInt_throws_above_max()
    {
        var w = new MqttBufferWriter(8);
        try
        {
            await Assert.That(() => w.WriteVarInt(268_435_456u)).Throws<MqttProtocolException>();
        }
        finally { w.Dispose(); }
    }
}
