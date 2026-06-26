// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;

namespace Mqtt.Client.Testing;

internal static class TopicFilter
{
    /// <summary>
    /// Matches a concrete topic against an MQTT subscription filter supporting the <c>+</c>
    /// (single level) and <c>#</c> (multi level) wildcards, per [MQTT-4.7].
    /// </summary>
    public static bool Matches(string filter, string topic)
    {
        // A leading wildcard does not match topics beginning with '$'.
        if (topic.Length > 0 && topic[0] == '$' && filter.Length > 0 &&
            (filter[0] == '#' || filter[0] == '+'))
        {
            return false;
        }

        var f = filter.Split('/');
        var t = topic.Split('/');
        for (var i = 0; i < f.Length; i++)
        {
            if (IsLevel(f[i], '#')) return true;     // matches this level and all below
            if (i >= t.Length) return false;
            if (IsLevel(f[i], '+')) continue;        // matches exactly one level
            if (!string.Equals(f[i], t[i], StringComparison.Ordinal)) return false;
        }
        return f.Length == t.Length;
    }

    /// <summary>Validates a subscription filter (wildcard placement rules).</summary>
    public static bool IsValidFilter(string filter)
    {
        if (filter.Length == 0) return false;
        var levels = filter.Split('/');
        for (var i = 0; i < levels.Length; i++)
        {
            var level = levels[i];
            if (IsLevel(level, '#'))
            {
                if (i != levels.Length - 1) return false;   // '#' must be last
            }
            else if (level.Contains('#') || (level.Contains('+') && !IsLevel(level, '+')))
            {
                return false;   // '+'/'#' must occupy a whole level
            }
        }
        return true;
    }

    private static bool IsLevel(string level, char wildcard)
        => level.Length == 1 && level[0] == wildcard;
}
