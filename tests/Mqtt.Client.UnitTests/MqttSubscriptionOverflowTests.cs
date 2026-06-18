// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

public class MqttSubscriptionOverflowTests
{
    [Test]
    public async Task DropOldest_drops_first_message_when_full()
    {
        var sub = TestSubscription.Create(
            "a",
            new MqttSubscriptionOptions { Capacity = 2, Overflow = MqttOverflowMode.DropOldest });
        sub.Writer.TryWrite(new MqttMessage { Topic = "a", PayloadMemory = new byte[] { 1 } });
        sub.Writer.TryWrite(new MqttMessage { Topic = "a", PayloadMemory = new byte[] { 2 } });
        sub.Writer.TryWrite(new MqttMessage { Topic = "a", PayloadMemory = new byte[] { 3 } });
        sub.Writer.TryComplete();

        var seen = new List<byte>();
        await foreach (var m in sub.Reader.ReadAllAsync())
        {
            seen.Add(m.PayloadMemory.Span[0]);
        }
        await Assert.That(seen.Count).IsEqualTo(2);
        await Assert.That(seen[0]).IsEqualTo((byte)2);  // '1' was dropped (oldest)
        await Assert.That(seen[1]).IsEqualTo((byte)3);
    }

    [Test]
    public async Task DropNewest_drops_most_recent_existing_when_full()
    {
        // System.Threading.Channels.BoundedChannelFullMode.DropNewest semantics:
        // "remove and ignore the newest item in the channel" — i.e. the most recently added
        // existing item is dropped, then the new item is appended.
        var sub = TestSubscription.Create(
            "a",
            new MqttSubscriptionOptions { Capacity = 2, Overflow = MqttOverflowMode.DropNewest });
        sub.Writer.TryWrite(new MqttMessage { Topic = "a", PayloadMemory = new byte[] { 1 } });
        sub.Writer.TryWrite(new MqttMessage { Topic = "a", PayloadMemory = new byte[] { 2 } });
        sub.Writer.TryWrite(new MqttMessage { Topic = "a", PayloadMemory = new byte[] { 3 } });
        sub.Writer.TryComplete();

        var seen = new List<byte>();
        await foreach (var m in sub.Reader.ReadAllAsync())
        {
            seen.Add(m.PayloadMemory.Span[0]);
        }
        await Assert.That(seen.Count).IsEqualTo(2);
        await Assert.That(seen[0]).IsEqualTo((byte)1);
        await Assert.That(seen[1]).IsEqualTo((byte)3);  // '2' was dropped (newest existing)
    }

    [Test]
    public async Task DisposeAsync_is_idempotent()
    {
        var sub = TestSubscription.Create("x", new MqttSubscriptionOptions());
        await sub.DisposeAsync();
        await sub.DisposeAsync();   // second dispose is a no-op
        await Assert.That(sub.TopicFilter).IsEqualTo("x");
    }

    [Test]
    public async Task Invalid_overflow_mode_throws()
    {
        await Assert.That(() => TestSubscription.Create("x",
            new MqttSubscriptionOptions { Overflow = (MqttOverflowMode)99 }))
            .Throws<ArgumentOutOfRangeException>();
    }
}
