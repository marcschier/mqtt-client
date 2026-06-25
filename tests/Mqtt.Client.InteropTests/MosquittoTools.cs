// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;

namespace Mqtt.Client.InteropTests;

/// <summary>
/// Thin wrapper over the Mosquitto C client tools (<c>mosquitto_pub</c> / <c>mosquitto_sub</c>),
/// used as the native-C MQTT client in interop tests.
/// </summary>
internal static class MosquittoTools
{
    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Locates a tool on PATH (adding the platform executable suffix), or null.</summary>
    public static string? Find(string tool)
    {
        var exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null) return null;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            string full;
            try { full = Path.Combine(dir, exe); }
            catch (ArgumentException) { continue; }
            if (File.Exists(full)) return full;
        }
        return null;
    }

    /// <summary>Publishes a single message with mosquitto_pub and returns when it exits.</summary>
    public static Task PublishAsync(
        int port,
        string topic,
        string payload,
        int qos = 0,
        bool retain = false,
        IEnumerable<string>? extraArgs = null,
        CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "-h", "127.0.0.1", "-p", I(port), "-V", "mqttv5",
            "-q", I(qos), "-t", topic, "-m", payload,
        };
        if (retain) args.Add("-r");
        if (extraArgs is not null) args.AddRange(extraArgs);
        return RunToExitAsync("mosquitto_pub", args, TimeSpan.FromSeconds(15), ct);
    }

    /// <summary>
    /// Subscribes with mosquitto_sub, captures up to <paramref name="count"/> message payloads
    /// (mosquitto_sub exits after that via <c>-C</c>), and returns them as trimmed lines.
    /// </summary>
    public static async Task<IReadOnlyList<string>> SubscribeCaptureAsync(
        int port,
        string topic,
        int count,
        int qos = 0,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "-h", "127.0.0.1", "-p", I(port), "-V", "mqttv5",
            "-q", I(qos), "-t", topic, "-C", I(count),
        };
        var (_, stdout, _) = await RunAsync(
            "mosquitto_sub", args, timeout ?? TimeSpan.FromSeconds(20), ct);
        return stdout.Split(
            '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Starts a long-lived mosquitto_sub registered with a Last Will. The caller kills the returned
    /// process ungracefully (no DISCONNECT) to make the broker publish the will.
    /// </summary>
    public static Process StartWilledClient(
        int port, string idleTopic, string willTopic, string willPayload, int willQos = 1)
    {
        var args = new List<string>
        {
            "-h", "127.0.0.1", "-p", I(port), "-V", "mqttv5", "-t", idleTopic,
            "--will-topic", willTopic, "--will-payload", willPayload, "--will-qos", I(willQos),
        };
        return Start("mosquitto_sub", args);
    }

    private static Process Start(string tool, IEnumerable<string> args)
    {
        var exe = Find(tool)
            ?? throw new InvalidOperationException($"{tool} is not on PATH.");
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {tool}.");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string tool, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        using var process = Start(tool, args);
        var outTask = process.StandardOutput.ReadToEndAsync(ct);
        var errTask = process.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }
        var stdout = await outTask.ConfigureAwait(false);
        var stderr = await errTask.ConfigureAwait(false);
        return (process.HasExited ? process.ExitCode : -1, stdout, stderr);
    }

    private static async Task RunToExitAsync(
        string tool, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var (exit, _, stderr) = await RunAsync(tool, args, timeout, ct);
        if (exit != 0)
        {
            throw new InvalidOperationException($"{tool} exited with {exit}: {stderr}");
        }
    }
}
