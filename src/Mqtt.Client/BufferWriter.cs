// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Mqtt.Client;

/// <summary>
/// Pooled buffer writer for MQTT control packets. A mutable value type that wraps an
/// <see cref="ArrayPool{T}"/> rented array and exposes primitive helpers for writing MQTT data
/// representations.
/// </summary>
/// <remarks>
/// Being a struct, instances live on the stack (no per-packet object allocation); only the backing
/// array is pooled. Pass by <c>ref</c> wherever mutation must be observed by the caller (encoders
/// take <c>ref TWriter</c>); copies share the same backing array, so exactly one copy may be
/// disposed to avoid a double-return to the pool.
/// </remarks>
internal struct MqttBufferWriter : IMqttBufferWriter
{
    private byte[] _buffer;
    private int _written;

    public MqttBufferWriter(int initialCapacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(16, initialCapacity));
        _written = 0;
    }

    public readonly int WrittenCount => _written;

    public readonly ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public readonly ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public void Dispose()
    {
        if (_buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null!;
            _written = 0;
        }
    }

    /// <summary>
    /// Transfers ownership of the rented backing array to the caller and resets this writer to an
    /// empty, non-owning state. The caller MUST return the array to
    /// <see cref="ArrayPool{T}.Shared"/>. Used to hand the encoded bytes to an outbound envelope
    /// without a copy.
    /// </summary>
    public byte[] DetachBuffer(out int length)
    {
        length = _written;
        var detached = _buffer;
        _buffer = null!;
        _written = 0;
        return detached;
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint <= 0 ? 1 : sizeHint);
        return _buffer.AsSpan(_written);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint <= 0 ? 1 : sizeHint);
        return _buffer.AsMemory(_written);
    }

    public void Advance(int count)
    {
        if (count < 0 || _written + count > _buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        _written += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_written++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16BigEndian(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(_written, 2), value);
        _written += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32BigEndian(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(_written, 4), value);
        _written += 4;
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(_written));
        _written += value.Length;
    }

    /// <summary>
    /// Writes every segment of a <see cref="ReadOnlySequence{T}"/> contiguously.
    /// </summary>
    public void WriteBytes(in ReadOnlySequence<byte> value)
    {
        if (value.IsSingleSegment)
        {
            WriteBytes(value.FirstSpan);
            return;
        }
        foreach (var segment in value)
        {
            WriteBytes(segment.Span);
        }
    }

    /// <summary>
    /// Writes an MQTT UTF-8 string: 2-byte big-endian length prefix followed by UTF-8 bytes.
    /// </summary>
    public void WriteString(string value)
    {
        if (value is null)
        {
            WriteUInt16BigEndian(0);
            return;
        }
        var maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
        EnsureCapacity(2 + maxBytes);
        var span = _buffer.AsSpan(_written + 2);
        var actual = Encoding.UTF8.GetBytes(value.AsSpan(), span);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(_written, 2), checked((ushort)actual));
        _written += 2 + actual;
    }

    /// <summary>
    /// Writes MQTT binary data: 2-byte big-endian length prefix followed by raw bytes.
    /// </summary>
    public void WriteBinaryData(ReadOnlySpan<byte> value)
    {
        WriteUInt16BigEndian(checked((ushort)value.Length));
        WriteBytes(value);
    }

    /// <summary>
    /// Writes MQTT binary data from a sequence: 2-byte length prefix followed by all segments.
    /// </summary>
    public void WriteBinaryData(in ReadOnlySequence<byte> value)
    {
        WriteUInt16BigEndian(checked((ushort)value.Length));
        WriteBytes(value);
    }

    /// <summary>
    /// Writes a Variable Byte Integer (per [MQTT-1.5.5]).
    /// </summary>
    public void WriteVarInt(uint value)
    {
        if (value > 268_435_455u)
        {
            throw new MqttProtocolException("Variable byte integer exceeds 268,435,455.");
        }
        EnsureCapacity(4);
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value > 0)
            {
                b |= 0x80;
            }
            _buffer[_written++] = b;
        }
        while (value > 0);
    }

    /// <summary>
    /// Writes a fixed header byte then a single placeholder length byte, returning the offset to
    /// patch. One byte is reserved optimistically (covers remaining lengths 0–127, the common
    /// case); <see cref="PatchRemainingLength"/> grows the field in place only when a larger
    /// remaining length needs more varint bytes.
    /// </summary>
    public int WriteFixedHeaderStart(byte firstByte)
    {
        WriteByte(firstByte);
        EnsureCapacity(1);
        var offset = _written;
        _written++;
        return offset;
    }

    /// <summary>
    /// Patches a remaining-length VarInt at the given offset (reserved as a single byte by
    /// <see cref="WriteFixedHeaderStart"/>). When the value needs more than one byte, the body that
    /// follows is shifted right to make room; the 1-byte common case never shifts.
    /// </summary>
    public void PatchRemainingLength(int lengthFieldOffset, int remainingLength)
    {
        if ((uint)remainingLength > 268_435_455u)
        {
            throw new MqttProtocolException("Remaining length exceeds MQTT maximum.");
        }
        PatchVarIntField(lengthFieldOffset, (uint)remainingLength);
    }

    /// <summary>
    /// Reserves a single placeholder byte for a length-prefixed section (e.g. an MQTT 5 property
    /// block) and returns its offset; pair with <see cref="PatchVarIntField"/> after the section is
    /// written.
    /// </summary>
    public int ReserveVarIntPlaceholder()
    {
        EnsureCapacity(1);
        var offset = _written;
        _written++;
        return offset;
    }

    /// <summary>
    /// Writes <paramref name="value"/> as a VarInt into the single byte reserved at
    /// <paramref name="fieldOffset"/>, shifting the trailing body right when the value needs more
    /// than one byte. The 1-byte common case writes in place with no copy.
    /// </summary>
    public void PatchVarIntField(int fieldOffset, uint value)
    {
        Span<byte> encoded = stackalloc byte[4];
        var count = 0;
        var v = value;
        do
        {
            var b = (byte)(v & 0x7F);
            v >>= 7;
            if (v > 0)
            {
                b |= 0x80;
            }
            encoded[count++] = b;
        }
        while (v > 0);

        if (count > 1)
        {
            // Grow the 1-byte placeholder to `count` bytes: shift the body that follows it right.
            var extra = count - 1;
            EnsureCapacity(extra);
            var bodyStart = fieldOffset + 1;
            var bodyLen = _written - bodyStart;
            Buffer.BlockCopy(_buffer, bodyStart, _buffer, bodyStart + extra, bodyLen);
            _written += extra;
        }
        for (var i = 0; i < count; i++)
        {
            _buffer[fieldOffset + i] = encoded[i];
        }
    }

    private void EnsureCapacity(int additional)
    {
        var required = _written + additional;
        if (required <= _buffer.Length)
        {
            return;
        }
        var newSize = Math.Max(_buffer.Length * 2, required);
        var newBuf = ArrayPool<byte>.Shared.Rent(newSize);
        Buffer.BlockCopy(_buffer, 0, newBuf, 0, _written);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuf;
    }
}
