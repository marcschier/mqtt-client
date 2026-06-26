// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;

namespace Mqtt.Client.Testing;

/// <summary>Thrown on a malformed packet; the broker responds by closing the connection.</summary>
internal sealed class MqttBrokerProtocolException : Exception
{
    public MqttBrokerProtocolException(string message) : base(message) { }
}
