// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client;

/// <summary>MQTT protocol version negotiated with the broker.</summary>
public enum MqttProtocolVersion
{
    /// <summary>MQTT 3.1.1 (OASIS).</summary>
    V311 = 4,

    /// <summary>MQTT 5.0 (OASIS).</summary>
    V500 = 5,
}

/// <summary>Quality of Service level for a PUBLISH.</summary>
public enum MqttQoS : byte
{
    /// <summary>At most once delivery (fire and forget).</summary>
    AtMostOnce = 0,

    /// <summary>At least once delivery (acknowledged with PUBACK).</summary>
    AtLeastOnce = 1,

    /// <summary>Exactly once delivery (PUBREC/PUBREL/PUBCOMP).</summary>
    ExactlyOnce = 2,
}

/// <summary>Current connection state of the client.</summary>
public enum MqttConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Disposed = 4,
}
