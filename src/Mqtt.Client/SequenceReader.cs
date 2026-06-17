// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Mqtt.Client.Buffers;

/// <summary>Allocation-free reader for MQTT-encoded primitives over a <see cref="ReadOnlySequence{Byte}"/>.</summary>
internal ref struct MqttSequenceReader
{
    private SequenceReader<byte> _reader;
    private readonly ReadOnlySequence<byte> _sequence;

    public MqttSequenceReader(ReadOnlySequence<byte> sequence)
    {
        _sequence = sequence;
        _reader = new SequenceReader<byte>(sequence);
    }

    private ReadOnlySequence<byte> UnreadSequence => _sequence.Slice(_reader.Position);

    public long Consumed => _reader.Consumed;

    public long Remaining => _reader.Remaining;

    public bool End => _reader.End;

    public SequencePosition Position => _reader.Position;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        if (!_reader.TryRead(out var value))
        {
            throw new MqttProtocolException("Unexpected end of packet.");
        }
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16BigEndian()
    {
        if (!_reader.TryReadBigEndian(out short value))
        {
            throw new MqttProtocolException("Unexpected end of packet.");
        }
        return (ushort)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32BigEndian()
    {
        if (!_reader.TryReadBigEndian(out int value))
        {
            throw new MqttProtocolException("Unexpected end of packet.");
        }
        return (uint)value;
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
        return ReadStringPayload(length);
    }

    private string ReadStringPayload(int length)
    {
        // Slice and decode UTF-8; handles segmented sequences via a pooled scratch buffer.
        var slice = UnreadSequence.Slice(0, length);
        _reader.Advance(length);
        if (slice.IsSingleSegment)
        {
            return Encoding.UTF8.GetString(slice.FirstSpan);
        }
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
        var slice = UnreadSequence.Slice(0, length);
        _reader.Advance(length);
        var arr = new byte[length];
        slice.CopyTo(arr);
        return arr;
    }

    public ReadOnlySequence<byte> ReadSequence(int length)
    {
        var slice = UnreadSequence.Slice(0, length);
        _reader.Advance(length);
        return slice;
    }

    public void Advance(long count) => _reader.Advance(count);
}
