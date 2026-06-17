// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Text;
using Mqtt.Client.Subscriptions;

namespace Mqtt.Client.FuzzTests.Harnesses;

/// <summary>
/// libFuzzer harness for <see cref="TopicFilterTrie{T}"/>. Splits fuzz input into a topic filter
/// and a topic name, registers the filter, and asserts the trie either matches or doesn't —
/// without throwing.
/// </summary>
internal static class TopicTrieHarness
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) return;

        var split = Math.Max(1, data[0] % Math.Max(1, data.Length - 1));
        var filterBytes = data.Slice(1, Math.Min(split, data.Length - 1));
        var topicBytes = data.Slice(1 + filterBytes.Length);
        if (filterBytes.IsEmpty || topicBytes.IsEmpty) return;

        var filter = SanitizeTopic(filterBytes);
        var topic = SanitizeTopic(topicBytes);
        if (string.IsNullOrEmpty(filter) || string.IsNullOrEmpty(topic)) return;

        var trie = new TopicFilterTrie<object>();
        try
        {
            trie.Add(filter, new object());
        }
        catch (ArgumentException)
        {
            return; // invalid filter — accepted rejection
        }
        trie.Match(topic, _ => { });
    }

    private static string SanitizeTopic(ReadOnlySpan<byte> bytes)
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
}
