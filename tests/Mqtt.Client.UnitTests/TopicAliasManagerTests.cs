// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

public class TopicAliasManagerTests
{
    [Test]
    public async Task FirstPublish_AssignsAlias_KeepsTopic()
    {
        var m = new TopicAliasManager(maxAlias: 10);
        var (topic, alias) = m.Resolve("sensors/temp");
        await Assert.That(topic).IsEqualTo("sensors/temp");
        await Assert.That(alias).IsEqualTo((ushort?)1);
    }

    [Test]
    public async Task SecondPublish_ReturnsEmptyTopic_SameAlias()
    {
        var m = new TopicAliasManager(maxAlias: 10);
        m.Resolve("a/b");
        var (topic, alias) = m.Resolve("a/b");
        await Assert.That(topic).IsEqualTo(string.Empty);
        await Assert.That(alias).IsEqualTo((ushort?)1);
    }

    [Test]
    public async Task MaxZero_DisablesAliasing()
    {
        var m = new TopicAliasManager(maxAlias: 0);
        var (topic, alias) = m.Resolve("x");
        await Assert.That(topic).IsEqualTo("x");
        await Assert.That(alias).IsNull();
    }

    [Test]
    public async Task BeyondMax_NoAlias()
    {
        var m = new TopicAliasManager(maxAlias: 1);
        m.Resolve("a");
        var (topic, alias) = m.Resolve("b");
        await Assert.That(topic).IsEqualTo("b");
        await Assert.That(alias).IsNull();
    }
}
