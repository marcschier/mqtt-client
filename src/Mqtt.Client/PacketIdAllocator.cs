// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Mqtt.Client;

/// <summary>
/// Allocates MQTT packet identifiers (1..65535) using a circular bitmap. Lock-free for the common
/// "next free id" path via interlocked CAS on per-word state.
/// </summary>
internal sealed class PacketIdAllocator
{
    private const int Bits = 65535; // valid ids 1..65535
    // Lazily allocated (8 KB) so QoS-0-only or short-lived clients never pay for it. Created with a
    // lock-free CAS on first Allocate; Release no-ops until then.
    private int[]? _bitmap;
    private int _cursor;

    private int[] Bitmap()
    {
        var map = Volatile.Read(ref _bitmap);
        if (map is not null)
        {
            return map;
        }
        var created = new int[(Bits + 31) / 32];
        return Interlocked.CompareExchange(ref _bitmap, created, null) ?? created;
    }

    public ushort Allocate()
    {
        var bitmap = Bitmap();
        var totalWords = bitmap.Length; // 2048 words => 65536 bits; bit 65535 is the unused spare.
        var start = _cursor;
        if ((uint)start >= (uint)Bits) start = 0;
        var firstWord = start >> 5;  // / 32
        var firstBit = start & 31;   // % 32

        // Word scan: visit each 32-bit word once in circular order from the cursor, using
        // BitOperations.TrailingZeroCount to jump straight to the first free bit (skipping full
        // words in O(1)). The cursor word is split into two visits (high bits at w==0, low bits at
        // w==totalWords) so every valid id is considered exactly once.
        for (var w = 0; w <= totalWords; w++)
        {
            int wordIdx;
            uint eligible;
            if (w == 0)
            {
                wordIdx = firstWord;
                eligible = uint.MaxValue << firstBit; // bits [firstBit, 32)
            }
            else if (w == totalWords)
            {
                if (firstBit == 0) break; // first word was already fully covered at w == 0
                wordIdx = firstWord;
                eligible = (1u << firstBit) - 1; // bits [0, firstBit)
            }
            else
            {
                wordIdx = firstWord + w;
                if (wordIdx >= totalWords) wordIdx -= totalWords;
                eligible = uint.MaxValue; // bits [0, 32)
            }

            // Exclude global indices >= Bits (the single spare bit in the last word).
            var baseIdx = wordIdx << 5;
            var validBits = Bits - baseIdx;
            if (validBits <= 0) continue;
            if (validBits < 32) eligible &= (1u << validBits) - 1;
            if (eligible == 0) continue;

            ref var word = ref bitmap[wordIdx];
            while (true)
            {
                var current = (uint)Volatile.Read(ref word);
                var free = ~current & eligible;
                if (free == 0) break; // no eligible free bit in this word; move on
                var bit = TrailingZeroCount(free);
                var mask = 1u << bit;
                if (Interlocked.CompareExchange(ref word, (int)(current | mask), (int)current)
                    == (int)current)
                {
                    var idx = baseIdx + bit;
                    Volatile.Write(ref _cursor, idx + 1);
                    return (ushort)(idx + 1);
                }
                // CAS lost to a concurrent allocator; re-read and retry this word.
            }
        }
        throw new InvalidOperationException("All MQTT packet identifiers are in use.");
    }

    public void Release(ushort packetId)
    {
        if (packetId == 0) return;
        var bitmap = Volatile.Read(ref _bitmap);
        if (bitmap is null) return;
        var idx = packetId - 1;
        ref var word = ref bitmap[idx / 32];
        var mask = 1 << (idx % 32);
        while (true)
        {
            var current = Volatile.Read(ref word);
            var next = current & ~mask;
            if (Interlocked.CompareExchange(ref word, next, current) == current) return;
        }
    }

    /// <summary>
    /// Marks a specific identifier as in use (idempotent). Restores packet-identifier reservations
    /// for persisted in-flight publishes after a Session-Present reconnect.
    /// </summary>
    public void Reserve(ushort packetId)
    {
        if (packetId == 0) return;
        var bitmap = Bitmap();
        var idx = packetId - 1;
        ref var word = ref bitmap[idx / 32];
        var mask = 1 << (idx % 32);
        while (true)
        {
            var current = Volatile.Read(ref word);
            var next = current | mask;
            if (Interlocked.CompareExchange(ref word, next, current) == current) return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int TrailingZeroCount(uint value)
    {
#if NETSTANDARD2_1
        // BitOperations is .NET Core 3.0+. Callers only pass non-zero values, so this terminates.
        var count = 0;
        while ((value & 1u) == 0u)
        {
            value >>= 1;
            count++;
        }
        return count;
#else
        return System.Numerics.BitOperations.TrailingZeroCount(value);
#endif
    }
}
