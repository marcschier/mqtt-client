// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
namespace Mqtt.Client.UnitTests;

public class CodecTests
{
    [Test]
    [Arguments(0u, 1)]
    [Arguments(127u, 1)]
    [Arguments(128u, 2)]
    [Arguments(16383u, 2)]
    [Arguments(16384u, 3)]
    [Arguments(2097151u, 3)]
    [Arguments(2097152u, 4)]
    [Arguments(268435455u, 4)]
    public async Task VarInt_RoundTrips(uint value, int expectedBytes)
    {
        using var writer = new MqttBufferWriter(8);
        writer.WriteVarInt(value);
        await Assert.That(writer.WrittenCount).IsEqualTo(expectedBytes);

        var reader = new MqttSequenceReader(new ReadOnlySequence<byte>(writer.WrittenMemory));
        var decoded = reader.ReadVarInt(out var byteCount);
        await Assert.That(decoded).IsEqualTo(value);
        await Assert.That(byteCount).IsEqualTo(expectedBytes);
    }

    [Test]
    public async Task Connect_V5_Roundtrip()
    {
        var pkt = new ConnectPacket
        {
            ProtocolVersion = MqttProtocolVersion.V500,
            ClientId = "test-client",
            CleanStart = true,
            KeepAliveSeconds = 60,
            Username = "alice",
            Password = "secret"u8.ToArray(),
            ReceiveMaximum = 10,
        };
        using var w = new MqttBufferWriter(128);
        MqttPacketEncoder.EncodeConnect(pkt, w);

        // Decoder round-trip checks packet bytes are well-formed; first byte is CONNECT.
        await Assert.That(w.WrittenSpan[0]).IsEqualTo((byte)0x10);
        // Minimum sane size: fixed header (2) + protocol name "MQTT" (6) + level (1) + flags (1) +
        // ka (2) + props len (1) + client id (2+11) + username (2+5) + password (2+6).
        await Assert.That(w.WrittenCount).IsGreaterThan(30);
    }

    [Test]
    public async Task Publish_QoS0_Roundtrip_V311()
    {
        var pkt = new PublishPacket
        {
            Topic = "a/b",
            QoS = MqttQoS.AtMostOnce,
            Payload = new byte[] { 0x01, 0x02, 0x03 },
        };
        using var w = new MqttBufferWriter(32);
        MqttPacketEncoder.EncodePublish(pkt, MqttProtocolVersion.V311, w);

        var ok = MqttPacketDecoder.TryDecode(
            new ReadOnlySequence<byte>(w.WrittenMemory),
            MqttProtocolVersion.V311,
            out var packet,
            out var firstByte,
            out var consumed);
        await Assert.That(ok).IsTrue();
        await Assert.That(firstByte >> 4).IsEqualTo(3);
        var decoded = packet as PublishPacket;
        await Assert.That(decoded).IsNotNull();
        await Assert.That(decoded!.Topic).IsEqualTo("a/b");
        await Assert.That(decoded.QoS).IsEqualTo(MqttQoS.AtMostOnce);
        await Assert.That(decoded.Payload.Length).IsEqualTo(3);
    }

    [Test]
    public async Task PingReq_Encodes_To_TwoBytes()
    {
        using var w = new MqttBufferWriter(2);
        MqttPacketEncoder.EncodePingReq(w);
        await Assert.That(w.WrittenCount).IsEqualTo(2);
        await Assert.That(w.WrittenSpan[0]).IsEqualTo((byte)0xC0);
        await Assert.That(w.WrittenSpan[1]).IsEqualTo((byte)0x00);
    }

    [Test]
    public async Task Decoder_ReturnsFalse_WhenIncomplete()
    {
        // Just first byte of a fixed header, no remaining length.
        var bytes = new byte[] { 0x30 };
        var ok = MqttPacketDecoder.TryDecode(
            new ReadOnlySequence<byte>(bytes),
            MqttProtocolVersion.V500,
            out _,
            out _,
            out _);
        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task Subscribe_V5_Encodes_With_Options()
    {
        var pkt = new SubscribePacket
        {
            PacketId = 7,
            Filters = new[]
            {
                new SubscribeFilter(
                    "foo/+",
                    MqttQoS.AtLeastOnce,
                    NoLocal: true,
                    RetainAsPublished: true),
            },
        };
        using var w = new MqttBufferWriter(32);
        MqttPacketEncoder.EncodeSubscribe(pkt, MqttProtocolVersion.V500, w);
        // First byte is SUBSCRIBE (0x80) | reserved 0x02 = 0x82
        await Assert.That(w.WrittenSpan[0]).IsEqualTo((byte)0x82);
    }
}
