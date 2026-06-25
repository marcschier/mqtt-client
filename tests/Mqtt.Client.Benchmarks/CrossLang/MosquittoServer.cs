// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Mqtt.Client.Benchmarks.CrossLang;

/// <summary>
/// Launches a real Eclipse Mosquitto broker (plain TCP, anonymous, ephemeral port) for the
/// cross-language throughput harness. <see cref="IsAvailable"/> is false when Mosquitto is not
/// installed, in which case the harness is skipped.
/// </summary>
internal sealed class MosquittoServer : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _dir;

    private MosquittoServer(Process process, string dir, int port)
    {
        _process = process;
        _dir = dir;
        Port = port;
    }

    public string Host => "127.0.0.1";
    public int Port { get; }

    public static bool IsAvailable => Which("mosquitto") is not null;
    public static bool ClientsAvailable => Which("mosquitto_pub") is not null;

    public static string? Which(string tool)
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

    public static async Task<MosquittoServer> StartAsync(CancellationToken ct = default)
    {
        var exe = Which("mosquitto")
            ?? throw new InvalidOperationException("mosquitto not on PATH.");
        var dir = Directory.CreateTempSubdirectory("mqtt-bench-").FullName;
        var port = GetEphemeralPort();
        var confPath = Path.Combine(dir, "mosquitto.conf");
        await File.WriteAllTextAsync(
            confPath,
            string.Join('\n',
                "allow_anonymous true",
                "max_queued_messages 0",      // unlimited: never drop while a fast publisher floods
                "max_inflight_messages 100",
                $"listener {port} 127.0.0.1",
                ""),
            ct);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        var psi = new ProcessStartInfo(exe, $"-c \"{confPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start mosquitto.");
        _ = process.StandardOutput.ReadToEndAsync(ct);
        _ = process.StandardError.ReadToEndAsync(ct);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port, ct);
                return new MosquittoServer(process, dir, port);
            }
            catch (SocketException)
            {
                await Task.Delay(100, ct);
            }
        }
        try { process.Kill(true); } catch { /* best effort */ }
        throw new TimeoutException("Mosquitto did not start in time.");
    }

    private static int GetEphemeralPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        try { _process.Kill(true); } catch { /* already gone */ }
        try { await _process.WaitForExitAsync(); } catch { /* best effort */ }
        _process.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
