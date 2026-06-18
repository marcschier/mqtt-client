// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

public class InMemorySessionStoreTests
{
    [Test]
    public async Task Save_then_List_returns_message()
    {
        var store = new InMemorySessionStore();
        var msg = new MqttMessage { Topic = "t", PayloadMemory = new byte[] { 1 } };
        await store.SavePendingPublishAsync(7, msg);
        var list = await store.ListPendingPublishesAsync();
        await Assert.That(list.Count).IsEqualTo(1);
        await Assert.That(list[0].PacketId).IsEqualTo((ushort)7);
        await Assert.That(list[0].Message.Topic).IsEqualTo("t");
    }

    [Test]
    public async Task Remove_drops_entry()
    {
        var store = new InMemorySessionStore();
        await store.SavePendingPublishAsync(
            1,
            new MqttMessage { Topic = "t", PayloadMemory = ReadOnlyMemory<byte>.Empty });
        await store.RemovePendingPublishAsync(1);
        var list = await store.ListPendingPublishesAsync();
        await Assert.That(list.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Clear_empties_store()
    {
        var store = new InMemorySessionStore();
        for (ushort i = 1; i <= 5; i++)
        {
            await store.SavePendingPublishAsync(
                i,
                new MqttMessage { Topic = "t", PayloadMemory = ReadOnlyMemory<byte>.Empty });
        }
        await store.ClearAsync();
        var list = await store.ListPendingPublishesAsync();
        await Assert.That(list.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Save_overwrites_existing_packet_id()
    {
        var store = new InMemorySessionStore();
        await store.SavePendingPublishAsync(
            3,
            new MqttMessage { Topic = "first", PayloadMemory = ReadOnlyMemory<byte>.Empty });
        await store.SavePendingPublishAsync(
            3,
            new MqttMessage { Topic = "second", PayloadMemory = ReadOnlyMemory<byte>.Empty });
        var list = await store.ListPendingPublishesAsync();
        await Assert.That(list.Count).IsEqualTo(1);
        await Assert.That(list[0].Message.Topic).IsEqualTo("second");
    }
}
