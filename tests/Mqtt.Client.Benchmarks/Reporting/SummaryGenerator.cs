// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Mqtt.Client.Benchmarks;

/// <summary>
/// Post-benchmark processor: walks BenchmarkDotNet.Artifacts/results, extracts a short summary,
/// writes docs/benchmarks.md (full results) and updates README.md between
/// <c>&lt;!-- benchmarks:start --&gt;</c> / <c>&lt;!-- benchmarks:end --&gt;</c> markers.
/// </summary>
public static class SummaryGenerator
{
    private const string ReadmeStart = "<!-- benchmarks:start -->";
    private const string ReadmeEnd = "<!-- benchmarks:end -->";

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
        var readmePath = Path.Combine(repoRoot, "README.md");

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
        docs.AppendLine(
            "Each section below opens with a one-line note on what that benchmark measures. " +
            "They fall into two groups: **codec micro-benchmarks** (in-memory encode/decode " +
            "— no broker, no network) and **end-to-end benchmarks** (a real in-process " +
            "MQTTnet broker over a TCP loopback, exercising the full client stack per " +
            "operation). In every table the MQTTnet row is the baseline (Ratio = 1.00), and " +
            "`PayloadSize` is the MQTT payload length in bytes.");
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

        if (!File.Exists(readmePath))
        {
            Console.WriteLine(
                $"[SummaryGenerator] README.md not found at {readmePath}; skipping summary block.");
            return;
        }
        var summary = BuildReadmeSummary(resultsDir);
        var readme = File.ReadAllText(readmePath);
        var start = readme.IndexOf(ReadmeStart, StringComparison.Ordinal);
        var end = readme.IndexOf(ReadmeEnd, StringComparison.Ordinal);
        var block = $"{ReadmeStart}\n{summary}\n{ReadmeEnd}";
        if (start >= 0 && end > start)
        {
            readme = string.Concat(
                readme.AsSpan(0, start),
                block,
                readme.AsSpan(end + ReadmeEnd.Length));
        }
        else
        {
            readme = readme.TrimEnd() + "\n\n## Benchmarks (latest run)\n\n" + block + "\n";
        }
        File.WriteAllText(readmePath, readme);
        Console.WriteLine($"[SummaryGenerator] Updated README summary at {readmePath}");
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

    private static string BuildReadmeSummary(string resultsDir)
    {
        // (Scenario, Client, Mean ns, Allocated bytes)
        var raw = new List<(string Scenario, string Client, double MeanNs, long Allocated)>();
        foreach (var json in Directory.EnumerateFiles(resultsDir, "*-report-full.json"))
        {
            using var fs = File.OpenRead(json);
            using var doc = JsonDocument.Parse(fs);
            if (!doc.RootElement.TryGetProperty("Benchmarks", out var benchmarks)) continue;
            foreach (var b in benchmarks.EnumerateArray())
            {
                var type = b.GetProperty("Type").GetString() ?? "";
                if (!type.Contains("PublishQoS", StringComparison.Ordinal) &&
                    !type.Contains("SubscribeReceive", StringComparison.Ordinal)) continue;
                if (b.TryGetProperty("Parameters", out var p) && p.GetString() is { } ps &&
                    ps.Contains("PayloadSize", StringComparison.Ordinal) &&
                    !ps.Contains("PayloadSize=256", StringComparison.Ordinal))
                {
                    continue;
                }
                var method = b.GetProperty("Method").GetString() ?? "";
                if (!b.TryGetProperty("Statistics", out var stats)
                    || stats.ValueKind != JsonValueKind.Object) continue;
                var mean = stats.GetProperty("Mean").GetDouble();
                var alloc = b.TryGetProperty("Memory", out var mem)
                    && mem.TryGetProperty("BytesAllocatedPerOperation", out var ba)
                    ? ba.GetInt64() : 0L;
                raw.Add((type, method, mean, alloc));
            }
        }

        // Pair MQTTnet baseline with Mqtt.Client contender per scenario.
        var sb = new StringBuilder();
        sb.AppendLine(
            "_Headline rows from the latest BenchmarkDotNet run (PayloadMemory = 256 B). See `docs/benchmarks.md` for the full matrix._");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Client | Mean | Allocated | Ratio vs MQTTnet |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: |");
        foreach (var grp in raw.GroupBy(r => r.Scenario)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var baseline = grp.FirstOrDefault(
                r => r.Client.Contains("Mqttnet", StringComparison.OrdinalIgnoreCase));
            foreach (var r in grp.OrderBy(r => r.Client, StringComparer.Ordinal))
            {
                var ratio = baseline.MeanNs > 0
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"{r.MeanNs / baseline.MeanNs:F2}")
                    : "—";
                var alloc = r.Allocated > 0
                    ? r.Allocated.ToString("N0", CultureInfo.InvariantCulture) + " B"
                    : "—";
                sb.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"| {Clean(r.Scenario)} | {r.Client} | {FormatTime(r.MeanNs)} | {alloc} | {ratio} |");
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string Clean(string typeName)
    {
        var i = typeName.LastIndexOf('.');
        return (i >= 0 ? typeName.Substring(i + 1) : typeName).Replace(
            "Benchmark",
            "",
            StringComparison.Ordinal);
    }

    private static string FormatTime(double ns)
    {
        if (ns < 1_000) return string.Create(CultureInfo.InvariantCulture, $"{ns:F1} ns");
        if (ns < 1_000_000) return string.Create(
            CultureInfo.InvariantCulture,
            $"{ns / 1_000:F2} \u00b5s");
        return string.Create(CultureInfo.InvariantCulture, $"{ns / 1_000_000:F2} ms");
    }
}
