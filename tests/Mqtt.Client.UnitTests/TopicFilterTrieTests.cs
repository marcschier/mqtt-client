// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Collections.Generic;
using Mqtt.Client.Subscriptions;

namespace Mqtt.Client.UnitTests;

public class TopicFilterTrieTests
{
    private static List<string> Match(TopicFilterTrie<string> trie, string topic)
    {
        var hits = new List<string>();
        trie.Match(topic, hits.Add);
        return hits;
    }

    [Test]
    public async Task ExactMatch_Single()
    {
        var t = new TopicFilterTrie<string>();
        t.Add("a/b/c", "x");
        var hits = Match(t, "a/b/c");
        await Assert.That(hits).Contains("x");
        await Assert.That(hits.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PlusWildcard_MatchesSingleLevel()
    {
        var t = new TopicFilterTrie<string>();
        t.Add("a/+/c", "x");
        await Assert.That(Match(t, "a/b/c")).Contains("x");
        await Assert.That(Match(t, "a/x/c")).Contains("x");
        await Assert.That(Match(t, "a/b/d").Count).IsEqualTo(0);
        await Assert.That(Match(t, "a/b/c/d").Count).IsEqualTo(0);
    }

    [Test]
    public async Task HashWildcard_MatchesMultiLevel_AndEmpty()
    {
        var t = new TopicFilterTrie<string>();
        t.Add("a/#", "x");
        await Assert.That(Match(t, "a")).Contains("x");
        await Assert.That(Match(t, "a/b")).Contains("x");
        await Assert.That(Match(t, "a/b/c/d")).Contains("x");
        await Assert.That(Match(t, "b/c").Count).IsEqualTo(0);
    }

    [Test]
    public async Task DollarTopic_NotMatchedByWildcards()
    {
        var t = new TopicFilterTrie<string>();
        t.Add("#", "all");
        t.Add("+/foo", "plus");
        await Assert.That(Match(t, "$SYS/abc").Count).IsEqualTo(0);
        await Assert.That(Match(t, "$SYS/foo").Count).IsEqualTo(0);
    }

    [Test]
    public async Task Remove_RemovesValue()
    {
        var t = new TopicFilterTrie<string>();
        t.Add("a/b", "x");
        await Assert.That(t.Remove("a/b", "x")).IsTrue();
        await Assert.That(Match(t, "a/b").Count).IsEqualTo(0);
    }

    [Test]
    public async Task MultipleSubscribers_AllReceive()
    {
        var t = new TopicFilterTrie<string>();
        t.Add("a/+", "x");
        t.Add("a/b", "y");
        t.Add("#", "z");
        var hits = Match(t, "a/b");
        await Assert.That(hits.Count).IsEqualTo(3);
    }
}
