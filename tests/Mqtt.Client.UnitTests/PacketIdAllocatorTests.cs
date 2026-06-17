// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

public class PacketIdAllocatorTests
{
    [Test]
    public async Task AllocatesUniqueIds()
    {
        var a = new PacketIdAllocator();
        var ids = new HashSet<ushort>();
        for (var i = 0; i < 1000; i++)
        {
            ids.Add(a.Allocate());
        }
        await Assert.That(ids.Count).IsEqualTo(1000);
        await Assert.That(ids.Contains((ushort)0)).IsFalse();
    }

    [Test]
    public async Task ReleaseAllowsReuse()
    {
        var a = new PacketIdAllocator();
        var id = a.Allocate();
        a.Release(id);
        var id2 = a.Allocate();
        await Assert.That(id2).IsNotEqualTo((ushort)0);
    }
}
