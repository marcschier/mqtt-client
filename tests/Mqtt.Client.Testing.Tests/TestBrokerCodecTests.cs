// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// Unit coverage for the test broker's wire codec (PacketBuilder writer + BufReader reader). The
// round-trip broker tests only drive well-formed packets, so the reader's truncation and
// overlong-varint guards — plus the writer's multi-byte varint, buffer-growth and framing paths —
// are exercised here directly. BufReader is a ref struct, so each use lives in a synchronous
// helper that returns plain values and never crosses an await.

namespace Mqtt.Client.Testing.Tests;

public class TestBrokerCodecTests
{
    [Test]
    public async Task Writer_and_reader_round_trip_all_field_types()
    {
        var r = FullRoundTrip();
        await Assert.That(r.B).IsEqualTo((byte)0xAB);
        await Assert.That(r.U).IsEqualTo((ushort)0x1234);
        await Assert.That(r.V).IsEqualTo(300u);
        await Assert.That(r.S).IsEqualTo("héllo");
        await Assert.That(r.Bin.AsSpan().SequenceEqual(new byte[] { 9, 8, 7 })).IsTrue();
        await Assert.That(r.Rem.AsSpan().SequenceEqual(new byte[] { 0xFE, 0xED })).IsTrue();
    }

    [Test]
    [Arguments(0u, 1)]
    [Arguments(127u, 1)]
    [Arguments(128u, 2)]
    [Arguments(16383u, 2)]
    [Arguments(16384u, 3)]
    [Arguments(2097151u, 3)]
    [Arguments(2097152u, 4)]
    [Arguments(268435455u, 4)]
    public async Task VarInt_round_trips(uint value, int expectedBytes)
    {
        var (decoded, bytes) = VarIntRoundTrip(value);
        await Assert.That(decoded).IsEqualTo(value);
        await Assert.That(bytes).IsEqualTo(expectedBytes);
    }

    [Test]
    public async Task Frame_prefixes_header_and_multibyte_remaining_length()
    {
        // A 130-byte body needs a 2-byte remaining-length VarInt (130 -> 0x82 0x01).
        var first = PacketBuilder.FirstByte(PacketType.Publish);
        var frame = PacketBuilder.Frame(first, new byte[130]);
        await Assert.That(frame[0]).IsEqualTo(first);
        await Assert.That(frame[1]).IsEqualTo((byte)0x82);
        await Assert.That(frame[2]).IsEqualTo((byte)0x01);
        await Assert.That(frame.Length).IsEqualTo(1 + 2 + 130);
    }

    [Test]
    public async Task FirstByte_packs_type_and_flags()
    {
        await Assert.That(PacketBuilder.FirstByte(PacketType.Publish, 0x0A)).IsEqualTo((byte)0x3A);
        await Assert.That(PacketBuilder.FirstByte(PacketType.Disconnect)).IsEqualTo((byte)0xE0);
    }

    [Test]
    public async Task ReadByte_past_end_throws()
    {
        await Assert.That(() => ReadByteAtEnd()).Throws<MqttBrokerProtocolException>();
    }

    [Test]
    public async Task ReadUInt16_truncated_throws()
    {
        await Assert.That(() => ReadU16Truncated()).Throws<MqttBrokerProtocolException>();
    }

    [Test]
    public async Task ReadBinary_field_truncated_throws()
    {
        await Assert.That(() => ReadFieldTruncated()).Throws<MqttBrokerProtocolException>();
    }

    [Test]
    public async Task ReadVarInt_overlong_throws()
    {
        await Assert.That(() => ReadVarIntTooLong()).Throws<MqttBrokerProtocolException>();
    }

    private static (byte B, ushort U, uint V, string S, byte[] Bin, byte[] Rem) FullRoundTrip()
    {
        var pb = new PacketBuilder(4);          // tiny capacity forces buffer growth
        pb.Byte(0xAB);
        pb.UInt16(0x1234);
        pb.VarInt(300);                          // 2-byte varint
        pb.Str("héllo");                         // multi-byte UTF-8 (length != byte count)
        pb.Bin(new byte[] { 9, 8, 7 });
        pb.Raw(new byte[] { 0xFE, 0xED });       // trailing bytes consumed via ReadRemaining
        var body = pb.Body.ToArray();

        var r = new BufReader(body);
        var b = r.ReadByte();
        var u = r.ReadUInt16();
        var v = r.ReadVarInt();
        var s = r.ReadString();
        var bin = r.ReadBinary().ToArray();
        var rem = r.ReadRemaining().ToArray();
        return (b, u, v, s, bin, rem);
    }

    private static (uint Decoded, int Bytes) VarIntRoundTrip(uint value)
    {
        var pb = new PacketBuilder(1);
        pb.VarInt(value);
        var body = pb.Body.ToArray();
        var r = new BufReader(body);
        var decoded = r.ReadVarInt();
        return (decoded, body.Length);
    }

    private static void ReadByteAtEnd()
    {
        var r = new BufReader(Array.Empty<byte>());
        _ = r.ReadByte();
    }

    private static void ReadU16Truncated()
    {
        var r = new BufReader(new byte[] { 0x01 });
        _ = r.ReadUInt16();
    }

    private static void ReadFieldTruncated()
    {
        // 2-byte length prefix declares 5 bytes, but only 2 follow.
        var r = new BufReader(new byte[] { 0x00, 0x05, 0x01, 0x02 });
        _ = r.ReadBinary().Length;
    }

    private static void ReadVarIntTooLong()
    {
        var r = new BufReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        _ = r.ReadVarInt();
    }
}
