// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using MQTTnet;
using MqttnetProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion;
using MqttnetQoS = MQTTnet.Protocol.MqttQualityOfServiceLevel;
using MqttnetClient = MQTTnet.IMqttClient;
using MqttnetClientFactory = MQTTnet.MqttClientFactory;

namespace Mqtt.Client.Benchmarks;

/// <summary>
/// Subscribes (via the client under test) then publishes via a separate publisher connection and
/// measures end-to-end receive throughput. Both the MQTTnet and Mqtt.Client paths use a
/// pre-built application message so the only work measured per iteration is publish + receive.
/// </summary>
public class SubscribeReceiveBenchmark : BrokerBenchmarkBase
{
    private MqttnetClient _publisher = null!;
    private MqttApplicationMessage _publishMessageMqttnet = null!;
    private MqttApplicationMessage _publishMessageOurs = null!;
    private Mqtt.Client.MqttSubscription _ourSub = null!;
    private Channel<int> _mqttnetReceived = null!;

    private string TopicMqttnet => $"{TopicPrefix}/recv/mqttnet";
    private string TopicOurs => $"{TopicPrefix}/recv/ours";

    [GlobalSetup]
    public override async Task Setup()
    {
        await base.Setup();

        _publisher = new MqttnetClientFactory().CreateMqttClient();
        await _publisher.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(Broker.Host, Broker.Port)
            .WithProtocolVersion(MqttnetProtocolVersion.V500)
            .WithClientId($"bench-pub-{Guid.NewGuid():N}")
            .WithMaximumPacketSize((uint)MaxPacket)
            .WithCleanStart(true).Build());

        _publishMessageMqttnet = new MqttApplicationMessageBuilder()
            .WithTopic(TopicMqttnet).WithPayload(Payload)
            .WithQualityOfServiceLevel(MqttnetQoS.AtMostOnce).Build();
        _publishMessageOurs = new MqttApplicationMessageBuilder()
            .WithTopic(TopicOurs).WithPayload(Payload)
            .WithQualityOfServiceLevel(MqttnetQoS.AtMostOnce).Build();

        _mqttnetReceived = Channel.CreateUnbounded<int>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        MqttnetClient.ApplicationMessageReceivedAsync += async ev =>
        {
            _mqttnetReceived.Writer.TryWrite(0);
            await Task.CompletedTask;
        };
        await MqttnetClient.SubscribeAsync(TopicMqttnet, MqttnetQoS.AtMostOnce);

        _ourSub = await OurClient.SubscribeAsync(TopicOurs,
            new Mqtt.Client.MqttSubscriptionOptions {
                QoS = Mqtt.Client.MqttQoS.AtMostOnce,
                Capacity = 4096 });
    }

    [Benchmark(Baseline = true, Description = "MQTTnet receive")]
    public async Task Mqttnet_Receive()
    {
        await _publisher.PublishAsync(_publishMessageMqttnet);
        await _mqttnetReceived.Reader.ReadAsync();
    }

    [Benchmark(Description = "Mqtt.Client receive")]
    public async Task MqttClient_Receive()
    {
        await _publisher.PublishAsync(_publishMessageOurs);
        await _ourSub.Reader.ReadAsync();
    }

    [GlobalCleanup]
    public override async Task Cleanup()
    {
        try { await _publisher.DisconnectAsync(); } catch { }
        _publisher.Dispose();
        await _ourSub.DisposeAsync();
        await base.Cleanup();
    }
}
