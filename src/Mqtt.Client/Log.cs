// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Mqtt.Client;

/// <summary>
/// Source-generated logging for Mqtt.Client.
/// </summary>
internal static partial class MqttLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "MQTT connecting to {Endpoint} (protocol {ProtocolVersion}, id={ClientId})")]
    public static partial void Connecting(
        ILogger logger,
        string endpoint,
        MqttProtocolVersion protocolVersion,
        string clientId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "MQTT connected to {Endpoint} (sessionPresent={SessionPresent})")]
    public static partial void Connected(ILogger logger, string endpoint, bool sessionPresent);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "MQTT disconnected from {Endpoint} ({Reason})")]
    public static partial void Disconnected(ILogger logger, string? endpoint, string reason);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Debug,
        Message = "MQTT publishing topic={Topic} qos={QoS} payloadBytes={PayloadBytes}")]
    public static partial void Publishing(
        ILogger logger,
        string topic,
        MqttQoS qoS,
        int payloadBytes);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Debug,
        Message = "MQTT subscribing to {Filter} qos={QoS}")]
    public static partial void Subscribing(ILogger logger, string filter, MqttQoS qoS);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "MQTT reconnect attempt {Attempt} in {DelayMs}ms")]
    public static partial void Reconnecting(ILogger logger, int attempt, double delayMs);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "MQTT connection loop failed: {Reason}")]
    public static partial void ConnectionLoopFailed(
        ILogger logger,
        Exception exception,
        string reason);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Trace,
        Message = "MQTT received packet type={PacketType} bytes={Bytes}")]
    public static partial void ReceivedPacket(ILogger logger, string packetType, int bytes);
}
