// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Mqtt.Client;

/// <summary>
/// Manages MQTT 5 outbound topic aliases. The first time a topic is published, the manager assigns
/// a new alias up to <see cref="MaxAlias"/>; subsequent publishes to the same topic publish the
/// alias number plus an empty topic name (per MQTT 5 §3.3.2.3.4).
/// </summary>
internal sealed class TopicAliasManager
{
    private readonly Dictionary<string, ushort> _aliases = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private ushort _next = 1;

    public TopicAliasManager(ushort maxAlias)
    {
        MaxAlias = maxAlias;
    }

    public ushort MaxAlias { get; }

    /// <summary>
    /// Resolves the outgoing topic/alias pair. If an alias already exists for the topic, the
    /// returned topic is the empty string (the broker honors the existing alias). If a new alias
    /// is allocated, the topic is unchanged so the broker can record it.
    /// </summary>
    public (string TopicToSend, ushort? Alias) Resolve(string topic)
    {
        if (MaxAlias == 0) return (topic, null);
        lock (_gate)
        {
            if (_aliases.TryGetValue(topic, out var existing))
            {
                return (string.Empty, existing);
            }
            if (_next > MaxAlias)
            {
                return (topic, null);
            }
            var allocated = _next++;
            _aliases[topic] = allocated;
            return (topic, allocated);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _aliases.Clear();
            _next = 1;
        }
    }
}
