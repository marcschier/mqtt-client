// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Mqtt.Client.Testing;

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
