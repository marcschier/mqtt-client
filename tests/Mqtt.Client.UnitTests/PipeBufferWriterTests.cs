// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
using System.IO.Pipelines;
namespace Mqtt.Client.UnitTests;

/// <summary>
/// Verifies <see cref="PipeBufferWriter"/> produces byte-for-byte identical output to
/// <see cref="MqttBufferWriter"/> for every control packet, including the back-patch-after-grow
/// path where a header outgrows the pipe's initial buffer.
/// </summary>
public class PipeBufferWriterTests
{
    private static byte[] ViaBuffer(int hint, RefBufferWriterEncode encode)
    {
        var w = new MqttBufferWriter(hint);
        try
        {
            encode(ref w);
            return w.WrittenSpan.ToArray();
        }
        finally
        {
            w.Dispose();
        }
    }

    private static async Task<byte[]> ViaPipe(int hint, RefPipeBufferWriterEncode encode)
    {
        var pipe = new Pipe();
        var w = new PipeBufferWriter(pipe.Writer, hint);
        encode(ref w);
        w.Commit();
        await pipe.Writer.FlushAsync();
        await pipe.Writer.CompleteAsync();
        var read = await pipe.Reader.ReadAsync();
        var bytes = read.Buffer.ToArray();
        pipe.Reader.AdvanceTo(read.Buffer.End);
        await pipe.Reader.CompleteAsync();
        return bytes;
    }

    private static async Task AssertSame(
        int hint,
        RefBufferWriterEncode viaBuffer,
        RefPipeBufferWriterEncode viaPipe)
    {
        var expected = ViaBuffer(hint, viaBuffer);
        var actual = await ViaPipe(hint, viaPipe);
        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task Connect_v5_with_will_and_auth_matches_buffer_writer()
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
            AuthenticationMethod = "SCRAM-SHA-256",
            AuthenticationData = new byte[] { 0xAA, 0xBB },
        };
        await AssertSame(
            256,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodeConnect(pkt, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodeConnect(pkt, ref w));
    }

