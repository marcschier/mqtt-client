// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using MQTTnet;
using MqttnetProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion;
using MqttnetClientFactory = MQTTnet.MqttClientFactory;

namespace Mqtt.Client.Benchmarks;

/// <summary>
/// Connect + Disconnect latency. New client per iteration to capture full handshake cost.
/// </summary>
// Connect/disconnect is resource-heavy (a TCP handshake + an ephemeral local port per cycle).
// A bounded, fixed invocation count keeps both clients well under the OS ephemeral-port range so
// the benchmark measures latency rather than failing with port exhaustion (SocketException 10048);
// a few hundred samples are ample for a millisecond-scale operation.
[WarmupCount(3)]
[IterationCount(10)]
[InvocationCount(16, 1)]
public class ConnectLatencyBenchmark
{
    private InProcessBroker _broker = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _broker = await InProcessBroker.StartAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _broker.DisposeAsync();

    [Benchmark(Baseline = true, Description = "MQTTnet")]
    public async Task Mqttnet_Connect()
    {
        var client = new MqttnetClientFactory().CreateMqttClient();
        var opts = new MqttClientOptionsBuilder()
            .WithTcpServer(_broker.Host, _broker.Port)
            .WithProtocolVersion(MqttnetProtocolVersion.V500)
            .WithClientId($"bench-conn-mqttnet-{Guid.NewGuid():N}")
            .Build();
        await client.ConnectAsync(opts);
        await client.DisconnectAsync();
        client.Dispose();
    }

    [Benchmark(Description = "Mqtt.Client")]
    public async Task MqttClient_Connect()
    {
        await using var client = Mqtt.Client.MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://{_broker.Host}:{_broker.Port}")
            .WithClientId($"bench-conn-ours-{Guid.NewGuid():N}")
            .WithProtocol(Mqtt.Client.MqttProtocolVersion.V500)
            .WithReconnect(null)
            .Build();
        await client.ConnectAsync();
        await client.DisconnectAsync();
    }
}
