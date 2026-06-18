// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using Mqtt.Client.Persistence;

namespace Mqtt.Client.UnitTests;

public class FileSessionStoreTests
{
    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "mqttclient-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Test]
    public async Task Save_and_list_roundtrips_message()
    {
        var dir = NewDir();
        try
        {
            var store = new FileSessionStore(dir);
            var msg = new MqttMessage { Topic = "a/b", Payload = new byte[] { 1, 2, 3 }, QoS = MqttQoS.AtLeastOnce, Retain = true };
            await store.SavePendingPublishAsync(42, msg);

            var list = await store.ListPendingPublishesAsync();
            await Assert.That(list.Count).IsEqualTo(1);
            await Assert.That(list[0].PacketId).IsEqualTo((ushort)42);
            await Assert.That(list[0].Message.Topic).IsEqualTo("a/b");
            await Assert.That(list[0].Message.QoS).IsEqualTo(MqttQoS.AtLeastOnce);
            await Assert.That(list[0].Message.Retain).IsTrue();
            await Assert.That(list[0].Message.Payload.ToArray().AsSpan().SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Remove_and_clear_remove_files()
    {
        var dir = NewDir();
        try
        {
            var store = new FileSessionStore(dir);
            await store.SavePendingPublishAsync(1, new MqttMessage { Topic = "t1", Payload = new byte[] { 1 } });
            await store.SavePendingPublishAsync(2, new MqttMessage { Topic = "t2", Payload = new byte[] { 2 } });

            await store.RemovePendingPublishAsync(1);
            var list = await store.ListPendingPublishesAsync();
            await Assert.That(list.Count).IsEqualTo(1);
            await Assert.That(list[0].PacketId).IsEqualTo((ushort)2);

            await store.ClearAsync();
            list = await store.ListPendingPublishesAsync();
            await Assert.That(list.Count).IsEqualTo(0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Survives_recreation_of_store_instance()
    {
        var dir = NewDir();
        try
        {
            var s1 = new FileSessionStore(dir);
            await s1.SavePendingPublishAsync(7, new MqttMessage { Topic = "persist", Payload = new byte[] { 9, 9 } });

            var s2 = new FileSessionStore(dir);
            var list = await s2.ListPendingPublishesAsync();
            await Assert.That(list.Count).IsEqualTo(1);
            await Assert.That(list[0].Message.Topic).IsEqualTo("persist");
            await Assert.That(list[0].Message.Payload.ToArray().AsSpan().SequenceEqual(new byte[] { 9, 9 })).IsTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
