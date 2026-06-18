// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
namespace Mqtt.Client.UnitTests;

public class DecoderAdditionalCoverageTests
{
    [Test]
    public async Task SubAck_v5_with_reason_string_and_user_property_decodes()
    {
        // Build manually: 0x90 (SUBACK) | length | pid | proplen | RS"ok" | UP("k","v") | rc
        var bytes = Build(0x90, body =>
        {
            body.AddRange(new byte[] { 0x00, 0x07 });           // packet id 7
            var props = new List<byte>();
            props.AddRange(new byte[] { 0x1F, 0x00, 0x02, (byte)'o', (byte)'k' });
            props.AddRange(new byte[] { 0x26, 0x00, 0x01, (byte)'k', 0x00, 0x01, (byte)'v' });
            body.Add((byte)props.Count);
            body.AddRange(props);
            body.Add((byte)MqttReasonCode.GrantedQoS1);
        });
        MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(bytes), MqttProtocolVersion.V500,
            out var packet, out _, out _);
        var sa = (SubAckPacket)packet!;
        await Assert.That(sa.PacketId).IsEqualTo((ushort)7);
        await Assert.That(sa.ReasonString).IsEqualTo("ok");
        await Assert.That(sa.UserProperties![0].Value).IsEqualTo("v");
        await Assert.That(sa.ReasonCodes[0]).IsEqualTo(MqttReasonCode.GrantedQoS1);
    }

    [Test]
    public async Task UnsubAck_v5_with_user_property_decodes()
    {
        var bytes = Build(0xB0, body =>
        {
            body.AddRange(new byte[] { 0x00, 0x0B });           // packet id 11
            var props = new List<byte>();
            props.AddRange(new byte[] { 0x26, 0x00, 0x01, (byte)'k', 0x00, 0x01, (byte)'v' });
            body.Add((byte)props.Count);
            body.AddRange(props);
            body.Add((byte)MqttReasonCode.NoSubscriptionExisted);
        });
        MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(bytes), MqttProtocolVersion.V500,
            out var packet, out _, out _);
        var ua = (UnsubAckPacket)packet!;
        await Assert.That(ua.PacketId).IsEqualTo((ushort)11);
        await Assert.That(ua.UserProperties![0].Name).IsEqualTo("k");
    }

    [Test]
    public async Task Disconnect_v5_with_session_expiry_and_reason_string_decodes()
    {
        var bytes = Build(0xE0, body =>
        {
            body.Add((byte)MqttReasonCode.ServerShuttingDown);
            var props = new List<byte>();
            props.AddRange(new byte[] { 0x11, 0, 0, 0, 60 });        // SessionExpiryInterval = 60
            props.AddRange(new byte[] { 0x1F, 0x00, 0x03, (byte)'b', (byte)'y', (byte)'e' });
            body.Add((byte)props.Count);
            body.AddRange(props);
        });
        MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(bytes), MqttProtocolVersion.V500,
            out var packet, out _, out _);
        var disc = (DisconnectPacket)packet!;
        await Assert.That(disc.ReasonCode).IsEqualTo(MqttReasonCode.ServerShuttingDown);
        await Assert.That(disc.SessionExpiryInterval).IsEqualTo(60u);
        await Assert.That(disc.ReasonString).IsEqualTo("bye");
    }

    [Test]
    public async Task Auth_v5_round_trips_via_encoder_decoder()
    {
        using var w = new MqttBufferWriter(64);
        MqttPacketEncoder.EncodeAuth(new AuthPacket
        {
            ReasonCode = MqttReasonCode.ContinueAuthentication,
            AuthenticationMethod = "M",
            AuthenticationData = new byte[] { 1, 2 },
            ReasonString = "r",
        }, w);
        MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(w.WrittenMemory),
            MqttProtocolVersion.V500, out var packet, out _, out _);
        var auth = (AuthPacket)packet!;
        await Assert.That(auth.AuthenticationMethod).IsEqualTo("M");
        await Assert.That(auth.AuthenticationData!.Length).IsEqualTo(2);
        await Assert.That(auth.ReasonString).IsEqualTo("r");
    }

    private static byte[] Build(byte firstByte, Action<List<byte>> writeBody)
    {
        var body = new List<byte>();
        writeBody(body);
        var result = new List<byte> { firstByte };
        var len = (uint)body.Count;
        do
        {
            var b = (byte)(len & 0x7F);
            len >>= 7;
            if (len > 0) b |= 0x80;
            result.Add(b);
        } while (len > 0);
        result.AddRange(body);
        return result.ToArray();
    }
}
