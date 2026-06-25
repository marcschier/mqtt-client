// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace Mqtt.Client.ChaosTests;

public sealed class MetricsCollector : IDisposable
{
    private const string MeterName = "Mqtt.Client";

    private static readonly string[] CounterNames =
    [
        "mqtt.client.publishes",
        "mqtt.client.receives",
        "mqtt.client.reconnects",
        "mqtt.client.connect.attempts",
        "mqtt.client.connect.failures",
        "mqtt.client.disconnects",
        "mqtt.client.resubscribes",
        "mqtt.client.keepalive.pings",
        "mqtt.client.keepalive.timeouts",
        "mqtt.client.messages.dropped",
        "mqtt.client.decode.errors",
        "mqtt.client.bytes.sent",
        "mqtt.client.bytes.received",
    ];

    private static readonly string[] HistogramNames =
    [
        "mqtt.client.publish.ack.duration",
        "mqtt.client.connect.duration",
        "mqtt.client.recovery.duration",
    ];

    private static readonly string[] GaugeNames =
    [
        "mqtt.client.connection.state",
        "mqtt.client.pending.acks",
        "mqtt.client.inflight.publishes",
        "mqtt.client.outbound.queue.depth",
        "mqtt.client.subscriptions",
    ];

    private static readonly string[] InstrumentNames = CounterNames
        .Concat(HistogramNames)
        .Concat(GaugeNames)
        .ToArray();

    private static readonly HashSet<string> CounterNameSet = new(CounterNames);
    private static readonly HashSet<string> GaugeNameSet = new(GaugeNames);

    private readonly ConcurrentDictionary<string, double> _counterTotals = new();
    private readonly ConcurrentDictionary<string, double> _latestGauges = new();
    private readonly ConcurrentDictionary<string, double> _latestValues = new();
    private readonly MeterListener _listener;
    private readonly List<MetricsSnapshot> _snapshots = new();
    private readonly object _snapshotGate = new();

    private bool _disposed;

    public MetricsCollector()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = OnInstrumentPublished;
        _listener.SetMeasurementEventCallback<long>(OnMeasurement);
        _listener.SetMeasurementEventCallback<double>(OnMeasurement);
        _listener.SetMeasurementEventCallback<int>(OnMeasurement);
        _listener.Start();
    }

    public IReadOnlyDictionary<string, double> CounterTotals =>
        new ReadOnlyDictionary<string, double>(_counterTotals);

    public IReadOnlyDictionary<string, double> LatestGauges =>
        new ReadOnlyDictionary<string, double>(_latestGauges);

    public void Snapshot(double elapsedSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _listener.RecordObservableInstruments();

        var values = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var name in InstrumentNames)
            values[name] = GetValue(name);

        lock (_snapshotGate)
            _snapshots.Add(new MetricsSnapshot(elapsedSeconds, values));
    }

    public void WriteCsv(string path)
    {
        MetricsSnapshot[] snapshots;

        lock (_snapshotGate)
            snapshots = _snapshots.ToArray();

        using var writer = new StreamWriter(path);
        writer.Write("ElapsedSeconds");

        foreach (var name in InstrumentNames)
        {
            writer.Write(',');
            writer.Write(name);
        }

        writer.WriteLine();

        foreach (var snapshot in snapshots)
        {
            writer.Write(snapshot.ElapsedSeconds.ToString(CultureInfo.InvariantCulture));

            foreach (var name in InstrumentNames)
            {
                writer.Write(',');
                writer.Write(snapshot.Values[name].ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteLine();
        }
    }

    public long Total(string instrumentName)
    {
        ArgumentNullException.ThrowIfNull(instrumentName);

        return _counterTotals.TryGetValue(instrumentName, out var value)
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : 0L;
    }

    public double LatestGauge(string instrumentName)
    {
        ArgumentNullException.ThrowIfNull(instrumentName);

        return _latestGauges.TryGetValue(instrumentName, out var value)
            ? value
            : 0.0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _listener.Dispose();
        _disposed = true;
    }

    private static void OnInstrumentPublished(
        Instrument instrument,
        MeterListener listener)
    {
        if (instrument.Meter.Name == MeterName)
            listener.EnableMeasurementEvents(instrument);
    }

    private void OnMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state) => OnMeasurement(instrument, (double)measurement);

    private void OnMeasurement(
        Instrument instrument,
        int measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state) => OnMeasurement(instrument, (double)measurement);

    private void OnMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state) => OnMeasurement(instrument, measurement);

    private void OnMeasurement(Instrument instrument, double measurement)
    {
        var name = instrument.Name;

        if (CounterNameSet.Contains(name))
        {
            _counterTotals.AddOrUpdate(name, measurement, (_, current) => current + measurement);
            _latestValues[name] = _counterTotals[name];
            return;
        }

        _latestValues[name] = measurement;

        if (GaugeNameSet.Contains(name))
            _latestGauges[name] = measurement;
    }

    private double GetValue(string name)
    {
        if (CounterNameSet.Contains(name))
            return _counterTotals.TryGetValue(name, out var total) ? total : 0.0;

        return _latestValues.TryGetValue(name, out var value) ? value : 0.0;
    }

    private sealed record MetricsSnapshot(
        double ElapsedSeconds,
        IReadOnlyDictionary<string, double> Values);
}
