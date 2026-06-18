// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mqtt.Client.Persistence;

/// <summary>
/// Pluggable store for QoS&gt;0 in-flight publishes and pending subscriptions across reconnects.
/// </summary>
public interface IPersistentSessionStore
{
    ValueTask SavePendingPublishAsync(ushort packetId, MqttMessage message);
    ValueTask RemovePendingPublishAsync(ushort packetId);
    ValueTask<IReadOnlyList<(ushort PacketId, MqttMessage Message)>>
        ListPendingPublishesAsync();
    ValueTask ClearAsync();
}

internal sealed class InMemorySessionStore : IPersistentSessionStore
{
    private readonly Dictionary<ushort, MqttMessage> _pending = new();
    private readonly object _gate = new();

    public ValueTask SavePendingPublishAsync(ushort packetId, MqttMessage message)
    {
        lock (_gate) { _pending[packetId] = message; }
        return default;
    }

    public ValueTask RemovePendingPublishAsync(ushort packetId)
    {
        lock (_gate) { _pending.Remove(packetId); }
        return default;
    }

    public ValueTask<IReadOnlyList<(ushort PacketId, MqttMessage Message)>>
        ListPendingPublishesAsync()
    {
        lock (_gate)
        {
            var list = new List<(ushort, MqttMessage)>(_pending.Count);
            foreach (var kv in _pending) list.Add((kv.Key, kv.Value));
            return new ValueTask<IReadOnlyList<(ushort, MqttMessage)>>(list);
        }
    }

    public ValueTask ClearAsync()
    {
        lock (_gate) { _pending.Clear(); }
        return default;
    }
}
