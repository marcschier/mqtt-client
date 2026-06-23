// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Attributes;

namespace Mqtt.Client.Benchmarks;

/// <summary>
/// Measures the common-path cost of allocating and releasing one MQTT packet identifier. The
/// allocator's bitmap is warmed in setup, so the figure reflects steady-state allocate/release
/// (the word-scan free-bit search) rather than the one-time lazy 8 KB bitmap allocation.
/// </summary>
public class PacketIdAllocatorBenchmark
{
    private readonly PacketIdAllocator _allocator = new();

    [GlobalSetup]
    public void Setup() => _allocator.Release(_allocator.Allocate());

    [Benchmark]
    public ushort AllocateAndRelease()
    {
        var id = _allocator.Allocate();
        _allocator.Release(id);
        return id;
    }
}
