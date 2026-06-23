// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Mqtt.Client;

/// <summary>
/// Allocation-free reader for MQTT-encoded primitives over a <see cref="ReadOnlySequence{Byte}"/>.
/// Inbound packets are almost always contiguous, so a single-segment fast path reads directly from
/// the segment span (via <see cref="BinaryPrimitives"/>) and only falls back to
/// <see cref="SequenceReader{T}"/> for the rare multi-segment case.
/// </summary>
internal ref struct MqttSequenceReader
{
    private readonly ReadOnlySequence<byte> _sequence;
    private readonly bool _single;
    private readonly ReadOnlySpan<byte> _span; // single-segment fast path
    private int _pos;
    private SequenceReader<byte> _reader;      // multi-segment fallback

    public MqttSequenceReader(ReadOnlySequence<byte> sequence)
    {
        _sequence = sequence;
        _pos = 0;
        if (sequence.IsSingleSegment)
        {
            _single = true;
            _span = sequence.FirstSpan;
            _reader = default;
        }
        else
        {
            _single = false;
            _span = default;
            _reader = new SequenceReader<byte>(sequence);
        }
    }

    private ReadOnlySequence<byte> UnreadSequence
        => _single ? _sequence.Slice(_pos) : _sequence.Slice(_reader.Position);

    public long Consumed => _single ? _pos : _reader.Consumed;

    public long Remaining => _single ? _span.Length - _pos : _reader.Remaining;

    public bool End => _single ? _pos >= _span.Length : _reader.End;

    public SequencePosition Position => _single ? _sequence.GetPosition(_pos) : _reader.Position;

    // Guards a length-prefixed read against the unread remainder so a malformed/oversized length
    // throws MqttProtocolException (the decoder's contract) instead of letting
    // ReadOnlySequence.Slice raise ArgumentOutOfRangeException.
    private void EnsureRemaining(int length)
    {
        if (length < 0 || length > Remaining)
        {
            throw new MqttProtocolException("Unexpected end of packet.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        if (_single)
        {
            if ((uint)_pos >= (uint)_span.Length)
            {
                throw new MqttProtocolException("Unexpected end of packet.");
            }
            return _span[_pos++];
        }
        if (!_reader.TryRead(out var value))
        {
            throw new MqttProtocolException("Unexpected end of packet.");
        }
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16BigEndian()
    {
        if (_single)
        {
            if ((uint)(_pos + 2) > (uint)_span.Length)
            {
                throw new MqttProtocolException("Unexpected end of packet.");
            }
            var value = BinaryPrimitives.ReadUInt16BigEndian(_span.Slice(_pos));
            _pos += 2;
            return value;
        }
        if (!_reader.TryReadBigEndian(out short v))
        {
            throw new MqttProtocolException("Unexpected end of packet.");
        }
        return (ushort)v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32BigEndian()
    {
        if (_single)
        {
            if ((uint)(_pos + 4) > (uint)_span.Length)
            {
                throw new MqttProtocolException("Unexpected end of packet.");
            }
            var value = BinaryPrimitives.ReadUInt32BigEndian(_span.Slice(_pos));
            _pos += 4;
            return value;
        }
        if (!_reader.TryReadBigEndian(out int v))
        {
            throw new MqttProtocolException("Unexpected end of packet.");
        }
        return (uint)v;
    }

    public uint ReadVarInt(out int byteCount)
    {
        uint value = 0;
        var multiplier = 1u;
        byteCount = 0;
        while (true)
        {
            var b = ReadByte();
            byteCount++;
            value += (uint)(b & 0x7F) * multiplier;
            if ((b & 0x80) == 0)
            {
                return value;
            }
            multiplier *= 128;
            if (multiplier > 128 * 128 * 128)
            {
                throw new MqttProtocolException("Malformed variable byte integer.");
            }
        }
    }

    public string ReadString()
    {
        var length = ReadUInt16BigEndian();
        if (length == 0)
        {
            return string.Empty;
        }
        if (_single)
        {
            if ((uint)(_pos + length) > (uint)_span.Length)
            {
                throw new MqttProtocolException("Unexpected end of packet.");
            }
            var s = Encoding.UTF8.GetString(_span.Slice(_pos, length));
            _pos += length;
            return s;
        }
        return ReadStringPayloadSegmented(length);
    }

    private string ReadStringPayloadSegmented(int length)
    {
        EnsureRemaining(length);
        var slice = UnreadSequence.Slice(0, length);
        _reader.Advance(length);
        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            slice.CopyTo(rented);
            return Encoding.UTF8.GetString(rented, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public byte[] ReadBinaryData()
    {
        var length = ReadUInt16BigEndian();
        if (length == 0)
        {
            return Array.Empty<byte>();
        }
        if (_single)
        {
            if ((uint)(_pos + length) > (uint)_span.Length)
            {
                throw new MqttProtocolException("Unexpected end of packet.");
            }
            var arr = _span.Slice(_pos, length).ToArray();
            _pos += length;
            return arr;
        }
        EnsureRemaining(length);
        var slice = UnreadSequence.Slice(0, length);
        _reader.Advance(length);
        var copy = new byte[length];
        slice.CopyTo(copy);
        return copy;
    }

    /// <summary>
    /// Reads MQTT binary data as a zero-copy slice when it lies within a single segment; otherwise
    /// copies into a fresh array. Only valid while the underlying buffer outlives the returned memory.
    /// </summary>
    public ReadOnlyMemory<byte> ReadBinaryDataMemory()
    {
        var length = ReadUInt16BigEndian();
        if (length == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
        EnsureRemaining(length);
        var slice = UnreadSequence.Slice(0, length);
        Advance(length);
        if (slice.IsSingleSegment)
        {
            return slice.First;
        }
        var arr = new byte[length];
        slice.CopyTo(arr);
        return arr;
    }

    public ReadOnlySequence<byte> ReadSequence(int length)
    {
        EnsureRemaining(length);
        var slice = UnreadSequence.Slice(0, length);
        Advance(length);
        return slice;
    }

    public void Advance(long count)
    {
        if (_single)
        {
            _pos += (int)count;
        }
        else
        {
            _reader.Advance(count);
        }
    }
}
