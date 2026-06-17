// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mqtt.Client.DependencyInjection;
using Mqtt.Client.Transport;
using Mqtt.Client.UnitTests.Fakes;

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Covers <see cref="MqttClientHostedService"/> and the named-client DI overload using the
/// in-process <see cref="FakePipeTransport"/>; no real broker required.
/// </summary>
public class HostedAndNamedClientTests
{
    [Test]
    [Timeout(5_000)]
    public async Task HostedService_connects_on_start_and_disconnects_on_stop(CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var fakeFactory = new FakeTransportFactory();
        services.AddSingleton<IMqttTransportFactory>(fakeFactory);
        services.AddOptions<MqttClientOptions>().Configure(o =>
        {
            o.Host = "fake"; o.ClientId = "h"; o.KeepAliveSeconds = 0; o.Reconnect = null;
        });
        services.AddSingleton<MqttClient>(sp =>
            new MqttClient(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MqttClientOptions>>().Value, fakeFactory));
        services.AddHostedService<MqttClientHostedService>();
        await using var sp = services.BuildServiceProvider();

        var hosted = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>().First();
        var broker = new FakeBroker(fakeFactory.Transport);
        var startTask = hosted.StartAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await startTask;

        var client = sp.GetRequiredService<MqttClient>();
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Connected);

        await hosted.StopAsync(ct);
        await Assert.That(client.State).IsEqualTo(MqttConnectionState.Disconnected);
    }

    [Test]
    public async Task Named_client_factory_resolves_distinct_instances()
    {
        var services = new ServiceCollection();
        services.AddMqttClient("a", o => o.Host = "broker-a");
        services.AddMqttClient("b", o => o.Host = "broker-b");
        await using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IMqttClientFactory>();
        var a = factory.Create("a");
        var b = factory.Create("b");
        await Assert.That(a).IsNotSameReferenceAs(b);
        await Assert.That(factory.Create("a")).IsSameReferenceAs(a);  // cached per name
    }

    [Test]
    public async Task Named_client_factory_throws_on_null_or_empty_name()
    {
        var services = new ServiceCollection();
        services.AddMqttClient("x", _ => { });
        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IMqttClientFactory>();
        await Assert.That(() => factory.Create(null!)).Throws<ArgumentException>();
        await Assert.That(() => factory.Create("")).Throws<ArgumentException>();
    }

    [Test]
    public async Task AddMqttClient_named_throws_on_null_args()
    {
        await Assert.That(() => MqttClientServiceCollectionExtensions.AddMqttClient(null!, "x", _ => { }))
            .Throws<ArgumentNullException>();
        await Assert.That(() => new ServiceCollection().AddMqttClient((string)null!, _ => { }))
            .Throws<ArgumentException>();
        await Assert.That(() => new ServiceCollection().AddMqttClient("x", null!))
            .Throws<ArgumentNullException>();
    }
}
