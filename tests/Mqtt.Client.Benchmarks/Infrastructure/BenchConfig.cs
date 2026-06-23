// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using Perfolizer.Mathematics.OutlierDetection;

namespace Mqtt.Client.Benchmarks;

public sealed class BenchConfig : ManualConfig
{
    public BenchConfig() : this(full: false) { }

    public BenchConfig(bool full)
    {
        // The full job is tuned for a noisy, occasionally-contended host: a short warmup plus a
        // bounded iteration band lets BDN's adaptive engine settle without unbounded runtime, and
        // RemoveUpper drops the slow spikes caused by other processes stealing the core. Keeping
        // LaunchCount at the default (1) keeps a full idle-window run tractable. ShortRun stays
        // minimal for quick local checks.
        AddJob(full
            ? Job.Default
                .WithWarmupCount(5)
                .WithMinIterationCount(15)
                .WithMaxIterationCount(30)
                .WithOutlierMode(OutlierMode.RemoveUpper)
            : Job.ShortRun);
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(CsvExporter.Default);
        AddExporter(JsonExporter.Full);
        AddLogger(ConsoleLogger.Default);
        AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance);
    }
}
