// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Mqtt.Client.Benchmarks.Infrastructure;
using MQTTnet;
using MqttnetProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion;
using MqttnetQoS = MQTTnet.Protocol.MqttQualityOfServiceLevel;
using MqttnetClient = MQTTnet.IMqttClient;
using MqttnetClientFactory = MQTTnet.MqttClientFactory;

namespace Mqtt.Client.Benchmarks.EndToEnd;

/// <summary>
/// Base class wiring up an in-process MQTTnet broker and a paired Mqtt.Client / MQTTnet client
/// connected with carefully matched options. Derived benchmark classes implement Mqttnet_/MqttClient_
/// methods that perform a single operation per invocation.
/// </summary>
public abstract class BrokerBenchmarkBase
{
    protected InProcessBroker Broker = null!;
    protected MqttnetClient MqttnetClient = null!;
    protected Mqtt.Client.MqttClient OurClient = null!;

    [Params(64, 256, 1024, 4096, 16384, 65536)]
    public int PayloadSize { get; set; }

    protected byte[] Payload = null!;
    protected const string TopicPrefix = "bench/e2e";

    public virtual async Task Setup()
    {
        Payload = new byte[PayloadSize];
        new Random(42).NextBytes(Payload);
        Broker = await InProcessBroker.StartAsync();

        // MQTTnet client
        MqttnetClient = new MqttnetClientFactory().CreateMqttClient();
        var nopts = new MqttClientOptionsBuilder()
            .WithTcpServer(Broker.Host, Broker.Port)
            .WithProtocolVersion(MqttnetProtocolVersion.V500)
            .WithClientId($"bench-mqttnet-{Guid.NewGuid():N}")
            .WithCleanStart(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
            .Build();
        await MqttnetClient.ConnectAsync(nopts);

        // Mqtt.Client
        OurClient = Mqtt.Client.MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://{Broker.Host}:{Broker.Port}")
            .WithClientId($"bench-ours-{Guid.NewGuid():N}")
            .WithProtocol(Mqtt.Client.MqttProtocolVersion.V500)
            .WithCleanStart(true)
            .WithKeepAlive(60)
            .WithReconnect(null)
            .Build();
        await OurClient.ConnectAsync();

        await OnConnectedAsync();
    }

    protected virtual Task OnConnectedAsync() => Task.CompletedTask;

    public virtual async Task Cleanup()
    {
        try { await MqttnetClient.DisconnectAsync(); } catch { }
        MqttnetClient.Dispose();
        await OurClient.DisposeAsync();
        await Broker.DisposeAsync();
    }
}
