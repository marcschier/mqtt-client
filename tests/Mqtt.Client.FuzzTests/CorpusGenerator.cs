// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using Mqtt.Client;
using Mqtt.Client.Buffers;
using Mqtt.Client.Protocol;
using Mqtt.Client.Protocol.Packets;

namespace Mqtt.Client.FuzzTests;

/// <summary>
/// Writes a small set of valid wire-format inputs to corpus directories so libFuzzer starts with
/// meaningful coverage instead of pure random bytes. Invoke with <c>--seed-corpus</c>.
/// </summary>
internal static class CorpusGenerator
{
    public static void GenerateAll(string corpusRoot)
    {
        var decoder = Path.Combine(corpusRoot, "decoder");
        var roundtrip = Path.Combine(corpusRoot, "codec-roundtrip");
        var trie = Path.Combine(corpusRoot, "topic-trie");
        Directory.CreateDirectory(decoder);
        Directory.CreateDirectory(roundtrip);
        Directory.CreateDirectory(trie);

        WriteDecoderSeeds(decoder);
        WriteRoundtripSeeds(roundtrip);
        WriteTrieSeeds(trie);
    }

    private static void WriteDecoderSeeds(string dir)
    {
        // Each seed is a well-formed control packet that the decoder should accept.
        WritePublish(dir, MqttQoS.AtMostOnce, "a", new byte[] { 1 }, "publish-qos0.bin");
        WritePublish(dir, MqttQoS.AtLeastOnce, "topic/with/levels", new byte[] { 1, 2, 3, 4 }, "publish-qos1.bin");
        WritePublish(dir, MqttQoS.ExactlyOnce, "$share/g1/topic", new byte[] { 9 }, "publish-qos2.bin");

        // PINGRESP fixed-header pair.
        File.WriteAllBytes(Path.Combine(dir, "pingresp.bin"), new byte[] { 0xD0, 0x00 });
        // DISCONNECT v3.1.1
        File.WriteAllBytes(Path.Combine(dir, "disconnect.bin"), new byte[] { 0xE0, 0x00 });
    }

    private static void WriteRoundtripSeeds(string dir)
    {
        // The roundtrip harness uses byte 0 as flags + remaining bytes as payload.
        File.WriteAllBytes(Path.Combine(dir, "qos0-v311-empty.bin"), new byte[] { 0x00 });
        File.WriteAllBytes(Path.Combine(dir, "qos1-v500-hello.bin"),
            new byte[] { 0x05 /* qos=1, v5 */, (byte)'h', (byte)'i' });
    }

    private static void WriteTrieSeeds(string dir)
    {
        File.WriteAllBytes(Path.Combine(dir, "exact.bin"),
            CombineWithLength((byte[])"a/b/c"u8.ToArray(), (byte[])"a/b/c"u8.ToArray()));
        File.WriteAllBytes(Path.Combine(dir, "plus.bin"),
            CombineWithLength((byte[])"a/+/c"u8.ToArray(), (byte[])"a/x/c"u8.ToArray()));
        File.WriteAllBytes(Path.Combine(dir, "hash.bin"),
            CombineWithLength((byte[])"a/#"u8.ToArray(), (byte[])"a/b/c"u8.ToArray()));
    }

    private static void WritePublish(string dir, MqttQoS qos, string topic, byte[] payload, string fileName)
    {
        var packet = new PublishPacket
        {
            Topic = topic,
            QoS = qos,
            PacketId = qos == MqttQoS.AtMostOnce ? (ushort)0 : (ushort)1,
            Payload = payload,
        };
        using var w = new MqttBufferWriter(payload.Length + 32);
        MqttPacketEncoder.EncodePublish(packet, MqttProtocolVersion.V500, w);
        File.WriteAllBytes(Path.Combine(dir, fileName), w.WrittenSpan.ToArray());
    }

    private static byte[] CombineWithLength(byte[] filter, byte[] topic)
    {
        var prefix = (byte)filter.Length;
        var result = new byte[1 + filter.Length + topic.Length];
        result[0] = prefix;
        Buffer.BlockCopy(filter, 0, result, 1, filter.Length);
        Buffer.BlockCopy(topic, 0, result, 1 + filter.Length, topic.Length);
        return result;
    }
}
