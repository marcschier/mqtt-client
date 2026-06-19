// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;

namespace Mqtt.Client;

/// <summary>
/// Abstraction over a pooled MQTT buffer writer. Extends <see cref="IBufferWriter{T}"/> with the
/// MQTT data-representation helpers plus the random-access back-patch operations (length-prefix
/// fields whose value is unknown until the section is written) that a forward-only
/// <see cref="IBufferWriter{T}"/> cannot express.
/// </summary>
/// <remarks>
/// Encoders are generic over this interface with a <c>struct</c> constraint
/// (<c>where TWriter : struct, IMqttBufferWriter</c>) so the JIT specializes each instantiation:
/// interface calls devirtualize, nothing boxes, and it stays AOT-clean on every target.
/// </remarks>
internal interface IMqttBufferWriter : IBufferWriter<byte>, IDisposable
{
    /// <summary>
    /// Gets the number of bytes written so far.
    /// </summary>
    int WrittenCount { get; }

    /// <summary>
    /// Gets the written bytes as memory. Valid until the next write grows the backing buffer.
    /// </summary>
    ReadOnlyMemory<byte> WrittenMemory { get; }

    /// <summary>
    /// Gets the written bytes as a span. Valid until the next write grows the backing buffer.
    /// </summary>
    ReadOnlySpan<byte> WrittenSpan { get; }

    /// <summary>
    /// Writes a single byte.
    /// </summary>
    void WriteByte(byte value);

    /// <summary>
    /// Writes a 16-bit big-endian integer.
    /// </summary>
    void WriteUInt16BigEndian(ushort value);

    /// <summary>
    /// Writes a 32-bit big-endian integer.
    /// </summary>
    void WriteUInt32BigEndian(uint value);

    /// <summary>
    /// Writes raw bytes.
    /// </summary>
    void WriteBytes(ReadOnlySpan<byte> value);

    /// <summary>
    /// Writes every segment of a sequence contiguously.
    /// </summary>
    void WriteBytes(in ReadOnlySequence<byte> value);

    /// <summary>
    /// Writes an MQTT UTF-8 string (2-byte length prefix + UTF-8 bytes).
    /// </summary>
    void WriteString(string value);

    /// <summary>
    /// Writes MQTT binary data (2-byte length prefix + raw bytes).
    /// </summary>
    void WriteBinaryData(ReadOnlySpan<byte> value);

    /// <summary>
    /// Writes MQTT binary data from a sequence (2-byte length prefix + all segments).
    /// </summary>
    void WriteBinaryData(in ReadOnlySequence<byte> value);

    /// <summary>
    /// Writes a Variable Byte Integer (per [MQTT-1.5.5]).
    /// </summary>
    void WriteVarInt(uint value);

    /// <summary>
    /// Writes a fixed-header byte then reserves a single placeholder length byte, returning the
    /// offset to patch later via <see cref="PatchRemainingLength"/>.
    /// </summary>
    int WriteFixedHeaderStart(byte firstByte);

    /// <summary>
    /// Patches a remaining-length VarInt at the offset returned by
    /// <see cref="WriteFixedHeaderStart"/>.
    /// </summary>
    void PatchRemainingLength(int lengthFieldOffset, int remainingLength);

    /// <summary>
    /// Reserves a single placeholder byte for a length-prefixed section and returns its offset;
    /// pair with <see cref="PatchVarIntField"/>.
    /// </summary>
    int ReserveVarIntPlaceholder();

    /// <summary>
    /// Writes <paramref name="value"/> as a VarInt into the byte reserved at
    /// <paramref name="fieldOffset"/>, shifting the trailing body when more than one byte is needed.
    /// </summary>
    void PatchVarIntField(int fieldOffset, uint value);
}
