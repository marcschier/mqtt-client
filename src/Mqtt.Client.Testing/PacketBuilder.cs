// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Mqtt.Client.Testing;

/// <summary>Grows a byte buffer while encoding a packet body; <see cref="Frame"/> adds the header.</summary>
internal sealed class PacketBuilder
{
    private byte[] _buf;
    private int _len;

    public PacketBuilder(int capacity = 64)
    {
        _buf = new byte[capacity < 8 ? 8 : capacity];
        _len = 0;
    }

    private void Ensure(int extra)
    {
        if (_len + extra <= _buf.Length) return;
        var size = _buf.Length * 2;
        while (size < _len + extra) size *= 2;
        Array.Resize(ref _buf, size);
    }

    public void Byte(byte value)
    {
        Ensure(1);
        _buf[_len++] = value;
    }

    public void UInt16(ushort value)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16BigEndian(_buf.AsSpan(_len, 2), value);
        _len += 2;
    }

    public void VarInt(uint value)
    {
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value > 0) b |= 0x80;
            Byte(b);
        }
        while (value > 0);
    }

    public void Raw(ReadOnlySpan<byte> value)
    {
        Ensure(value.Length);
        value.CopyTo(_buf.AsSpan(_len));
        _len += value.Length;
    }

    public void Str(string value)
    {
        var count = Encoding.UTF8.GetByteCount(value);
        UInt16((ushort)count);
        Ensure(count);
        Encoding.UTF8.GetBytes(value, 0, value.Length, _buf, _len);
        _len += count;
    }

    public void Bin(ReadOnlySpan<byte> value)
    {
        UInt16((ushort)value.Length);
        Raw(value);
    }

    public ReadOnlySpan<byte> Body => _buf.AsSpan(0, _len);

    /// <summary>Frames a body: header byte + VarInt remaining length + body.</summary>
    public static byte[] Frame(byte firstByte, ReadOnlySpan<byte> body)
    {
        Span<byte> lenBytes = stackalloc byte[4];
        var n = 0;
        var rem = (uint)body.Length;
        do
        {
            var b = (byte)(rem & 0x7F);
            rem >>= 7;
            if (rem > 0) b |= 0x80;
            lenBytes[n++] = b;
        }
        while (rem > 0);

        var packet = new byte[1 + n + body.Length];
        packet[0] = firstByte;
        lenBytes.Slice(0, n).CopyTo(packet.AsSpan(1));
        body.CopyTo(packet.AsSpan(1 + n));
        return packet;
    }

    public static byte FirstByte(PacketType type, int flags = 0)
        => (byte)(((int)type << 4) | flags);
}
