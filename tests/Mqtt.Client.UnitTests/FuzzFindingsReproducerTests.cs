// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Text;
using Mqtt.Client.Protocol;
using Mqtt.Client.Subscriptions;

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Walks tests/Mqtt.Client.FuzzTests/findings/&lt;harness&gt; and re-runs each crash input
/// against the corresponding harness. Once libFuzzer discovers a crash and we add the input
/// to findings/, this test pins the regression forever — even outside a fuzz run.
/// </summary>
public class FuzzFindingsReproducerTests
{
    private static string FindingsRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                dir = dir.Parent;
            }
            return dir is null
                ? string.Empty
                : Path.Combine(dir.FullName, "tests", "Mqtt.Client.FuzzTests", "findings");
        }
    }

    [Test]
    public async Task Decoder_findings_do_not_crash()
    {
        await ReplayAsync("decoder", bytes =>
        {
            try
            {
                MqttPacketDecoder.TryDecode(new ReadOnlySequence<byte>(bytes), MqttProtocolVersion.V500, out _, out _, out _);
            }
            catch (MqttProtocolException) { }
        });
    }

    [Test]
    public async Task TopicTrie_findings_do_not_crash()
    {
        await ReplayAsync("topic-trie", bytes =>
        {
            if (bytes.Length < 4) return;
            var split = Math.Max(1, bytes[0] % Math.Max(1, bytes.Length - 1));
            var filterBytes = bytes.AsSpan(1, Math.Min(split, bytes.Length - 1));
            var topicBytes = bytes.AsSpan(1 + filterBytes.Length);
            if (filterBytes.IsEmpty || topicBytes.IsEmpty) return;
            var filter = Sanitize(filterBytes);
            var topic = Sanitize(topicBytes);
            if (string.IsNullOrEmpty(filter) || string.IsNullOrEmpty(topic)) return;
            var trie = new TopicFilterTrie<object>();
            try { trie.Add(filter, new object()); } catch (ArgumentException) { return; }
            trie.Match(topic, _ => { });
        });
    }

    private static string Sanitize(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            if ((b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9') ||
                b == '/' || b == '+' || b == '#' || b == '$' || b == '-' || b == '_')
            {
                sb.Append((char)b);
            }
        }
        return sb.ToString();
    }

    private static async Task ReplayAsync(string harness, Action<byte[]> action)
    {
        var dir = Path.Combine(FindingsRoot, harness);
        if (string.IsNullOrEmpty(FindingsRoot) || !Directory.Exists(dir))
        {
            return;
        }
        var failures = new List<string>();
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            if (Path.GetFileName(f).StartsWith('.')) continue;
            try
            {
                action(File.ReadAllBytes(f));
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(f)}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        await Assert.That(failures.Count).IsEqualTo(0);
    }
}
