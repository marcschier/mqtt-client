// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Mqtt.Client.Buffers;

/// <summary>
/// Pooled buffer writer for MQTT control packets. Wraps an <see cref="ArrayPool{T}"/> rented array
/// and exposes primitive helpers for writing MQTT data representations.
/// </summary>
internal sealed class MqttBufferWriter : IDisposable
{
    private byte[] _buffer;
    private int _written;

    public MqttBufferWriter(int initialCapacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(16, initialCapacity));
        _written = 0;
    }

    public int WrittenCount => _written;

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public void Dispose()
    {
        if (_buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null!;
            _written = 0;
        }
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

    /// <summary>Writes a Variable Byte Integer (per [MQTT-1.5.5]).</summary>
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
    /// Writes a fixed header byte then a placeholder length, returning the offset to patch.
    /// </summary>
    public int WriteFixedHeaderStart(byte firstByte)
    {
        WriteByte(firstByte);
        // Reserve 4 bytes for the worst-case remaining length encoding; we patch the actual
        // length in <see cref="PatchRemainingLength"/>.
        EnsureCapacity(4);
        var offset = _written;
        _written += 4;
        return offset;
    }

    /// <summary>Patches a remaining-length VarInt at the given offset.</summary>
    public void PatchRemainingLength(int lengthFieldOffset, int remainingLength)
    {
        if ((uint)remainingLength > 268_435_455u)
        {
            throw new MqttProtocolException("Remaining length exceeds MQTT maximum.");
        }
        var value = (uint)remainingLength;
        Span<byte> encoded = stackalloc byte[4];
        var count = 0;
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value > 0)
            {
                b |= 0x80;
            }
            encoded[count++] = b;
        }
        while (value > 0);

        var payloadStart = lengthFieldOffset + 4;
        var payloadEnd = _written;
        if (count != 4)
        {
            var shift = 4 - count;
            Buffer.BlockCopy(
                _buffer,
                payloadStart,
                _buffer,
                payloadStart - shift,
                payloadEnd - payloadStart);
            _written -= shift;
        }
        for (var i = 0; i < count; i++)
        {
            _buffer[lengthFieldOffset + i] = encoded[i];
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
