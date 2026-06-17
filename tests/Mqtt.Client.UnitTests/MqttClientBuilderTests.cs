// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

public class MqttClientBuilderTests
{
    [Test]
    public async Task ParsesMqttScheme()
    {
        var c = MqttClient.CreateBuilder().ConnectTo("mqtt://example.com").Build();
        await using var _ = c;
        await Assert.That(c.State).IsEqualTo(MqttConnectionState.Disconnected);
    }

    [Test]
    public async Task ParsesMqttsSchemeWithCustomPort()
    {
        var c = MqttClient.CreateBuilder().ConnectTo("mqtts://example.com:1234").Build();
        await using var _ = c;
        await Assert.That(c.State).IsEqualTo(MqttConnectionState.Disconnected);
    }

    [Test]
    public async Task FluentChain_BuildsClient()
    {
        var c = MqttClient.CreateBuilder()
            .ConnectTo("mqtt://broker")
            .WithClientId("cid")
            .WithCredentials("u", "p")
            .WithKeepAlive(30)
            .WithProtocol(MqttProtocolVersion.V500)
            .Build();
        await using var _ = c;
        await Assert.That(c).IsNotNull();
    }
}
