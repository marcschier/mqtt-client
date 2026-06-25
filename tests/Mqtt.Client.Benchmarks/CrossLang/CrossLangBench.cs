// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using MQTTnet;
using MqttnetProtocolVersion = MQTTnet.Formatter.MqttProtocolVersion;
using MqttnetQoS = MQTTnet.Protocol.MqttQualityOfServiceLevel;

namespace Mqtt.Client.Benchmarks.CrossLang;

/// <summary>
/// Cross-language publish→receive throughput harness. Each publisher (Mqtt.Client, MQTTnet, and the
/// native-C <c>mosquitto_pub</c>) sends N messages through the same real Mosquitto broker to a
/// constant <c>mosquitto_sub -C N</c> subscriber; throughput is N / wall-clock to receive all N.
/// Run via <c>dotnet run -- --crosslang</c>. Skips when Mosquitto is not installed.
/// </summary>
internal static class CrossLangBench
{
    private static readonly int[] Sizes = { 64, 256, 1024, 16384, 65536 };
    private static readonly int[] QoSLevels = { 0, 1 };

    private static int MessagesFor(int size) => size switch
    {
        <= 256 => 3000,
        <= 1024 => 2000,
        <= 16384 => 800,
        _ => 200,
    };

    private readonly record struct Row(int Qos, int Size, double Ours, double Net, double Mosq);

    public static async Task<int> RunAsync()
    {
        if (!MosquittoServer.IsAvailable || !MosquittoServer.ClientsAvailable)
        {
            Console.WriteLine("[crosslang] mosquitto / mosquitto_pub not on PATH; skipping.");
            return 0;
        }

        await using var broker = await MosquittoServer.StartAsync();
        const int maxPacket = 2 * 1024 * 1024;

        var mqttnet = new MQTTnet.MqttClientFactory().CreateMqttClient();
        await mqttnet.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(broker.Host, broker.Port)
            .WithProtocolVersion(MqttnetProtocolVersion.V500)
            .WithClientId($"xl-net-{Guid.NewGuid():N}")
            .WithMaximumPacketSize(maxPacket)
            .Build());

        await using var ours = Mqtt.Client.MqttClient.CreateBuilder()
            .ConnectTo($"mqtt://{broker.Host}:{broker.Port}")
            .WithClientId($"xl-ours-{Guid.NewGuid():N}")
            .WithProtocol(Mqtt.Client.MqttProtocolVersion.V500)
            .WithReconnect(null)
            .Configure(o => o.MaxIncomingPacketSize = maxPacket)
            .Build();
        await ours.ConnectAsync();

