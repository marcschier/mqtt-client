// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;

namespace Mqtt.Client;

/// <summary>
/// Exception thrown for MQTT protocol violations (malformed packets, reason codes, etc.).
/// </summary>
public sealed class MqttProtocolException : Exception
{
    public MqttProtocolException(string message) : base(message) { }
    public MqttProtocolException(
        string message,
        Exception innerException) : base(message, innerException) { }
}

/// <summary>Exception thrown when the connection is closed unexpectedly or by the broker.</summary>
public sealed class MqttConnectionException : Exception
{
    public MqttConnectionException(string message) : base(message) { }
    public MqttConnectionException(
        string message,
        Exception innerException) : base(message, innerException) { }
}
