// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// AOT smoke test: builds and publishes with PublishAot=true to verify no trim/AOT warnings.
// Does not require a real broker — exercises codec + builder + DI paths only.

using System.Buffers;
using Mqtt.Client;
Console.WriteLine("Mqtt.Client AOT smoke test");

// Builder + options
var client = MqttClient.CreateBuilder()
    .ConnectTo("mqtt://localhost")
    .WithClientId("aot-smoke")
    .WithProtocol(MqttProtocolVersion.V500)
    .Build();

Console.WriteLine($"Built client. State={client.State}");

// Encode + decode roundtrip
using var w = new MqttBufferWriter(32);
var pub = new PublishPacket
{
    Topic = "smoke/aot",
    QoS = MqttQoS.AtMostOnce,
    Payload = new byte[] { 1, 2, 3, 4 },
};
MqttPacketEncoder.EncodePublish(pub, MqttProtocolVersion.V500, w);
var ok = MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(w.WrittenMemory),
    MqttProtocolVersion.V500, out var packet, out _, out _);
Console.WriteLine($"Roundtrip ok={ok}, packet={packet?.GetType().Name}");

// Topic trie
var trie = new TopicFilterTrie<string>();
trie.Add("a/+", "x");
var hits = new List<string>();
trie.Match("a/b", hits.Add);
Console.WriteLine($"Trie hits: {hits.Count}");

await client.DisposeAsync();
Console.WriteLine("AOT smoke OK.");
return 0;
