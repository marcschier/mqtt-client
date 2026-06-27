// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Mqtt.Client;

internal static class SequenceCompat
{
    /// <summary>
    /// <c>ReadOnlySequence&lt;byte&gt;.FirstSpan</c> equivalent. That property exists from
    /// .NET Core 3.0 / netstandard2.1; on netstandard2.0 it is derived from <c>First.Span</c>.
    /// Aggressively inlined so the modern path is identical to the direct property access.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> FirstSpan(this ReadOnlySequence<byte> sequence)
#if NETSTANDARD2_0
        => sequence.First.Span;
#else
        => sequence.FirstSpan;
#endif
}
