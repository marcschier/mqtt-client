// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Diagnostics.Metrics;

namespace Mqtt.Client.Diagnostics;

/// <summary>System.Diagnostics.Metrics instrumentation for Mqtt.Client.</summary>
internal sealed class MqttMetrics : IDisposable
{
    public const string MeterName = "Mqtt.Client";

    private readonly Meter _meter;

    public MqttMetrics()
    {
        _meter = new Meter(MeterName);
        Publishes = _meter.CreateCounter<long>("mqtt.client.publishes", unit: "{packets}", description: "Number of PUBLISH packets sent.");
        Receives = _meter.CreateCounter<long>(
            "mqtt.client.receives",
            unit: "{packets}",
            description: "Number of PUBLISH packets received.");
        BytesSent = _meter.CreateCounter<long>("mqtt.client.bytes.sent", unit: "By", description: "Bytes written to the transport.");
        BytesReceived = _meter.CreateCounter<long>("mqtt.client.bytes.received", unit: "By", description: "Bytes read from the transport.");
        Reconnects = _meter.CreateCounter<long>("mqtt.client.reconnects", unit: "{events}", description: "Number of reconnects.");
        PublishAckLatency = _meter.CreateHistogram<double>(
            "mqtt.client.publish.ack.duration",
            unit: "ms",
            description: "Round-trip time for a QoS>0 PUBLISH ack.");
    }

    public Counter<long> Publishes { get; }
    public Counter<long> Receives { get; }
    public Counter<long> BytesSent { get; }
    public Counter<long> BytesReceived { get; }
    public Counter<long> Reconnects { get; }
    public Histogram<double> PublishAckLatency { get; }

    public void Dispose() => _meter.Dispose();
}
