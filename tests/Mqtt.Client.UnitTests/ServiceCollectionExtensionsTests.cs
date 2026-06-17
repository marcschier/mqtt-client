// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mqtt.Client.DependencyInjection;
using Mqtt.Client.Persistence;

namespace Mqtt.Client.UnitTests;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public async Task AddMqttClient_registers_singleton()
    {
        var services = new ServiceCollection();
        services.AddMqttClient(o => { o.Host = "broker"; o.ClientId = "id"; });
        await using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<MqttClient>();
        await Assert.That(client).IsNotNull();
        var client2 = sp.GetRequiredService<MqttClient>();
        await Assert.That(client2).IsSameReferenceAs(client);
    }

    [Test]
    public async Task AddMqttClient_binds_options()
    {
        var services = new ServiceCollection();
        services.AddMqttClient(o => { o.Host = "h"; o.Port = 8888; o.KeepAliveSeconds = 30; });
        await using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<MqttClientOptions>>().Value;
        await Assert.That(opts.Host).IsEqualTo("h");
        await Assert.That(opts.Port).IsEqualTo(8888);
        await Assert.That(opts.KeepAliveSeconds).IsEqualTo((ushort)30);
    }

    [Test]
    public async Task AddMqttClient_throws_on_null_services()
    {
        await Assert.That(() => MqttClientServiceCollectionExtensions.AddMqttClient(null!, _ => { }))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddMqttClient_throws_on_null_configure()
    {
        await Assert.That(() => new ServiceCollection().AddMqttClient(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddMqttClient_uses_user_supplied_persistence()
    {
        var services = new ServiceCollection();
        var custom = new InMemorySessionStore();
        services.AddSingleton<IPersistentSessionStore>(custom);
        services.AddMqttClient(o => o.Host = "h");
        await using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<MqttClient>();
        await Assert.That(client).IsNotNull();
    }
}
