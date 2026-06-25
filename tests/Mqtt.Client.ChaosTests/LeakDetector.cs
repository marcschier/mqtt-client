// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

namespace Mqtt.Client.ChaosTests;

public sealed record LeakSample(
    double ElapsedSeconds,
    long ManagedHeapBytes,
    long WorkingSetBytes,
    int HandleCount,
    int ThreadCount);

public sealed class LeakDetector
{
    private const long HeapAbsoluteSlackBytes = 32L * 1024L * 1024L;
    private const double HeapGrowthFactor = 2.0;
    private const double HandleFactor = 2.0;
    private const int HandleSlack = 200;
    private const double ThreadFactor = 2.0;
    private const int ThreadSlack = 32;
    private const double MaxHeapSlopeBytesPerSecond = 1024.0 * 1024.0 / 60.0;

    private readonly List<LeakSample> _samples = new();
    private readonly TimeSpan _warmup;

    public LeakDetector(TimeSpan warmup) => _warmup = warmup;

    public IReadOnlyList<LeakSample> Samples => new ReadOnlyCollection<LeakSample>(_samples);

    public LeakSample Sample(double elapsedSeconds)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var managedHeapBytes = GC.GetTotalMemory(forceFullCollection: true);
        using var process = Process.GetCurrentProcess();
        process.Refresh();

        var sample = new LeakSample(
            elapsedSeconds,
            managedHeapBytes,
            process.WorkingSet64,
            process.HandleCount,
            process.Threads.Count);
        _samples.Add(sample);

        return sample;
    }

    public void WriteCsv(string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(
            "ElapsedSeconds,ManagedHeapBytes,WorkingSetBytes,HandleCount,ThreadCount");

        foreach (var sample in _samples)
        {
            writer.Write(sample.ElapsedSeconds.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.ManagedHeapBytes.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.WorkingSetBytes.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.HandleCount.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.WriteLine(sample.ThreadCount.ToString(CultureInfo.InvariantCulture));
        }
    }

    public bool TryDetectLeak(out string reason)
    {
        var postWarmup = _samples
            .Where(sample => sample.ElapsedSeconds >= _warmup.TotalSeconds)
            .ToArray();

        if (postWarmup.Length < 3)
        {
            reason = "Not enough post-warmup samples to detect a leak.";
            return false;
        }

        var baseline = postWarmup[0];
        var maxHeapBytes = postWarmup.Max(sample => sample.ManagedHeapBytes);
        var maxHandleCount = postWarmup.Max(sample => sample.HandleCount);
        var maxThreadCount = postWarmup.Max(sample => sample.ThreadCount);
        var heapLimit = baseline.ManagedHeapBytes * HeapGrowthFactor
            + HeapAbsoluteSlackBytes;
        var handleLimit = baseline.HandleCount * HandleFactor + HandleSlack;
        var threadLimit = baseline.ThreadCount * ThreadFactor + ThreadSlack;

        if (maxHeapBytes > heapLimit)
        {
            reason = $"Managed heap grew from {baseline.ManagedHeapBytes} to "
                + $"{maxHeapBytes} bytes, above limit {heapLimit:F0}.";
            return true;
        }

        if (maxHandleCount > handleLimit)
        {
            reason = $"Handle count grew from {baseline.HandleCount} to "
                + $"{maxHandleCount}, above limit {handleLimit:F0}.";
            return true;
        }

        if (maxThreadCount > threadLimit)
        {
            reason = $"Thread count grew from {baseline.ThreadCount} to "
                + $"{maxThreadCount}, above limit {threadLimit:F0}.";
            return true;
        }

        var heapSlope = CalculateHeapSlope(postWarmup);

        if (heapSlope > MaxHeapSlopeBytesPerSecond)
        {
            reason = $"Managed heap slope was {heapSlope:F2} bytes/sec, above "
                + $"limit {MaxHeapSlopeBytesPerSecond:F2}.";
            return true;
        }

        reason = "No leak detected.";
        return false;
    }

    private static double CalculateHeapSlope(IReadOnlyList<LeakSample> samples)
    {
        var count = samples.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumXX = 0.0;

        foreach (var sample in samples)
        {
            var x = sample.ElapsedSeconds;
            var y = sample.ManagedHeapBytes;
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumXX += x * x;
        }

        var denominator = count * sumXX - sumX * sumX;

        if (Math.Abs(denominator) < double.Epsilon)
            return 0.0;

        return (count * sumXY - sumX * sumY) / denominator;
    }
}
