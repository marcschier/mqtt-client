// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;

namespace Mqtt.Client;

/// <summary>
/// Per-subscription options.
/// </summary>
public sealed class MqttSubscriptionOptions
{
    /// <summary>
    /// Maximum QoS requested for this subscription.
    /// </summary>
    public MqttQoS QoS { get; init; } = MqttQoS.AtMostOnce;

    /// <summary>
    /// Channel capacity (must be &gt; 0).
    /// </summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>
    /// Overflow behavior when the consumer can't keep up.
    /// </summary>
    public MqttOverflowMode Overflow { get; init; } = MqttOverflowMode.Wait;

    /// <summary>
    /// MQTT 5 NoLocal flag.
    /// </summary>
    public bool NoLocal { get; init; }

    /// <summary>
    /// MQTT 5 RetainAsPublished flag.
    /// </summary>
    public bool RetainAsPublished { get; init; }
}

/// <summary>
/// Overflow policy for the inbound subscription channel.
/// </summary>
public enum MqttOverflowMode
{
    /// <summary>
    /// Apply backpressure (writer suspends, propagates to TCP receive window).
    /// </summary>
    Wait = 0,

    /// <summary>
    /// Drop oldest unread message to make room.
    /// </summary>
    DropOldest = 1,

    /// <summary>
    /// Drop the incoming message.
    /// </summary>
    DropNewest = 2,
}
