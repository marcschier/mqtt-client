// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
using Mqtt.Client;
namespace Mqtt.Client.FuzzTests;

/// <summary>
/// libFuzzer harness for codec round-tripping. Interprets fuzz input as the payload of a
/// hand-built PUBLISH packet, encodes, decodes, and asserts the topic + payload match.
/// </summary>
internal static class CodecRoundtripHarness
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return;

        // Use the first byte to pick QoS (0/1/2) and protocol version (3.1.1 / 5.0).
        var qos = (MqttQoS)(data[0] & 0x03);
        if ((byte)qos > 2) qos = MqttQoS.AtMostOnce;
        var v5 = (data[0] & 0x04) != 0;
        var version = v5 ? MqttProtocolVersion.V500 : MqttProtocolVersion.V311;

        var payload = data.Slice(1).ToArray();
        var packet = new PublishPacket
        {
            Topic = "fuzz/roundtrip",
            QoS = qos,
            PacketId = qos == MqttQoS.AtMostOnce ? (ushort)0 : (ushort)42,
            Payload = payload,
        };

        using var writer = new MqttBufferWriter(payload.Length + 32);
        MqttPacketEncoder.EncodePublish(packet, version, writer);

        if (!MqttPacketDecoder.TryDecode(
                new ReadOnlySequence<byte>(writer.WrittenMemory),
                version,
                out var decoded,
                out _,
                out _))
        {
            throw new InvalidOperationException("Decoder rejected own encoder output.");
        }
        if (decoded is not PublishPacket d ||
            d.Topic != "fuzz/roundtrip" ||
            d.Payload.Length != payload.Length ||
            !d.Payload.Span.SequenceEqual(payload))
        {
            throw new InvalidOperationException("Roundtrip mismatch.");
        }
    }
}
