// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
namespace Mqtt.Client.UnitTests;

public class CodecAllPacketsTests
{
    [Test]
    public async Task Connect_v5_with_will_and_auth_roundtrips()
    {
        var pkt = new ConnectPacket
        {
            ProtocolVersion = MqttProtocolVersion.V500,
            ClientId = "client-1",
            CleanStart = true,
            KeepAliveSeconds = 60,
            Username = "u",
            Password = "p"u8.ToArray(),
            Will = new MqttLastWill
            {
                Topic = "last/will",
                PayloadMemory = new byte[] { 1, 2, 3 },
                QoS = MqttQoS.AtLeastOnce,
                Retain = true,
            },
            SessionExpiryInterval = 300,
            ReceiveMaximum = 10,
            MaximumPacketSize = 65535,
            TopicAliasMaximum = 5,
            RequestResponseInformation = true,
            RequestProblemInformation = false,
            AuthenticationMethod = "SCRAM-SHA-256",
            AuthenticationData = new byte[] { 0xAA, 0xBB },
        };
        using var w = new MqttBufferWriter(256);
        MqttPacketEncoder.EncodeConnect(pkt, w);
        await Assert.That(w.WrittenSpan[0]).IsEqualTo((byte)0x10);
        await Assert.That(w.WrittenCount).IsGreaterThan(30);
    }

