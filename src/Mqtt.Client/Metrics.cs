// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Mqtt.Client;

/// <summary>
/// System.Diagnostics.Metrics instrumentation for Mqtt.Client. All instruments are published on the
/// <see cref="MeterName"/> meter; observable gauges are pull-based (read on demand by a listener) so
/// they add no hot-path cost.
/// </summary>
internal sealed class MqttMetrics : IDisposable
{
    public const string MeterName = "Mqtt.Client";

    private readonly Meter _meter;

    public MqttMetrics()
    {
        _meter = new Meter(MeterName);
        Publishes = _meter.CreateCounter<long>(
            "mqtt.client.publishes",
            unit: "{packets}",
            description: "Number of PUBLISH packets sent.");
        Receives = _meter.CreateCounter<long>(
            "mqtt.client.receives",
            unit: "{packets}",
            description: "Number of PUBLISH packets received.");
        BytesSent = _meter.CreateCounter<long>(
            "mqtt.client.bytes.sent",
            unit: "By",
            description: "Bytes written to the transport.");
        BytesReceived = _meter.CreateCounter<long>(
            "mqtt.client.bytes.received",
            unit: "By",
            description: "Bytes read from the transport.");
        Reconnects = _meter.CreateCounter<long>(
            "mqtt.client.reconnects",
            unit: "{events}",
            description: "Number of successful reconnects.");
        ConnectAttempts = _meter.CreateCounter<long>(
            "mqtt.client.connect.attempts",
            unit: "{events}",
            description: "Number of connect attempts (initial and reconnect).");
        ConnectFailures = _meter.CreateCounter<long>(
            "mqtt.client.connect.failures",
            unit: "{events}",
            description: "Number of failed connect attempts. Tagged with 'reason'.");
        Disconnects = _meter.CreateCounter<long>(
            "mqtt.client.disconnects",
            unit: "{events}",
            description: "Number of disconnects. Tagged with 'reason'.");
        Resubscribes = _meter.CreateCounter<long>(
            "mqtt.client.resubscribes",
            unit: "{subscriptions}",
            description: "Subscriptions re-sent after a non-session-present reconnect.");
        KeepAlivePings = _meter.CreateCounter<long>(
            "mqtt.client.keepalive.pings",
            unit: "{packets}",
            description: "Number of PINGREQ packets sent.");
        KeepAliveTimeouts = _meter.CreateCounter<long>(
            "mqtt.client.keepalive.timeouts",
            unit: "{events}",
            description: "Number of keep-alive read-idle timeouts (no PINGRESP in time).");
        MessagesDropped = _meter.CreateCounter<long>(
            "mqtt.client.messages.dropped",
            unit: "{packets}",
            description: "Inbound PUBLISH messages dropped. Tagged with 'reason'.");
        DecodeErrors = _meter.CreateCounter<long>(
            "mqtt.client.decode.errors",
            unit: "{events}",
            description: "Number of inbound packet decode/protocol errors.");
        PublishAckLatency = _meter.CreateHistogram<double>(
            "mqtt.client.publish.ack.duration",
            unit: "ms",
            description: "Round-trip time for a QoS>0 PUBLISH ack.");
        ConnectDuration = _meter.CreateHistogram<double>(
            "mqtt.client.connect.duration",
            unit: "ms",
            description: "Time for a connect handshake to complete (CONNECT to CONNACK).");
        RecoveryDuration = _meter.CreateHistogram<double>(
            "mqtt.client.recovery.duration",
            unit: "ms",
            description: "Time from an unexpected disconnect to a successful reconnect.");
    }

    public Counter<long> Publishes { get; }
    public Counter<long> Receives { get; }
    public Counter<long> BytesSent { get; }
    public Counter<long> BytesReceived { get; }
    public Counter<long> Reconnects { get; }
    public Counter<long> ConnectAttempts { get; }
    public Counter<long> ConnectFailures { get; }
    public Counter<long> Disconnects { get; }
    public Counter<long> Resubscribes { get; }
    public Counter<long> KeepAlivePings { get; }
    public Counter<long> KeepAliveTimeouts { get; }
    public Counter<long> MessagesDropped { get; }
    public Counter<long> DecodeErrors { get; }
    public Histogram<double> PublishAckLatency { get; }
    public Histogram<double> ConnectDuration { get; }
    public Histogram<double> RecoveryDuration { get; }

    /// <summary>
    /// Registers the pull-based observable gauges that read live client state. Called once after the
    /// owning client's fields are initialized. Each provider must be cheap and thread-safe; the
    /// runtime invokes them only when a listener records measurements.
    /// </summary>
    public void BindGauges(
        Func<int> connectionState,
        Func<int> pendingAcks,
        Func<int> inflightPublishes,
        Func<int> outboundQueueDepth,
        Func<int> subscriptions)
    {
        _meter.CreateObservableGauge(
            "mqtt.client.connection.state",
            connectionState,
            description: "Current connection state (see MqttConnectionState).");
        _meter.CreateObservableGauge(
            "mqtt.client.pending.acks",
            pendingAcks,
            unit: "{packets}",
            description: "In-flight packets awaiting an ack (QoS>0 publish / SUBSCRIBE / etc.).");
        _meter.CreateObservableGauge(
            "mqtt.client.inflight.publishes",
            inflightPublishes,
            unit: "{packets}",
            description: "Outbound QoS>0 publishes currently using a Receive-Maximum slot.");
        _meter.CreateObservableGauge(
            "mqtt.client.outbound.queue.depth",
            outboundQueueDepth,
            unit: "{packets}",
            description: "Packets queued in the outbound channel awaiting the write loop.");
        _meter.CreateObservableGauge(
            "mqtt.client.subscriptions",
            subscriptions,
            unit: "{subscriptions}",
            description: "Number of live subscriptions tracked by the client.");
    }

    public static KeyValuePair<string, object?> Reason(string reason)
        => new("reason", reason);

    public void Dispose() => _meter.Dispose();
}