    [Test]
    public async Task Publish_v5_full_properties_matches_buffer_writer()
    {
        var pkt = new PublishPacket
        {
            Topic = "topic/full",
            QoS = MqttQoS.AtLeastOnce,
            PacketId = 7,
            PayloadMemory = new byte[] { 0x01, 0x02, 0x03 },
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
        await AssertSame(
            64,
            (ref MqttBufferWriter w) =>
                MqttPacketEncoder.EncodePublish(pkt, MqttProtocolVersion.V500, ref w),
            (ref PipeBufferWriter w) =>
                MqttPacketEncoder.EncodePublish(pkt, MqttProtocolVersion.V500, ref w));
    }

    [Test]
    public async Task PublishHeader_v500_matches_buffer_writer()
    {
        var pkt = new PublishPacket
        {
            Topic = "hdr/only",
            QoS = MqttQoS.ExactlyOnce,
            PacketId = 99,
            PayloadMemory = new byte[1234],
        };
        await AssertSame(
            64,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodePublishHeader(
                pkt, MqttProtocolVersion.V500, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodePublishHeader(
                pkt, MqttProtocolVersion.V500, ref w));
    }

    [Test]
    public async Task Acks_match_buffer_writer()
    {
        await AssertSame(8,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodePubAck(
                5, MqttReasonCode.Success, MqttProtocolVersion.V500, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodePubAck(
                5, MqttReasonCode.Success, MqttProtocolVersion.V500, ref w));
        await AssertSame(8,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodePubRec(
                6, MqttReasonCode.Success, MqttProtocolVersion.V500, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodePubRec(
                6, MqttReasonCode.Success, MqttProtocolVersion.V500, ref w));
        await AssertSame(8,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodePubRel(
                7, MqttReasonCode.Success, MqttProtocolVersion.V500, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodePubRel(
                7, MqttReasonCode.Success, MqttProtocolVersion.V500, ref w));
        await AssertSame(8,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodePubComp(
                8, MqttReasonCode.Success, MqttProtocolVersion.V500, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodePubComp(
                8, MqttReasonCode.Success, MqttProtocolVersion.V500, ref w));
    }

    [Test]
    public async Task Subscribe_and_unsubscribe_match_buffer_writer()
    {
        var sub = new SubscribePacket
        {
            PacketId = 11,
            SubscriptionIdentifier = 42,
            Filters = new[]
            {
                new SubscribeFilter("a/+/c", MqttQoS.AtLeastOnce, NoLocal: true),
                new SubscribeFilter("d/#", MqttQoS.ExactlyOnce),
            },
        };
        await AssertSame(64,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodeSubscribe(
                sub, MqttProtocolVersion.V500, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodeSubscribe(
                sub, MqttProtocolVersion.V500, ref w));

        var unsub = new UnsubscribePacket { PacketId = 12, Topics = new[] { "a/b", "c/d" } };
        await AssertSame(32,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodeUnsubscribe(
                unsub, MqttProtocolVersion.V500, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodeUnsubscribe(
                unsub, MqttProtocolVersion.V500, ref w));
    }

    [Test]
    public async Task PingReq_disconnect_auth_match_buffer_writer()
    {
        await AssertSame(2,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodePingReq(ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodePingReq(ref w));

        var disc = new DisconnectPacket
        {
            ReasonCode = MqttReasonCode.AdministrativeAction,
            SessionExpiryInterval = 60,
            ReasonString = "shutdown",
        };
        await AssertSame(64,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodeDisconnect(
                disc, MqttProtocolVersion.V500, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodeDisconnect(
                disc, MqttProtocolVersion.V500, ref w));

        var disc311 = new DisconnectPacket();
        await AssertSame(2,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodeDisconnect(
                disc311, MqttProtocolVersion.V311, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodeDisconnect(
                disc311, MqttProtocolVersion.V311, ref w));

        var auth = new AuthPacket
        {
            ReasonCode = MqttReasonCode.ContinueAuthentication,
            AuthenticationMethod = "M",
            AuthenticationData = new byte[] { 1, 2, 3 },
        };
        await AssertSame(32,
            (ref MqttBufferWriter w) => MqttPacketEncoder.EncodeAuth(auth, ref w),
            (ref PipeBufferWriter w) => MqttPacketEncoder.EncodeAuth(auth, ref w));
    }

    [Test]
    public async Task Large_header_forces_grow_and_matches_buffer_writer()
    {
        // A topic far larger than the pipe's initial segment forces PipeBufferWriter.Grow, and the
        // trailing properties exercise the property-length + remaining-length back-patch after the
        // header has been copied into the larger buffer.
        var topic = new string('t', 20_000);
        var pkt = new PublishPacket
        {
            Topic = topic,
            QoS = MqttQoS.AtLeastOnce,
            PacketId = 1,
            PayloadMemory = new byte[] { 9 },
            Properties = new MqttPublishProperties
            {
                ResponseTopic = "rsp",
                UserProperties = new[] { new MqttUserProperty("k", "v") },
            },
        };
        await AssertSame(
            16,
            (ref MqttBufferWriter w) =>
                MqttPacketEncoder.EncodePublish(pkt, MqttProtocolVersion.V500, ref w),
            (ref PipeBufferWriter w) =>
                MqttPacketEncoder.EncodePublish(pkt, MqttProtocolVersion.V500, ref w));
    }

    [Test]
    public async Task Roundtrips_through_decoder()
    {
        // End-to-end sanity: a PipeBufferWriter-encoded publish decodes back to the same packet.
        var pkt = new PublishPacket
        {
            Topic = "rt/topic",
            QoS = MqttQoS.AtLeastOnce,
            PacketId = 3,
            PayloadMemory = new byte[] { 4, 5, 6, 7 },
        };
        var bytes = await ViaPipe(
            64,
            (ref PipeBufferWriter w) =>
                MqttPacketEncoder.EncodePublish(pkt, MqttProtocolVersion.V500, ref w));
        var ok = MqttPacketDecoder.TryDecode(
            new ReadOnlySequence<byte>(bytes), MqttProtocolVersion.V500, out var decoded, out _,
            out _);
        await Assert.That(ok).IsTrue();
        var d = (PublishPacket)decoded!;
        await Assert.That(d.Topic).IsEqualTo("rt/topic");
        await Assert.That(d.PacketId).IsEqualTo((ushort)3);
        await Assert.That((int)d.Payload.Length).IsEqualTo(4);
    }
}
