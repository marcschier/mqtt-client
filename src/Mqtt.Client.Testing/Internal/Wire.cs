// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Mqtt.Client.Testing;

internal enum PacketType : byte
{
    Connect = 1,
    ConnAck = 2,
    Publish = 3,
    PubAck = 4,
    PubRec = 5,
    PubRel = 6,
    PubComp = 7,
    Subscribe = 8,
    SubAck = 9,
    Unsubscribe = 10,
    UnsubAck = 11,
    PingReq = 12,
    PingResp = 13,
    Disconnect = 14,
    Auth = 15,
}

/// <summary>Thrown on a malformed packet; the broker responds by closing the connection.</summary>
internal sealed class MqttBrokerProtocolException : Exception
{
    public MqttBrokerProtocolException(string message) : base(message) { }
}

/// <summary>Forward-only reader over a single decoded packet body.</summary>
internal ref struct BufReader
{
    private readonly ReadOnlySpan<byte> _span;
    private int _pos;

    public BufReader(ReadOnlySpan<byte> span)
    {
        _span = span;
        _pos = 0;
    }

    public int Remaining => _span.Length - _pos;

    public bool End => _pos >= _span.Length;

    public byte ReadByte()
    {
        if (_pos >= _span.Length)
        {
            throw new MqttBrokerProtocolException("Unexpected end of packet.");
        }
        return _span[_pos++];
    }

    public ushort ReadUInt16()
    {
        if (Remaining < 2) throw new MqttBrokerProtocolException("Truncated u16.");
        var v = BinaryPrimitives.ReadUInt16BigEndian(_span.Slice(_pos, 2));
        _pos += 2;
        return v;
    }

    public uint ReadVarInt()
    {
        uint value = 0;
        var multiplier = 1u;
        for (var i = 0; i < 4; i++)
        {
            var b = ReadByte();
            value += (uint)(b & 0x7F) * multiplier;
            if ((b & 0x80) == 0) return value;
            multiplier *= 128;
        }
        throw new MqttBrokerProtocolException("VarInt too long.");
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0 || Remaining < count)
        {
            throw new MqttBrokerProtocolException("Truncated field.");
        }
        var slice = _span.Slice(_pos, count);
        _pos += count;
        return slice;
    }

    public string ReadString()
    {
        var len = ReadUInt16();
        var bytes = ReadBytes(len);
        return Encoding.UTF8.GetString(bytes);
    }

    public ReadOnlySpan<byte> ReadBinary()
    {
        var len = ReadUInt16();
        return ReadBytes(len);
    }

    /// <summary>Reads an MQTT 5 property block (a VarInt length followed by that many bytes).</summary>
    public ReadOnlySpan<byte> ReadProperties()
    {
        var len = (int)ReadVarInt();
        return ReadBytes(len);
    }

    public ReadOnlySpan<byte> ReadRemaining()
    {
        var slice = _span.Slice(_pos);
        _pos = _span.Length;
        return slice;
    }
}

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

internal static class TopicFilter
{
    /// <summary>
    /// Matches a concrete topic against an MQTT subscription filter supporting the <c>+</c>
    /// (single level) and <c>#</c> (multi level) wildcards, per [MQTT-4.7].
    /// </summary>
    public static bool Matches(string filter, string topic)
    {
        // A leading wildcard does not match topics beginning with '$'.
        if (topic.Length > 0 && topic[0] == '$' && filter.Length > 0 &&
            (filter[0] == '#' || filter[0] == '+'))
        {
            return false;
        }

        var f = filter.Split('/');
        var t = topic.Split('/');
        for (var i = 0; i < f.Length; i++)
        {
            if (IsLevel(f[i], '#')) return true;     // matches this level and all below
            if (i >= t.Length) return false;
            if (IsLevel(f[i], '+')) continue;        // matches exactly one level
            if (!string.Equals(f[i], t[i], StringComparison.Ordinal)) return false;
        }
        return f.Length == t.Length;
    }

    /// <summary>Validates a subscription filter (wildcard placement rules).</summary>
    public static bool IsValidFilter(string filter)
    {
        if (filter.Length == 0) return false;
        var levels = filter.Split('/');
        for (var i = 0; i < levels.Length; i++)
        {
            var level = levels[i];
            if (IsLevel(level, '#'))
            {
                if (i != levels.Length - 1) return false;   // '#' must be last
            }
            else if (level.Contains('#') || (level.Contains('+') && !IsLevel(level, '+')))
            {
                return false;   // '+'/'#' must occupy a whole level
            }
        }
        return true;
    }

    private static bool IsLevel(string level, char wildcard)
        => level.Length == 1 && level[0] == wildcard;
}