        var rows = new List<Row>();
        foreach (var qos in QoSLevels)
        {
            foreach (var size in Sizes)
            {
                var n = MessagesFor(size);
                var payload = new byte[size];
                Array.Fill(payload, (byte)'x');
                var line = new string('x', size);

                // Warmup each .NET client once (untimed).
                await PublishOursAsync(ours, $"xl/warm/{Guid.NewGuid():N}", payload, qos, 50);

                var oursRate = await MeasureAsync(broker, n, qos,
                    t => PublishOursAsync(ours, t, payload, qos, n));
                var netRate = await MeasureAsync(broker, n, qos,
                    t => PublishMqttnetAsync(mqttnet, t, payload, qos, n));
                var mosqRate = await MeasureAsync(broker, n, qos,
                    t => PublishMosquittoAsync(broker.Port, t, line, qos, n));

                rows.Add(new Row(qos, size, oursRate, netRate, mosqRate));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[crosslang] qos{qos} {size}B x{n}: ours={oursRate:N0} net={netRate:N0} " +
                    $"mosq={mosqRate:N0} msg/s"));
            }
        }

        try { await mqttnet.DisconnectAsync(); } catch { /* best effort */ }
        mqttnet.Dispose();

        WriteDoc(rows);
        return 0;
    }

    /// <summary>
    /// Times a publisher delivering N messages to a fresh <c>mosquitto_sub -C N</c> subscriber on a
    /// unique topic; returns messages/second end-to-end.
    /// </summary>
    private static async Task<double> MeasureAsync(
        MosquittoServer broker, int n, int qos, Func<string, Task> publishAll)
    {
        var topic = $"xl/{qos}/{Guid.NewGuid():N}";
        using var sub = StartSub(broker.Port, topic, n, qos);
        await Task.Delay(500);   // ensure the subscriber is registered before publishing

        var sw = Stopwatch.StartNew();
        await publishAll(topic);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await sub.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { sub.Kill(true); } catch { /* gone */ } }
        sw.Stop();

        return sub.HasExited && sub.ExitCode == 0
            ? n / sw.Elapsed.TotalSeconds
            : double.NaN;
    }

    private static async Task PublishOursAsync(
        Mqtt.Client.MqttClient client, string topic, byte[] payload, int qos, int n)
    {
        var q = (Mqtt.Client.MqttQoS)qos;
        for (var i = 0; i < n; i++)
        {
            await client.PublishAsync(topic, payload, q);
        }
    }

    private static async Task PublishMqttnetAsync(
        IMqttClient client, string topic, byte[] payload, int qos, int n)
    {
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel((MqttnetQoS)qos)
            .Build();
        for (var i = 0; i < n; i++)
        {
            await client.PublishAsync(msg);
        }
    }

    private static async Task PublishMosquittoAsync(
        int port, string topic, string line, int qos, int n)
    {
        var exe = MosquittoServer.Which("mosquitto_pub")!;
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[]
        {
            "-h", "127.0.0.1", "-p", port.ToString(CultureInfo.InvariantCulture),
            "-V", "mqttv5", "-q", qos.ToString(CultureInfo.InvariantCulture), "-t", topic, "-l",
        })
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        _ = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        var sb = new StringBuilder(line.Length + 1);
        for (var i = 0; i < n; i++)
        {
            sb.Clear();
            sb.Append(line).Append('\n');
            await p.StandardInput.WriteAsync(sb.ToString());
        }
        p.StandardInput.Close();
        await p.WaitForExitAsync();
    }

    private static Process StartSub(int port, string topic, int count, int qos)
    {
        var exe = MosquittoServer.Which("mosquitto_sub")!;
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[]
        {
            "-h", "127.0.0.1", "-p", port.ToString(CultureInfo.InvariantCulture),
            "-V", "mqttv5", "-q", qos.ToString(CultureInfo.InvariantCulture),
            "-t", topic, "-C", count.ToString(CultureInfo.InvariantCulture),
        })
        {
            psi.ArgumentList.Add(a);
        }
        var p = Process.Start(psi)!;
        _ = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        return p;
    }

    private static void WriteDoc(List<Row> rows)
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.WriteLine("[crosslang] repo root not found; not writing doc.");
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine("# Cross-implementation throughput — Mqtt.Client vs MQTTnet vs Mosquitto C");
        sb.AppendLine();
        sb.AppendLine(
            CultureInfo.InvariantCulture, $"_Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC._");
        sb.AppendLine();
        sb.AppendLine(
            "End-to-end **publish→receive** throughput: each publisher sends N messages through " +
            "a real [Eclipse Mosquitto](https://mosquitto.org/) broker to a constant " +
            "`mosquitto_sub -C N` subscriber; the rate is N divided by the wall-clock time for " +
            "the subscriber to receive all N. The native-C datapoint is Mosquitto's own " +
            "`mosquitto_pub` (libmosquitto). Higher is better.");
        sb.AppendLine();
        sb.AppendLine(
            "These numbers are **wall-clock and cross-language** — not directly comparable to " +
            "the per-operation [BenchmarkDotNet results](benchmarks.md). The native-C datapoint " +
            "is the `mosquitto_pub` **command-line tool** (libmosquitto): a convenience CLI that " +
            "reads messages line-by-line from stdin and does not pipeline QoS 1, so it trails " +
            "the persistent, tight-loop .NET publishers here. This measures that tool, **not** " +
            "the ceiling of a C library driven directly (e.g. paho.mqtt.c with batching). Both " +
            ".NET clients await acknowledgements for QoS 1; QoS 0 is measured end-to-end (the " +
            "subscriber must receive all N), so fire-and-forget enqueue is not mistaken for " +
            "delivery.");
        sb.AppendLine();

        foreach (var qos in QoSLevels)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"## QoS {qos} — throughput (msg/s, higher is better)");
            sb.AppendLine();
            sb.AppendLine("| Payload | Mqtt.Client | MQTTnet | Mosquitto C |");
            sb.AppendLine("| --- | ---: | ---: | ---: |");
            foreach (var r in rows.Where(r => r.Qos == qos))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {Bytes(r.Size)} | {Rate(r.Ours)} | {Rate(r.Net)} | {Rate(r.Mosq)} |");
            }
            sb.AppendLine();
        }

        var path = Path.Combine(repoRoot, "docs", "interop-benchmarks.md");
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"[crosslang] wrote {path}");
    }

    private static string Rate(double msgPerSec)
        => double.IsNaN(msgPerSec)
            ? "n/a"
            : msgPerSec.ToString("N0", CultureInfo.InvariantCulture);

    private static string Bytes(int size)
        => size >= 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{size / 1024} KiB")
            : string.Create(CultureInfo.InvariantCulture, $"{size} B");

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
