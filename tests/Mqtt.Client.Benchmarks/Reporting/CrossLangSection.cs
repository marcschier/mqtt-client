// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;

namespace Mqtt.Client.Benchmarks;

/// <summary>
/// The cross-language throughput results live as a marked section inside docs/benchmarks.md, so the
/// per-operation BenchmarkDotNet tables and the cross-language tables share one document. The two
/// generators run in separate processes / CI jobs, so each only rewrites its own part:
/// <see cref="SummaryGenerator"/> preserves this section, while the --crosslang harness replaces it.
/// </summary>
internal static class CrossLangSection
{
    public const string Begin = "<!-- BEGIN: cross-language throughput (--crosslang) -->";
    public const string End = "<!-- END: cross-language throughput -->";

    /// <summary>Returns the marked section (markers included), or null when it is absent.</summary>
    public static string? Extract(string doc)
    {
        var start = doc.IndexOf(Begin, StringComparison.Ordinal);
        if (start < 0) return null;
        var end = doc.IndexOf(End, start, StringComparison.Ordinal);
        if (end < 0) return null;
        return doc.Substring(start, end - start + End.Length);
    }

    /// <summary>
    /// Replaces the marked section with <paramref name="section"/> (which must include the markers).
    /// When the document has no markers yet, the section is appended after a blank line.
    /// </summary>
    public static string Upsert(string doc, string section)
    {
        var start = doc.IndexOf(Begin, StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = doc.IndexOf(End, start, StringComparison.Ordinal);
            if (end >= 0)
            {
                return doc[..start] + section + doc[(end + End.Length)..];
            }
        }
        var nl = Environment.NewLine;
        return doc.TrimEnd('\r', '\n') + nl + nl + section + nl;
    }
}
