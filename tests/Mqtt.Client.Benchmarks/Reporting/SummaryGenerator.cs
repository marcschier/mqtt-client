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
            "[MQTTnet](https://github.com/dotnet/MQTTnet) is a mature, battle-tested " +
            ".NET MQTT library.");
        docs.AppendLine(
            "These benchmarks are not a verdict on MQTTnet — they exist to make tradeoffs visible");
        docs.AppendLine("for callers choosing between the two clients. See the README's");
        docs.AppendLine("\"When to pick MQTTnet instead\" section for guidance.");
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
            docs.AppendLine(
                CultureInfo.InvariantCulture,
                $"## {Path.GetFileNameWithoutExtension(md)}");
            docs.AppendLine();
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
