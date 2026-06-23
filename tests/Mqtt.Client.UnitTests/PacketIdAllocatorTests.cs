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

    [Test]
    public async Task AllocatesEntireRangeThenThrows()
    {
        var a = new PacketIdAllocator();
        var ids = new HashSet<ushort>();
        for (var i = 0; i < 65535; i++)
        {
            await Assert.That(ids.Add(a.Allocate())).IsTrue();
        }
        // 65535 distinct non-zero ushorts == exactly the valid range 1..65535 (never 0, never the
        // unused spare bit 65535 in the bitmap).
        await Assert.That(ids.Count).IsEqualTo(65535);
        await Assert.That(ids.Contains((ushort)0)).IsFalse();
        await Assert.That(ids.Contains((ushort)1)).IsTrue();
        await Assert.That(ids.Contains((ushort)65535)).IsTrue();
        await Assert.That(() => a.Allocate()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task WrapsAroundToReuseAReleasedId()
    {
        var a = new PacketIdAllocator();
        for (var i = 0; i < 65535; i++) a.Allocate();
        // Cursor is now past the end; releasing a low id forces the next Allocate to wrap.
        a.Release(1000);
        await Assert.That(a.Allocate()).IsEqualTo((ushort)1000);
    }

    [Test]
    public async Task ReusesReleasedBoundaryIds()
    {
        var a = new PacketIdAllocator();
        for (var i = 0; i < 65535; i++) a.Allocate();
        a.Release(65535); // last valid id, adjacent to the spare bit
        a.Release(1);     // first id
        var got = new HashSet<ushort> { a.Allocate(), a.Allocate() };
        await Assert.That(got.Count).IsEqualTo(2);
        await Assert.That(got.Contains((ushort)1)).IsTrue();
        await Assert.That(got.Contains((ushort)65535)).IsTrue();
        await Assert.That(() => a.Allocate()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ConcurrentAllocationsAreUnique()
    {
        var a = new PacketIdAllocator();
        var bag = new System.Collections.Concurrent.ConcurrentBag<ushort>();
        System.Threading.Tasks.Parallel.For(0, 20000, _ => bag.Add(a.Allocate()));
        var set = new HashSet<ushort>(bag);
        await Assert.That(set.Count).IsEqualTo(20000);
        await Assert.That(set.Contains((ushort)0)).IsFalse();
    }
}
