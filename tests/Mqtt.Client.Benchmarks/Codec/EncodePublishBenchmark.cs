// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
using BenchmarkDotNet.Attributes;
using Mqtt.Client.Benchmarks.Infrastructure;
using Mqtt.Client.Buffers;
using Mqtt.Client.Protocol;
using Mqtt.Client.Protocol.Packets;
using MQTTnet.Formatter;
using MQTTnet.Packets;
using MqttnetProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion;
using MqttnetQoS = MQTTnet.Protocol.MqttQualityOfServiceLevel;

namespace Mqtt.Client.Benchmarks.Codec;

public class EncodePublishBenchmark
{
    [Params(64, 256, 1024, 4096, 16384, 65536)]
    public int PayloadSize { get; set; }

    private byte[] _payload = null!;
    private MqttPublishPacket _mqttnetPacket = null!;
    private MqttPacketFormatterAdapter _mqttnetFormatter = null!;
    private MQTTnet.Formatter.MqttBufferWriter _mqttnetBuf = null!;
    private PublishPacket _ourPacket = null!;

    public EncodePublishBenchmark() : this(full: false) { }
    public EncodePublishBenchmark(bool full) { _ = full; }

    [GlobalSetup]
    public void Setup()
    {
        _payload = new byte[PayloadSize];
        new Random(42).NextBytes(_payload);
        _ourPacket = new PublishPacket
        {
            Topic = "bench/encode",
            QoS = Mqtt.Client.MqttQoS.AtMostOnce,
            PacketId = 0,
            Payload = _payload,
        };
        _mqttnetPacket = new MqttPublishPacket
        {
            Topic = "bench/encode",
            QualityOfServiceLevel = MqttnetQoS.AtMostOnce,
            PayloadSegment = new ArraySegment<byte>(_payload),
        };
        _mqttnetBuf = new MQTTnet.Formatter.MqttBufferWriter(PayloadSize + 64, PayloadSize * 2 + 1024);
        _mqttnetFormatter = new MqttPacketFormatterAdapter(MqttnetProtocolVersion.V500, _mqttnetBuf);
    }

    [Benchmark(Baseline = true, Description = "MQTTnet")]
    public int Mqttnet_Encode()
    {
        _mqttnetBuf.Reset(64);
        var buf = _mqttnetFormatter.Encode(_mqttnetPacket);
        return buf.Length;
    }

    [Benchmark(Description = "Mqtt.Client")]
    public int MqttClient_Encode()
    {
        using var w = new Mqtt.Client.Buffers.MqttBufferWriter(PayloadSize + 64);
        MqttPacketEncoder.EncodePublish(_ourPacket, MqttProtocolVersion.V500, w);
        return w.WrittenCount;
    }
}
