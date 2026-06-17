// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Threading;

namespace Mqtt.Client;

/// <summary>
/// Allocates MQTT packet identifiers (1..65535) using a circular bitmap. Lock-free for the common
/// "next free id" path via interlocked CAS on per-word state.
/// </summary>
internal sealed class PacketIdAllocator
{
    private const int Bits = 65535; // valid ids 1..65535
    private readonly int[] _bitmap = new int[(Bits + 31) / 32];
    private int _cursor;

    public ushort Allocate()
    {
        var start = _cursor;
        for (var i = 0; i < Bits; i++)
        {
            var idx = (start + i) % Bits;
            ref var word = ref _bitmap[idx / 32];
            var mask = 1 << (idx % 32);
            while (true)
            {
                var current = Volatile.Read(ref word);
                if ((current & mask) != 0) break;
                var next = current | mask;
                if (Interlocked.CompareExchange(ref word, next, current) == current)
                {
                    Volatile.Write(ref _cursor, idx + 1);
                    return (ushort)(idx + 1);
                }
            }
        }
        throw new InvalidOperationException("All MQTT packet identifiers are in use.");
    }

    public void Release(ushort packetId)
    {
        if (packetId == 0) return;
        var idx = packetId - 1;
        ref var word = ref _bitmap[idx / 32];
        var mask = 1 << (idx % 32);
        while (true)
        {
            var current = Volatile.Read(ref word);
            var next = current & ~mask;
            if (Interlocked.CompareExchange(ref word, next, current) == current) return;
        }
    }
}
