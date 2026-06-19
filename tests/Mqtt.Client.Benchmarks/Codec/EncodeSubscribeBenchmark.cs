// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using MQTTnet.Formatter;
using MQTTnet.Packets;
using MqttnetProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion;
using MqttnetQoS = MQTTnet.Protocol.MqttQualityOfServiceLevel;

namespace Mqtt.Client.Benchmarks;

public class EncodeSubscribeBenchmark
{
    private MqttSubscribePacket _mqttnetPacket = null!;
    private MqttPacketFormatterAdapter _mqttnetFormatter = null!;
    private MQTTnet.Formatter.MqttBufferWriter _mqttnetBuf = null!;
    private SubscribePacket _ourPacket = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ourPacket = new SubscribePacket
        {
            PacketId = 1,
            Filters = new[]
            {
                new SubscribeFilter("sensors/+/temp", Mqtt.Client.MqttQoS.AtLeastOnce),
                new SubscribeFilter("commands/#", Mqtt.Client.MqttQoS.AtMostOnce),
            },
        };
        _mqttnetPacket = new MqttSubscribePacket
        {
            PacketIdentifier = 1,
            TopicFilters =
            {
                new MqttTopicFilter {
                    Topic = "sensors/+/temp",
                    QualityOfServiceLevel = MqttnetQoS.AtLeastOnce },
                new MqttTopicFilter {
                    Topic = "commands/#",
                    QualityOfServiceLevel = MqttnetQoS.AtMostOnce },
            },
        };
        _mqttnetBuf = new MQTTnet.Formatter.MqttBufferWriter(64, 4096);
        _mqttnetFormatter = new MqttPacketFormatterAdapter(
            MqttnetProtocolVersion.V500,
            _mqttnetBuf);
    }

    [Benchmark(Baseline = true, Description = "MQTTnet")]
    public int Mqttnet_Encode()
    {
        _mqttnetBuf.Reset(64);
        return _mqttnetFormatter.Encode(_mqttnetPacket).Length;
    }

    [Benchmark(Description = "Mqtt.Client")]
    public int MqttClient_Encode()
    {
        var w = new Mqtt.Client.MqttBufferWriter(64);
        try
        {
            MqttPacketEncoder.EncodeSubscribe(_ourPacket, MqttProtocolVersion.V500, ref w);
            return w.WrittenCount;
        }
        finally
        {
            w.Dispose();
        }
    }
}
