// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Globalization;

namespace Mqtt.Client.ChaosTests;

/// <summary>
/// Parsed configuration for a chaos/soak run. All knobs have safe defaults; a nightly run overrides
/// only <c>--duration</c>. A failing run is reproduced by re-passing the logged <c>--seed</c>.
/// </summary>
public sealed class ChaosConfig
{
    public TimeSpan Duration { get; private set; } = TimeSpan.FromHours(1);
    public int Seed { get; private set; } = Environment.TickCount;
    public int Clients { get; private set; } = 4;
    public string Transport { get; private set; } = "tcp";
    public string Scenario { get; private set; } = "all";
    public string ReportDir { get; private set; } = ".";
    public bool FailFast { get; private set; }

    /// <summary>
    /// Keep-alive seconds used by the workload clients. Deliberately short so the black-hole fault
    /// exercises the keep-alive read-idle watchdog within the soak.
    /// </summary>
    public int KeepAliveSeconds { get; private set; } = 3;

    public static ChaosConfig Parse(string[] args)
    {
        var c = new ChaosConfig();
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (key)
            {
                case "--duration":
                    c.Duration = ParseDuration(Next());
                    break;
                case "--seed":
                    c.Seed = ParseInt(Next(), c.Seed);
                    break;
                case "--clients":
                    c.Clients = Math.Max(1, ParseInt(Next(), c.Clients));
                    break;
                case "--transport":
                    c.Transport = (Next() ?? c.Transport).ToLowerInvariant();
                    break;
                case "--scenario":
                    c.Scenario = (Next() ?? c.Scenario).ToLowerInvariant();
                    break;
                case "--report":
                    c.ReportDir = Next() ?? c.ReportDir;
                    break;
                case "--keepalive":
                    c.KeepAliveSeconds = Math.Max(1, ParseInt(Next(), c.KeepAliveSeconds));
                    break;
                case "--fail-fast":
                    c.FailFast = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {key}");
            }
        }
        return c;
    }

    public override string ToString()
        => $"duration={Duration} seed={Seed} clients={Clients} transport={Transport} "
            + $"scenario={Scenario} keepAlive={KeepAliveSeconds}s report={ReportDir} "
            + $"failFast={FailFast}";

    private static TimeSpan ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.FromHours(1);
        }
        // A bare integer means seconds (TimeSpan.Parse would read "90" as 90 days). Only fall back
        // to hh:mm:ss parsing when the value isn't a plain number.
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs))
        {
            return TimeSpan.FromSeconds(secs);
        }
        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var ts))
        {
            return ts;
        }
        throw new ArgumentException($"Invalid --duration: {value}");
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : fallback;
}
