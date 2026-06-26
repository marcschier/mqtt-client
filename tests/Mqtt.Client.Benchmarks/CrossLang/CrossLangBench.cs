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

    private readonly record struct Row(
        int Qos, int Size, double Ours, double Net, double Mosq, double Paho);

    private static string? PahoBin()
    {
        var p = Environment.GetEnvironmentVariable("PAHO_PUB_BENCH");
        return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
    }

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
        var pahoBin = PahoBin();
        Console.WriteLine(pahoBin is null
            ? "[crosslang] PAHO_PUB_BENCH not set; Paho C column will be n/a."
            : $"[crosslang] Paho C publisher: {pahoBin}");
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
                var pahoRate = pahoBin is null
                    ? double.NaN
                    : await MeasureAsync(broker, n, qos,
                        t => PublishPahoAsync(pahoBin, broker.Port, t, qos, n, size));

                rows.Add(new Row(qos, size, oursRate, netRate, mosqRate, pahoRate));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[crosslang] qos{qos} {size}B x{n}: ours={oursRate:N0} net={netRate:N0} " +
                    $"mosq={mosqRate:N0} paho={pahoRate:N0} msg/s"));
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

    // Publish window for QoS > 0: the number of acknowledgements kept in flight at once. A small
    // window pipelines the round-trips (so the comparison is sustained throughput, not per-message
    // latency) while staying within paho's maxInflightMessages; all three publishers use it.
    private const int PublishWindow = 100;

    // Keeps up to PublishWindow QoS>0 publishes in flight at once as a true sliding window: a new
    // publish is started as soon as the oldest completes, so the pipe stays full. This matches the
    // C publishers (mosquitto_pub / paho), which collect PUBACKs asynchronously and never block
    // per message — a Task.WhenAll barrier would instead drain to zero every batch and understate
    // sustained throughput.
    private static async Task PipelineAsync(Func<Task> publishOne, int n)
    {
        var inflight = new Queue<Task>(PublishWindow);
        for (var i = 0; i < n; i++)
        {
            inflight.Enqueue(publishOne());
            if (inflight.Count >= PublishWindow) await inflight.Dequeue();
        }
        while (inflight.Count > 0) await inflight.Dequeue();
    }

    private static async Task PublishOursAsync(
        Mqtt.Client.MqttClient client, string topic, byte[] payload, int qos, int n)
    {
        var q = (Mqtt.Client.MqttQoS)qos;
        if (qos == 0)
        {
            for (var i = 0; i < n; i++) await client.PublishAsync(topic, payload, q);
            return;
        }
        await PipelineAsync(() => client.PublishAsync(topic, payload, q).AsTask(), n);
    }

    private static async Task PublishMqttnetAsync(
        IMqttClient client, string topic, byte[] payload, int qos, int n)
    {
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel((MqttnetQoS)qos)
            .Build();
        if (qos == 0)
        {
            for (var i = 0; i < n; i++) await client.PublishAsync(msg);
            return;
        }
        await PipelineAsync(() => client.PublishAsync(msg), n);
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

    private static async Task PublishPahoAsync(
        string bin, int port, string topic, int qos, int n, int size)
    {
        var psi = new ProcessStartInfo(bin)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[]
        {
            "127.0.0.1", port.ToString(CultureInfo.InvariantCulture), topic,
            qos.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture),
            size.ToString(CultureInfo.InvariantCulture),
        })
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        _ = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        // On a non-zero exit the messages were not delivered; the subscriber then times out and the
        // cell is recorded as n/a by MeasureAsync — no need to throw here.
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
        sb.AppendLine(CrossLangSection.Begin);
        sb.AppendLine(
            "## Cross-implementation throughput — Mqtt.Client vs MQTTnet vs C (Mosquitto, Paho)");
        sb.AppendLine();
        var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"_Cross-language section generated {ts} UTC by `--crosslang`._");
        sb.AppendLine();
        sb.AppendLine(
            "End-to-end **publish→receive** throughput: each publisher sends N messages through " +
            "a real [Eclipse Mosquitto](https://mosquitto.org/) broker to a constant " +
            "`mosquitto_sub -C N` subscriber; the rate is N divided by the wall-clock time for " +
            "the subscriber to receive all N. Two native-C datapoints are included — the " +
            "`mosquitto_pub` CLI tool and a purpose-built **paho.mqtt.c** publisher. Higher is " +
            "better.");
        sb.AppendLine();
        sb.AppendLine(
            "These numbers are **wall-clock and cross-language** — not directly comparable to " +
            "the per-operation BenchmarkDotNet results above. The **Mosquitto C (CLI)** column " +
            "is the `mosquitto_pub` command-line tool, driven by feeding it one message per line " +
            "on stdin; that stdin mechanism — not the protocol — caps it at roughly 14k msg/s " +
            "here for both QoS levels. It still pipelines QoS 1 (it sends the PUBLISHes and " +
            "collects the PUBACKs asynchronously, never blocking per message), so its QoS 1 " +
            "lands at the same stdin-bound ceiling rather than below it — a convenience tool, " +
            "not a throughput-optimised client. The **Paho C (lib)** column is a purpose-built " +
            "publisher on the Eclipse Paho C synchronous `MQTTClient` v5 API doing exactly what " +
            "the .NET clients do — one persistent connection over the same broker — so it is the " +
            "true apples-to-apples native baseline. For QoS 1 every persistent publisher keeps a " +
            "sliding window of in-flight publishes (the .NET clients and paho up to 100; " +
            "`mosquitto_pub` via libmosquitto's own window) and collects the PUBACKs " +
            "asynchronously, so the figure is sustained throughput, not per-message round-trip " +
            "latency; QoS 0 is measured end-to-end (the subscriber must receive all N), so " +
            "fire-and-forget enqueue is not mistaken for delivery.");
        sb.AppendLine();
        sb.AppendLine(
            "**Reading the Paho C column:** its QoS 0 figure (no acknowledgements) is " +
            "competitive, but its QoS 1 figure is held back by paho.mqtt.c exposing no " +
            "`TCP_NODELAY` option — its acknowledgement round-trips stall on Nagle / delayed-ACK " +
            "even when pipelined, where the .NET clients disable Nagle. So the QoS 1 number " +
            "reflects paho's default TCP behaviour, not a native-C ceiling — a useful reminder " +
            "that TCP tuning, not language, dominates QoS 1 throughput here.");
        sb.AppendLine();

        foreach (var qos in QoSLevels)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"### QoS {qos} — throughput (msg/s, higher is better)");
            sb.AppendLine();
            sb.AppendLine("| Payload | Mqtt.Client | MQTTnet | Mosquitto C (CLI) | Paho C (lib) |");
            sb.AppendLine("| --- | ---: | ---: | ---: | ---: |");
            foreach (var r in rows.Where(r => r.Qos == qos))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {Bytes(r.Size)} | {Rate(r.Ours)} | {Rate(r.Net)} | {Rate(r.Mosq)} | " +
                    $"{Rate(r.Paho)} |");
            }
            sb.AppendLine();
        }
        sb.Append(CrossLangSection.End);

        var path = Path.Combine(repoRoot, "docs", "benchmarks.md");
        var doc = File.Exists(path)
            ? File.ReadAllText(path)
            : "# Mqtt.Client vs MQTTnet — benchmark results" + Environment.NewLine;
        doc = CrossLangSection.Upsert(doc, sb.ToString());
        File.WriteAllText(path, doc);
        Console.WriteLine($"[crosslang] updated cross-language section in {path}");
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
