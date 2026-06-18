// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

public class SharedSubscriptionTests
{
    [Test]
    public async Task StripSharedSubscriptionPrefix_returns_topic_for_share_filter()
    {
        var s = MqttClient.StripSharedSubscriptionPrefix("$share/group1/sensors/+/temp");
        await Assert.That(s).IsEqualTo("sensors/+/temp");
    }

    [Test]
    public async Task StripSharedSubscriptionPrefix_passes_through_normal_filter()
    {
        var s = MqttClient.StripSharedSubscriptionPrefix("sensors/+/temp");
        await Assert.That(s).IsEqualTo("sensors/+/temp");
    }

    [Test]
    public async Task StripSharedSubscriptionPrefix_returns_input_for_malformed_share()
    {
        var s = MqttClient.StripSharedSubscriptionPrefix("$share/onlygroup");
        await Assert.That(s).IsEqualTo("$share/onlygroup");
    }
}
