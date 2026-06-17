// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using MQTTnet;
using MqttnetQoS = MQTTnet.Protocol.MqttQualityOfServiceLevel;

namespace Mqtt.Client.Benchmarks.EndToEnd;

public class PublishQoS0Benchmark : BrokerBenchmarkBase
{
    private MqttApplicationMessage _mqttnetMessage = null!;

    [GlobalSetup]
        public override async Task Setup()
    {
        await base.Setup();
        _mqttnetMessage = new MqttApplicationMessageBuilder()
            .WithTopic($"{TopicPrefix}/qos0")
            .WithPayload(Payload)
            .WithQualityOfServiceLevel(MqttnetQoS.AtMostOnce)
            .Build();
    }

    [Benchmark(Baseline = true, Description = "MQTTnet")]
    public Task Mqttnet_Publish() => MqttnetClient.PublishAsync(_mqttnetMessage);

    [Benchmark(Description = "Mqtt.Client")]
    public ValueTask<Mqtt.Client.MqttPublishResult> MqttClient_Publish()
        => OurClient.PublishAsync($"{TopicPrefix}/qos0", Payload, Mqtt.Client.MqttQoS.AtMostOnce);
}

public class PublishQoS1Benchmark : BrokerBenchmarkBase
{
    private MqttApplicationMessage _mqttnetMessage = null!;

    [GlobalSetup]
        public override async Task Setup()
    {
        await base.Setup();
        _mqttnetMessage = new MqttApplicationMessageBuilder()
            .WithTopic($"{TopicPrefix}/qos1")
            .WithPayload(Payload)
            .WithQualityOfServiceLevel(MqttnetQoS.AtLeastOnce)
            .Build();
    }

    [Benchmark(Baseline = true, Description = "MQTTnet")]
    public Task Mqttnet_Publish() => MqttnetClient.PublishAsync(_mqttnetMessage);

    [Benchmark(Description = "Mqtt.Client")]
    public ValueTask<Mqtt.Client.MqttPublishResult> MqttClient_Publish()
        => OurClient.PublishAsync($"{TopicPrefix}/qos1", Payload, Mqtt.Client.MqttQoS.AtLeastOnce);
}

public class PublishQoS2Benchmark : BrokerBenchmarkBase
{
    private MqttApplicationMessage _mqttnetMessage = null!;

    [GlobalSetup]
        public override async Task Setup()
    {
        await base.Setup();
        _mqttnetMessage = new MqttApplicationMessageBuilder()
            .WithTopic($"{TopicPrefix}/qos2")
            .WithPayload(Payload)
            .WithQualityOfServiceLevel(MqttnetQoS.ExactlyOnce)
            .Build();
    }

    [Benchmark(Baseline = true, Description = "MQTTnet")]
    public Task Mqttnet_Publish() => MqttnetClient.PublishAsync(_mqttnetMessage);

    [Benchmark(Description = "Mqtt.Client")]
    public ValueTask<Mqtt.Client.MqttPublishResult> MqttClient_Publish()
        => OurClient.PublishAsync($"{TopicPrefix}/qos2", Payload, Mqtt.Client.MqttQoS.ExactlyOnce);
}
