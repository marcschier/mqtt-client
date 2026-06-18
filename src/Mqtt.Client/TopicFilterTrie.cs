// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Mqtt.Client.Subscriptions;

/// <summary>
/// Topic-filter trie supporting MQTT wildcards <c>+</c> (single level) and <c>#</c> (multi-level).
/// All matching is allocation-free (uses spans, no string splits at match time).
/// </summary>
internal sealed class TopicFilterTrie<T> where T : class
{
    private readonly Node _root = new();

    /// <summary>Adds <paramref name="value"/> under <paramref name="topicFilter"/>.</summary>
    public void Add(string topicFilter, T value)
    {
        if (string.IsNullOrEmpty(topicFilter))
        {
            throw new ArgumentException("Topic filter cannot be empty.", nameof(topicFilter));
        }
        var node = _root;
        var span = topicFilter.AsSpan();
        while (!span.IsEmpty)
        {
            var slash = span.IndexOf('/');
            var part = slash < 0 ? span : span.Slice(0, slash);
            var key = part.ToString();
            node.Children ??= new Dictionary<string, Node>(StringComparer.Ordinal);
            if (!node.Children.TryGetValue(key, out var child))
            {
                child = new Node();
                node.Children[key] = child;
            }
            node = child;
            span = slash < 0 ? default : span.Slice(slash + 1);
        }
        (node.Values ??= new List<T>()).Add(value);
    }

    /// <summary>
    /// Removes a value from <paramref name="topicFilter"/>. Returns true if removed.
    /// </summary>
    public bool Remove(string topicFilter, T value)
    {
        var node = _root;
        var span = topicFilter.AsSpan();
        while (!span.IsEmpty)
        {
            var slash = span.IndexOf('/');
            var part = slash < 0 ? span : span.Slice(0, slash);
            if (node.Children is null || !node.Children.TryGetValue(part.ToString(), out var child))
            {
                return false;
            }
            node = child;
            span = slash < 0 ? default : span.Slice(slash + 1);
        }
        return node.Values?.Remove(value) == true;
    }

    /// <summary>
    /// Invokes <paramref name="action"/> with every matching value for <paramref name="topic"/>.
    /// </summary>
    public void Match(string topic, Action<T> action)
    {
        MatchRecursive(_root, topic.AsSpan(), isFirstSegment: true, action);
    }

    private static void MatchRecursive(
        Node node,
        ReadOnlySpan<char> remaining,
        bool isFirstSegment,
        Action<T> action)
    {
        if (remaining.IsEmpty)
        {
            // Topic exhausted: emit values at this node.
            if (node.Values is not null)
            {
                foreach (var v in node.Values) action(v);
            }
            // And any child '#' that absorbs empty.
            if (node.Children is not null && node.Children.TryGetValue("#", out var hash)
                && hash.Values is not null)
            {
                foreach (var v in hash.Values) action(v);
            }
            return;
        }

        var slash = remaining.IndexOf('/');
        var segment = slash < 0 ? remaining : remaining.Slice(0, slash);
        var rest = slash < 0 ? default : remaining.Slice(slash + 1);

        // '$' topics are not matched by '+' or '#' (MQTT 4.7.2).
        var startsWithDollar = isFirstSegment && segment.Length > 0 && segment[0] == '$';

        if (node.Children is null) return;

        if (node.Children.TryGetValue(segment.ToString(), out var literal))
        {
            MatchRecursive(literal, rest, isFirstSegment: false, action);
        }
        if (!startsWithDollar)
        {
            if (node.Children.TryGetValue("+", out var plus))
            {
                MatchRecursive(plus, rest, isFirstSegment: false, action);
            }
            if (node.Children.TryGetValue("#", out var hash) && hash.Values is not null)
            {
                foreach (var v in hash.Values) action(v);
            }
        }
    }

    private sealed class Node
    {
        public Dictionary<string, Node>? Children;
        public List<T>? Values;
    }
}
