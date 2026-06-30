// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// Unit coverage for the test broker's topic-filter engine. The end-to-end round-trip tests only
// drive valid filters and concrete matches; these exercise the wildcard rules and the filter
// validator directly, including the edge cases ($-prefix exclusion, trailing '#' parent match, and
// malformed wildcard placement) that a real client never sends.

namespace Mqtt.Client.Testing.Tests;

public class TopicFilterTests
{
    [Test]
    [Arguments("a/b/c", "a/b/c", true)]      // exact match
    [Arguments("a/b/c", "a/b/d", false)]     // last level differs
    [Arguments("a/b", "a/b/c", false)]       // filter shorter than topic
    [Arguments("a/b/c", "a/b", false)]       // filter longer than topic
    [Arguments("a/+/c", "a/x/c", true)]      // single-level wildcard
    [Arguments("a/+/c", "a/c", false)]       // '+' must consume exactly one level
    [Arguments("a/+", "a/b/c", false)]       // '+' does not span multiple levels
    [Arguments("a/#", "a/b/c", true)]        // multi-level wildcard
    [Arguments("a/#", "a", true)]            // trailing '#' also matches the parent level
    [Arguments("a/#", "a/b", true)]
    [Arguments("#", "a/b/c", true)]          // lone '#' matches any non-$ topic
    [Arguments("#", "$SYS/x", false)]        // leading '#' must not match a '$' topic
    [Arguments("+/x", "$SYS/x", false)]      // leading '+' must not match a '$' topic
    [Arguments("$SYS/#", "$SYS/broker", true)]   // explicit '$' prefix is allowed
    [Arguments("$SYS/+", "$SYS/broker", true)]
    public async Task Matches_follows_mqtt_wildcard_rules(
        string filter, string topic, bool expected)
    {
        var actual = TopicFilter.Matches(filter, topic);
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    [Arguments("a/b/c", true)]
    [Arguments("a/+/c", true)]
    [Arguments("a/#", true)]
    [Arguments("#", true)]
    [Arguments("+", true)]
    [Arguments("+/+/+", true)]
    [Arguments("sport/+/player1", true)]
    [Arguments("", false)]                   // empty filter is invalid
    [Arguments("a/#/b", false)]              // '#' must be the last level
    [Arguments("a/b#", false)]               // '#' must occupy a whole level
    [Arguments("a/+b", false)]               // '+' must occupy a whole level
    [Arguments("sport#", false)]
    [Arguments("a/b+", false)]
    public async Task IsValidFilter_enforces_wildcard_placement(string filter, bool expected)
    {
        var actual = TopicFilter.IsValidFilter(filter);
        await Assert.That(actual).IsEqualTo(expected);
    }
}
