// Copyright (c) 2026 marcschier. Licensed under the MIT License.

#if NETSTANDARD2_0
using System.Buffers.Binary;

// Minimal System.Buffers.SequenceReader<T> polyfill (the BCL type is .NET Core 3.0 / not in
// netstandard2.0). Implements only the members the multi-segment decode fallback in
// MqttSequenceReader uses. This path is never taken on net8.0+ / netstandard2.1, so it does not
// affect those builds.
namespace System.Buffers;

internal ref struct SequenceReader<T>
    where T : unmanaged, IEquatable<T>
{
    private readonly ReadOnlySequence<T> _sequence;
    private long _consumed;

    public SequenceReader(ReadOnlySequence<T> sequence)
    {
        _sequence = sequence;
        _consumed = 0;
    }

    public readonly long Consumed => _consumed;

    public readonly long Remaining => _sequence.Length - _consumed;

    public readonly bool End => _consumed >= _sequence.Length;

    public readonly SequencePosition Position => _sequence.GetPosition(_consumed);

    public void Advance(long count) => _consumed += count;

    public bool TryRead(out T value)
    {
        foreach (var segment in _sequence.Slice(_consumed))
        {
            if (segment.Length > 0)
            {
                value = segment.Span[0];
                _consumed++;
                return true;
            }
        }
        value = default;
        return false;
    }

    public readonly bool TryCopyTo(Span<T> destination)
    {
        if (Remaining < destination.Length) return false;
        _sequence.Slice(_consumed, destination.Length).CopyTo(destination);
        return true;
    }
}

internal static class SequenceReaderExtensions
{
    public static bool TryReadBigEndian(ref this SequenceReader<byte> reader, out short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (!reader.TryCopyTo(buffer)) { value = 0; return false; }
        reader.Advance(2);
        value = BinaryPrimitives.ReadInt16BigEndian(buffer);
        return true;
    }

    public static bool TryReadBigEndian(ref this SequenceReader<byte> reader, out int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (!reader.TryCopyTo(buffer)) { value = 0; return false; }
        reader.Advance(4);
        value = BinaryPrimitives.ReadInt32BigEndian(buffer);
        return true;
    }
}
#endif
