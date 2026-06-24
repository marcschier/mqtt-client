// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mqtt.Client;

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

/// <summary>
/// Optional companion to <see cref="IPersistentSessionStore"/> that also persists inbound QoS 2
/// receipt state — packet identifiers received in a PUBLISH but not yet released by a PUBREL — so
/// exactly-once de-duplication survives a Session-Present reconnect. A store implementing this is
/// consulted by the client alongside the base interface; <see cref="IPersistentSessionStore.ClearAsync"/>
/// also clears this state.
/// </summary>
public interface IPersistentInboundQoS2Store
{
    ValueTask SaveReceivedQoS2Async(ushort packetId);
    ValueTask RemoveReceivedQoS2Async(ushort packetId);
    ValueTask<IReadOnlyList<ushort>> ListReceivedQoS2Async();
}

internal sealed class InMemorySessionStore : IPersistentSessionStore, IPersistentInboundQoS2Store
{
    private readonly Dictionary<ushort, MqttMessage> _pending = new();
    private readonly HashSet<ushort> _receivedQoS2 = new();
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

    public ValueTask SaveReceivedQoS2Async(ushort packetId)
    {
        lock (_gate) { _receivedQoS2.Add(packetId); }
        return default;
    }

    public ValueTask RemoveReceivedQoS2Async(ushort packetId)
    {
        lock (_gate) { _receivedQoS2.Remove(packetId); }
        return default;
    }

    public ValueTask<IReadOnlyList<ushort>> ListReceivedQoS2Async()
    {
        lock (_gate)
        {
            var list = new List<ushort>(_receivedQoS2);
            return new ValueTask<IReadOnlyList<ushort>>(list);
        }
    }

    public ValueTask ClearAsync()
    {
        lock (_gate)
        {
            _pending.Clear();
            _receivedQoS2.Clear();
        }
        return default;
    }
}
