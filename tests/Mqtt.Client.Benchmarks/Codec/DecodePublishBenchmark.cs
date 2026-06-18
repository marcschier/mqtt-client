// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
using BenchmarkDotNet.Attributes;
using MQTTnet.Adapter;
using MQTTnet.Formatter;
using MQTTnet.Packets;
using MqttnetProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion;
using MqttnetQoS = MQTTnet.Protocol.MqttQualityOfServiceLevel;

namespace Mqtt.Client.Benchmarks;

public class DecodePublishBenchmark
{
    [Params(64, 256, 1024, 4096, 16384, 65536)]
    public int PayloadSize { get; set; }

    private byte[] _encoded = null!;
    private MqttPacketFormatterAdapter _mqttnetFormatter = null!;
    private ReadOnlySequence<byte> _encodedSequence;

    [GlobalSetup]
    public void Setup()
    {
        var payload = new byte[PayloadSize];
        new Random(42).NextBytes(payload);
        // Encode once with our encoder; both decoders read the same bytes (codec is identical on the wire).
        using var w = new Mqtt.Client.MqttBufferWriter(PayloadSize + 64);
        MqttPacketEncoder.EncodePublish(new PublishPacket
        {
            Topic = "bench/decode",
            QoS = Mqtt.Client.MqttQoS.AtMostOnce,
            PacketId = 0,
            Payload = payload,
        }, MqttProtocolVersion.V500, w);
        _encoded = w.WrittenSpan.ToArray();
        _encodedSequence = new ReadOnlySequence<byte>(_encoded);

        var writeBuf = new MQTTnet.Formatter.MqttBufferWriter(64, PayloadSize * 2 + 1024);
        _mqttnetFormatter = new MqttPacketFormatterAdapter(MqttnetProtocolVersion.V500, writeBuf);
    }

    [Benchmark(Baseline = true, Description = "MQTTnet")]
    public object? Mqttnet_Decode()
    {
        // Strip fixed header to build ReceivedMqttPacket: first byte + remaining length.
        var firstByte = _encoded[0];
        // Decode the var-int remaining length.
        var idx = 1;
        var remaining = 0;
        var multiplier = 1;
        byte b;
        do
        {
            b = _encoded[idx++];
            remaining += (b & 0x7F) * multiplier;
            multiplier *= 128;
        }
        while ((b & 0x80) != 0);
        var body = new ArraySegment<byte>(_encoded, idx, remaining);
        var packet = new ReceivedMqttPacket(firstByte, body, _encoded.Length);
        return _mqttnetFormatter.Decode(packet);
    }

    [Benchmark(Description = "Mqtt.Client")]
    public object? MqttClient_Decode()
    {
        MqttPacketDecoder.TryDecode(
            _encodedSequence,
            MqttProtocolVersion.V500,
            out var packet,
            out _,
            out _);
        return packet;
    }
}
