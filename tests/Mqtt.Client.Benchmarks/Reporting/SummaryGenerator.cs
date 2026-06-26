// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Globalization;
using System.IO;
using System.Text;

namespace Mqtt.Client.Benchmarks;

/// <summary>
/// Post-benchmark processor: walks BenchmarkDotNet.Artifacts/results and writes
/// docs/benchmarks.md with the full results.
/// </summary>
public static class SummaryGenerator
{
    public static void Generate()
    {
        var resultsDir = FindResultsDir();
        if (resultsDir is null)
        {
            Console.WriteLine(
                "[SummaryGenerator] No BenchmarkDotNet.Artifacts/results directory found.");
            return;
        }
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.WriteLine(
                "[SummaryGenerator] Couldn't locate repo root (no .git folder above cwd).");
            return;
        }
        var docsPath = Path.Combine(repoRoot, "docs", "benchmarks.md");

        // The cross-language throughput tables are owned by the --crosslang harness (a separate
        // process / CI job). Preserve its marked section verbatim so regenerating the
        // BenchmarkDotNet tables here never drops it, and surface it up front.
        var existingCrossLang = File.Exists(docsPath)
            ? CrossLangSection.Extract(File.ReadAllText(docsPath))
            : null;

        var docs = new StringBuilder();
        docs.AppendLine("# Mqtt.Client vs MQTTnet — benchmark results");
        docs.AppendLine();
        docs.AppendLine(
            CultureInfo.InvariantCulture,
            $"_Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC._");
        docs.AppendLine();
        docs.AppendLine(
            "[MQTTnet](https://github.com/dotnet/MQTTnet) is a mature, battle-tested .NET " +
            "MQTT library. These benchmarks are not a verdict on MQTTnet — they exist to " +
            "make tradeoffs visible for callers choosing between the two clients. See the " +
            "README's \"When to pick MQTTnet instead\" section for guidance.");
        docs.AppendLine();
        // Cross-implementation throughput leads, then the per-operation micro/end-to-end tables.
        docs.AppendLine(existingCrossLang ?? CrossLangPlaceholder());
        docs.AppendLine();
        docs.AppendLine(
            "The per-operation sections below each open with a one-line note on what that " +
            "benchmark measures. They fall into two groups: **codec micro-benchmarks** " +
            "(in-memory encode/decode — no broker, no network) and **end-to-end benchmarks** " +
            "(a real in-process MQTTnet broker over a TCP loopback, exercising the full client " +
            "stack per operation). In every table the MQTTnet row is the baseline (Ratio = " +
            "1.00), and `PayloadSize` is the MQTT payload length in bytes.");
        docs.AppendLine();
        docs.AppendLine(
            "Read each `Ratio` next to its `Error`/`StdDev` columns. The end-to-end benchmarks " +
            "carry real run-to-run variance (loopback scheduling, GC, and other processes on the " +
            "machine), so a single cell that is within ~10–15% of 1.00 — or that is not " +
            "corroborated by the neighbouring payload sizes and the other QoS levels — is noise, " +
            "not a regression. The codec micro-benchmarks and the allocation columns are the " +
            "more stable signal.");
        docs.AppendLine();
        docs.AppendLine("Run with:");
        docs.AppendLine("```");
        docs.AppendLine(
            "dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- --filter '*' --report");
        docs.AppendLine(
            "dotnet run -c Release --project tests/Mqtt.Client.Benchmarks -- " +
            "--filter '*' --full --report");
        docs.AppendLine("```");
        docs.AppendLine();