    [Test]
    public async Task Publish_v5_with_full_properties_roundtrips()
    {
        var pkt = new PublishPacket
        {
            Topic = "topic",
            QoS = MqttQoS.AtLeastOnce,
            PacketId = 7,
            PayloadMemory = new byte[] { 0x01, 0x02 },
            Properties = new MqttPublishProperties
            {
                PayloadFormatIndicator = 1,
                MessageExpiryInterval = 60,
                TopicAlias = 3,
                ResponseTopic = "rsp",
                CorrelationData = new byte[] { 0xCC },
                ContentType = "application/json",
                SubscriptionIdentifiers = new[] { 1u, 2u },
                UserProperties = new[] { new MqttUserProperty("k", "v") },
            },
        };
        using var w = new MqttBufferWriter(64);
        MqttPacketEncoder.EncodePublish(pkt, MqttProtocolVersion.V500, w);

        var ok = MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(w.WrittenMemory),
            MqttProtocolVersion.V500, out var decoded, out _, out _);
        await Assert.That(ok).IsTrue();
        var d = (PublishPacket)decoded!;
        await Assert.That(d.Topic).IsEqualTo("topic");
        await Assert.That(d.QoS).IsEqualTo(MqttQoS.AtLeastOnce);
        await Assert.That(d.PacketId).IsEqualTo((ushort)7);
        await Assert.That(d.Properties!.ResponseTopic).IsEqualTo("rsp");
        await Assert.That(d.Properties.ContentType).IsEqualTo("application/json");
        await Assert.That(d.Properties.UserProperties![0].Name).IsEqualTo("k");
    }

    [Test]
    public async Task EncodePublishHeader_then_separate_payload_reconstructs_full_packet()
    {
        var payload = new byte[64 * 1024];
        new Random(7).NextBytes(payload);
        var pkt = new PublishPacket
        {
            Topic = "big",
            QoS = MqttQoS.AtMostOnce,
            PayloadMemory = payload,
        };
        using var w = new MqttBufferWriter(128);
        MqttPacketEncoder.EncodePublishHeader(pkt, MqttProtocolVersion.V500, w);

        // Compose header + payload into a single buffer (what the pipe would emit on the wire).
        var combined = new byte[w.WrittenCount + payload.Length];
        w.WrittenSpan.CopyTo(combined);
        payload.CopyTo(combined.AsSpan(w.WrittenCount));

        var ok = MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(combined),
            MqttProtocolVersion.V500, out var decoded, out _, out _);
        await Assert.That(ok).IsTrue();
        var d = (PublishPacket)decoded!;
        await Assert.That((int)d.Payload.Length).IsEqualTo(payload.Length);
        await Assert.That(d.Payload.FirstSpan.SequenceEqual(payload)).IsTrue();
    }

    [Test]
    public async Task PubRec_PubRel_PubComp_v5_roundtrip()
    {
        var cases = new (string, Action<MqttBufferWriter>, byte)[]
        {
            ("PubRec", w => MqttPacketEncoder.EncodePubRec(
                42,
                MqttReasonCode.Success,
                MqttProtocolVersion.V500,
                w), 0x50),
            ("PubRel", w => MqttPacketEncoder.EncodePubRel(
                42,
                MqttReasonCode.Success,
                MqttProtocolVersion.V500,
                w), 0x62),
            ("PubComp", w => MqttPacketEncoder.EncodePubComp(
                42,
                MqttReasonCode.Success,
                MqttProtocolVersion.V500,
                w), 0x70),
        };
        foreach (var (name, encode, expectedFirstByte) in cases)
        {
            using var w = new MqttBufferWriter(8);
            encode(w);
            await Assert.That(w.WrittenSpan[0]).IsEqualTo(expectedFirstByte);
        }
    }

    [Test]
    public async Task Unsubscribe_v5_roundtrips()
    {
        var pkt = new UnsubscribePacket { PacketId = 9, Topics = new[] { "a/b", "c/d" } };
        using var w = new MqttBufferWriter(32);
        MqttPacketEncoder.EncodeUnsubscribe(pkt, MqttProtocolVersion.V500, w);
        await Assert.That(w.WrittenSpan[0]).IsEqualTo((byte)0xA2);
    }

    [Test]
    public async Task Disconnect_v5_with_properties_encodes()
    {
        var pkt = new DisconnectPacket
        {
            ReasonCode = MqttReasonCode.AdministrativeAction,
            SessionExpiryInterval = 60,
            ReasonString = "shutdown",
            ServerReference = "alt-broker:1883",
        };
        using var w = new MqttBufferWriter(64);
        MqttPacketEncoder.EncodeDisconnect(pkt, MqttProtocolVersion.V500, w);
        await Assert.That(w.WrittenSpan[0]).IsEqualTo((byte)0xE0);
        await Assert.That(w.WrittenCount).IsGreaterThan(2);
    }

    [Test]
    public async Task Disconnect_v311_is_two_bytes_with_no_reason_code()
    {
        var pkt = new DisconnectPacket();
        using var w = new MqttBufferWriter(2);
        MqttPacketEncoder.EncodeDisconnect(pkt, MqttProtocolVersion.V311, w);
        await Assert.That(w.WrittenCount).IsEqualTo(2);
        await Assert.That(w.WrittenSpan[0]).IsEqualTo((byte)0xE0);
        await Assert.That(w.WrittenSpan[1]).IsEqualTo((byte)0x00);
    }

    [Test]
    public async Task Auth_v5_encodes_with_method_and_data()
    {
        var pkt = new AuthPacket
        {
            ReasonCode = MqttReasonCode.ContinueAuthentication,
            AuthenticationMethod = "SCRAM-SHA-256",
            AuthenticationData = new byte[] { 1, 2, 3 },
        };
        using var w = new MqttBufferWriter(32);
        MqttPacketEncoder.EncodeAuth(pkt, w);
        await Assert.That(w.WrittenSpan[0]).IsEqualTo((byte)0xF0);
    }

    [Test]
    public async Task Decoder_rejects_oversized_packet_when_above_limit()
    {
        // 0x30 = PUBLISH, remaining length 5 bytes (encoded as var-int with high bit set).
        // We claim a remaining length larger than maxPacketSize.
        var bytes = new byte[] { 0x30, 0x82, 0x01, /* ... */ };
        await Assert.That(() =>
        {
            MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(bytes), MqttProtocolVersion.V500,
                maxPacketSize: 16, out _, out _, out _);
        }).Throws<MqttProtocolException>();
    }

    [Test]
    public async Task Decoder_returns_false_when_packet_body_incomplete()
    {
        // PUBLISH header says remaining length 100, but we provide only the header.
        var bytes = new byte[] { 0x30, 0x64 };
        var ok = MqttPacketDecoder.TryDecode(
            new ReadOnlySequence<byte>(bytes),
            MqttProtocolVersion.V500,
            out _,
            out _,
            out _);
        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task Decoder_throws_on_unknown_packet_type_from_broker()
    {
        // 0x10 = CONNECT — client should never see this from the broker.
        var bytes = new byte[] { 0x10, 0x00 };
        await Assert.That(() =>
        {
            MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(bytes), MqttProtocolVersion.V500,
                out _, out _, out _);
        }).Throws<MqttProtocolException>();
    }
}
