// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;

namespace Mqtt.Client;

/// <summary>
/// An <see cref="IMqttBufferWriter"/> that encodes a control packet directly into a
/// <see cref="PipeWriter"/>'s buffer, avoiding the intermediate pooled array and the header copy
/// that <see cref="MqttBufferWriter"/> incurs. Construct one per packet, encode, then call
/// <see cref="Commit"/> to advance the pipe; payload bytes are streamed separately.
/// </summary>
/// <remarks>
/// MQTT length-prefix fields are back-patched after the body is written, which needs random access
/// to the already-written bytes. A <see cref="PipeWriter"/> only guarantees that within a single
/// un-advanced <c>GetMemory</c> buffer, so when the header outgrows the current buffer
/// <see cref="Grow"/> requests a larger one and copies the partial header into it, keeping the whole
/// header contiguous until <see cref="Commit"/>. A non-ref struct (it holds a
/// <see cref="Memory{T}"/>, not a <see cref="Span{T}"/>), so it satisfies the encoders'
/// <c>where TWriter : struct</c> constraint on every target framework and stays AOT-clean.
/// </remarks>
internal struct PipeBufferWriter : IMqttBufferWriter
{
    private readonly PipeWriter _output;
    private Memory<byte> _memory;
    private int _written;

    public PipeBufferWriter(PipeWriter output, int sizeHint)
    {
        _output = output;
        _memory = output.GetMemory(Math.Max(16, sizeHint));
        _written = 0;
    }

    public readonly int WrittenCount => _written;

    public readonly ReadOnlyMemory<byte> WrittenMemory => _memory.Slice(0, _written);

    public readonly ReadOnlySpan<byte> WrittenSpan => _memory.Span.Slice(0, _written);

    /// <summary>
    /// Advances the underlying <see cref="PipeWriter"/> by the number of bytes written and resets
    /// the cursor. Call exactly once after encoding a packet, before streaming any payload.
    /// </summary>
    public void Commit()
    {
        _output.Advance(_written);
        _written = 0;
    }

    /// <summary>
    /// No-op: the <see cref="PipeWriter"/> owns its memory, so there is nothing to return.
    /// </summary>
    public readonly void Dispose()
    {
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint <= 0 ? 1 : sizeHint);
        return _memory.Span.Slice(_written);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint <= 0 ? 1 : sizeHint);
        return _memory.Slice(_written);
    }

    public void Advance(int count)
    {
        if (count < 0 || _written + count > _memory.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        _written += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _memory.Span[_written++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16BigEndian(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16BigEndian(_memory.Span.Slice(_written, 2), value);
        _written += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32BigEndian(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32BigEndian(_memory.Span.Slice(_written, 4), value);
        _written += 4;
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_memory.Span.Slice(_written));
        _written += value.Length;
    }

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

    public void WriteString(string value)
    {
        if (value is null)
        {
            WriteUInt16BigEndian(0);
            return;
        }
        var maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
        EnsureCapacity(2 + maxBytes);
        var span = _memory.Span;
        var actual = Encoding.UTF8.GetBytes(value.AsSpan(), span.Slice(_written + 2));
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(_written, 2), checked((ushort)actual));
        _written += 2 + actual;
    }

    public void WriteBinaryData(ReadOnlySpan<byte> value)
    {
        WriteUInt16BigEndian(checked((ushort)value.Length));
        WriteBytes(value);
    }

    public void WriteBinaryData(in ReadOnlySequence<byte> value)
    {
        WriteUInt16BigEndian(checked((ushort)value.Length));
        WriteBytes(value);
    }

    public void WriteVarInt(uint value)
    {
        if (value > 268_435_455u)
        {
            throw new MqttProtocolException("Variable byte integer exceeds 268,435,455.");
        }
        EnsureCapacity(4);
        var span = _memory.Span;
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value > 0)
            {
                b |= 0x80;
            }
            span[_written++] = b;
        }
        while (value > 0);
    }

    public int WriteFixedHeaderStart(byte firstByte)
    {
        WriteByte(firstByte);
        EnsureCapacity(1);
        var offset = _written;
        _written++;
        return offset;
    }

    public void PatchRemainingLength(int lengthFieldOffset, int remainingLength)
    {
        if ((uint)remainingLength > 268_435_455u)
        {
            throw new MqttProtocolException("Remaining length exceeds MQTT maximum.");
        }
        PatchVarIntField(lengthFieldOffset, (uint)remainingLength);
    }

    public int ReserveVarIntPlaceholder()
    {
        EnsureCapacity(1);
        var offset = _written;
        _written++;
        return offset;
    }

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
            // Span.CopyTo is memmove-safe, so the overlapping right shift is correct.
            var extra = count - 1;
            EnsureCapacity(extra);
            var body = _memory.Span;
            var bodyStart = fieldOffset + 1;
            var bodyLen = _written - bodyStart;
            body.Slice(bodyStart, bodyLen).CopyTo(body.Slice(bodyStart + extra, bodyLen));
            _written += extra;
        }
        var span = _memory.Span;
        for (var i = 0; i < count; i++)
        {
            span[fieldOffset + i] = encoded[i];
        }
    }

    private void EnsureCapacity(int additional)
    {
        if (_written + additional > _memory.Length)
        {
            Grow(_written + additional);
        }
    }

    private void Grow(int required)
    {
        // The current PipeWriter buffer can't hold the (still un-advanced) header. Request a larger
        // one and copy the partial header into it so back-patching stays possible. The previous
        // buffer is abandoned un-advanced (contributes nothing on flush). Rare: only headers that
        // outgrow the pipe's segment hit this path.
        var newMemory = _output.GetMemory(required);
        _memory.Span.Slice(0, _written).CopyTo(newMemory.Span);
        _memory = newMemory;
    }
}