        foreach (var md in Directory.EnumerateFiles(resultsDir, "*-report-github.md")
            .OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(md);
            docs.AppendLine(CultureInfo.InvariantCulture, $"## {name}");
            docs.AppendLine();
            var description = DescribeBenchmark(name);
            if (description.Length > 0)
            {
                docs.AppendLine(description);
                docs.AppendLine();
            }
            docs.AppendLine(File.ReadAllText(md).Trim());
            docs.AppendLine();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(docsPath)!);
        File.WriteAllText(docsPath, docs.ToString());
        Console.WriteLine($"[SummaryGenerator] Wrote {docsPath}");
    }

    private static string CrossLangPlaceholder()
    {
        var nl = Environment.NewLine;
        return CrossLangSection.Begin + nl
            + "## Cross-implementation throughput" + nl + nl
            + "_Run the harness with `--crosslang` (needs Mosquitto + the Paho C library) to "
            + "populate this section._" + nl
            + CrossLangSection.End;
    }

    private static string DescribeBenchmark(string reportName)
    {
        if (reportName.Contains("EncodePublish", StringComparison.Ordinal))
        {
            return "**Codec micro-benchmark.** Serialises a single MQTT 5 PUBLISH packet " +
                "(QoS 0, topic `bench/encode`) to its wire bytes, once per `PayloadSize`. Both " +
                "clients reuse one array-backed buffer across iterations (no per-operation " +
                "`ArrayPool` churn) and write the fixed header separately from the payload " +
                "(payload bytes are never copied into the header buffer), so it isolates the " +
                "raw encode-logic cost on equal footing. No broker or socket is involved.";
        }
        if (reportName.Contains("DecodePublish", StringComparison.Ordinal))
        {
            return "**Codec micro-benchmark.** The inverse of the encode test: parses the " +
                "bytes of one pre-encoded MQTT 5 PUBLISH back into a packet object, once per " +
                "`PayloadSize`. The same wire bytes (produced once by our encoder — the " +
                "on-wire format is identical) feed both decoders, so it measures pure parse " +
                "cost: the var-int remaining-length, the topic, and the payload " +
                "slice/allocation. No broker or socket is involved.";
        }
        if (reportName.Contains("EncodeSubscribe", StringComparison.Ordinal))
        {
            return "**Codec micro-benchmark.** Serialises one MQTT 5 SUBSCRIBE packet " +
                "carrying two topic filters (`sensors/+/temp` at QoS 1 and `commands/#` at " +
                "QoS 0) to its wire bytes. There is no payload-size parameter — it is a small, " +
                "fixed packet that exercises topic-filter and per-filter QoS/options encoding " +
                "rather than bulk payload throughput.";
        }
        if (reportName.Contains("ConnectLatency", StringComparison.Ordinal))
        {
            return "**End-to-end.** Measures one full connect + disconnect cycle — TCP " +
                "handshake, CONNECT/CONNACK, then DISCONNECT — creating a brand-new client per " +
                "invocation so the complete handshake cost is captured. It runs with a small, " +
                "fixed invocation count so neither client exhausts the OS ephemeral-port range " +
                "(which would surface as `SocketException 10048`); the figure is handshake " +
                "latency, not throughput. There is no payload-size parameter.";
        }
        if (reportName.Contains("PublishQoS0", StringComparison.Ordinal))
        {
            return "**End-to-end.** Times a single `PublishAsync` at QoS 0 (at-most-once — " +
                "fire-and-forget, the broker sends no acknowledgement) per invocation, for each " +
                "`PayloadSize`. This is the leanest publish path: serialise the PUBLISH and " +
                "write it to the socket, with no return round-trip to await.";
        }
        if (reportName.Contains("PublishQoS1", StringComparison.Ordinal))
        {
            return "**End-to-end.** Times a single `PublishAsync` at QoS 1 (at-least-once) per " +
                "invocation, for each `PayloadSize`. The call completes only once the broker's " +
                "PUBACK arrives, so each measurement includes one network round-trip plus the " +
                "packet-id allocation and ack-correlation work.";
        }
        if (reportName.Contains("PublishQoS2", StringComparison.Ordinal))
        {
            return "**End-to-end.** Times a single `PublishAsync` at QoS 2 (exactly-once) per " +
                "invocation, for each `PayloadSize`. This drives the full four-packet handshake " +
                "— PUBLISH → PUBREC → PUBREL → PUBCOMP, i.e. two round-trips — making it the " +
                "most expensive delivery guarantee to benchmark.";
        }
        if (reportName.Contains("SubscribeReceive", StringComparison.Ordinal))
        {
            return "**End-to-end.** Measures receive throughput: a separate publisher " +
                "connection publishes one QoS 0 message and the subscribed client-under-test " +
                "waits to read it from its channel, for each `PayloadSize`. The application " +
                "messages are pre-built during setup, so only publish → broker fan-out → " +
                "client receive/dispatch is timed.";
        }
        return string.Empty;
    }

    private static string? FindResultsDir()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(Environment.CurrentDirectory, "BenchmarkDotNet.Artifacts", "results"),
            Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts", "results"),
        })
        {
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? FindRepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
                dir = dir.Parent;
            }
        }
        return null;
    }
}
