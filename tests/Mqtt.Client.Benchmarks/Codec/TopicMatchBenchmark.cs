// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using BenchmarkDotNet.Attributes;

namespace Mqtt.Client.Benchmarks;

/// <summary>
/// Measures <see cref="TopicFilterTrie{T}"/> match cost and allocation. <c>Match</c> runs for every
/// inbound PUBLISH on MQTT 3.1.1 (and v5 without echoed subscription identifiers); the
/// MemoryDiagnoser <c>Allocated</c> column shows whether per-segment key strings are allocated on
/// the match hot path. The collector delegate is cached in setup so only the trie's own allocation
/// is measured (no per-call closure).
/// </summary>
public class TopicMatchBenchmark
{
    private static readonly object Value = new();
    private readonly TopicFilterTrie<object> _trie = new();
    private Action<object> _collect = null!;
    private int _count;

    [Params("sensors/floor1/room2/temp", "a/b/c/d/e/f/g/h")]
    public string Topic { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _collect = _ => _count++;
        string[] filters =
        {
            "sensors/+/+/temp",
            "sensors/floor1/#",
            "sensors/floor1/room2/temp",
            "a/b/c/d/e/f/g/h",
            "a/+/c/+/e/+/g/+",
            "devices/+/status",
            "alerts/#",
        };
        foreach (var filter in filters)
        {
            _trie.Add(filter, Value);
        }
    }

    [Benchmark]
    public int Match()
    {
        _count = 0;
        _trie.Match(Topic, _collect);
        return _count;
    }
}
