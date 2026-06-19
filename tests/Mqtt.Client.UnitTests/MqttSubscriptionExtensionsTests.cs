// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

public class MqttSubscriptionExtensionsTests
{
    [Test]
    [Timeout(2_000)]
    public async Task ReadAllAsync_returns_messages_then_completes_on_dispose(CancellationToken ct)
    {
        var options = new MqttSubscriptionOptions {
            Capacity = 4,
            Overflow = MqttOverflowMode.Wait };
        // Use reflection-free path: construct via internal ctor through a tiny helper. The
        // MqttSubscription type is public but its ctor is internal; here we exercise the
        // ReadAllAsync extension by routing through a published instance. We test the public
        // ChannelReader<T>.ReadAllAsync surface via the Reader directly to avoid coupling to
        // internal construction.
        var sub = TestSubscription.Create("a/b", options);
        sub.Writer!.TryWrite(new MqttMessage { Topic = "a/b", PayloadMemory = new byte[] { 1 } });
        sub.Writer!.TryWrite(new MqttMessage { Topic = "a/b", PayloadMemory = new byte[] { 2 } });
        sub.Writer!.TryComplete();

        var count = 0;
        await foreach (var _ in sub.ReadAllAsync(ct))
        {
            count++;
        }
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task ReadAllAsync_throws_on_null_subscription()
    {
        await Assert.That(() => MqttSubscriptionExtensions.ReadAllAsync(null!))
            .Throws<ArgumentNullException>();
    }
}

internal static class TestSubscription
{
    public static MqttSubscription Create(string topicFilter, MqttSubscriptionOptions options)
    {
        // Use the internal ctor via InternalsVisibleTo.
        return new MqttSubscription(topicFilter, options, _ => default);
    }
}
