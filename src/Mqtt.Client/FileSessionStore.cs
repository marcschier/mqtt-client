// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client.Persistence;

/// <summary>
/// File-system-backed <see cref="IPersistentSessionStore"/>. Each pending publish is stored as
/// a single binary file named <c>{packetId}.bin</c> inside <see cref="Directory"/>. The format is
/// compact and NativeAOT-friendly (no reflection / JSON):
/// <c>[ushort QoS][byte Retain][ushort topicLen][topic UTF-8][int payloadLen][payload bytes]</c>.
/// Optional MQTT 5 publish properties are intentionally not persisted; restore best-effort.
/// </summary>
public sealed class FileSessionStore : IPersistentSessionStore
{
    private readonly object _gate = new();

    public FileSessionStore(string directory)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        System.IO.Directory.CreateDirectory(directory);
    }

    public string Directory { get; }

    public ValueTask SavePendingPublishAsync(ushort packetId, MqttMessage message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        var topicBytes = Encoding.UTF8.GetBytes(message.Topic);
        var payload = message.Payload.Span;
        var bufLen = 2 + 1 + 2 + topicBytes.Length + 4 + payload.Length;
        var buf = new byte[bufLen];
        var s = buf.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(s.Slice(0, 2), (ushort)message.QoS);
        s[2] = (byte)(message.Retain ? 1 : 0);
        BinaryPrimitives.WriteUInt16BigEndian(s.Slice(3, 2), (ushort)topicBytes.Length);
        topicBytes.AsSpan().CopyTo(s.Slice(5));
        BinaryPrimitives.WriteInt32BigEndian(s.Slice(5 + topicBytes.Length, 4), payload.Length);
        payload.CopyTo(s.Slice(5 + topicBytes.Length + 4));

        var path = Path.Combine(Directory, packetId + ".bin");
        var tmp = path + ".tmp";
        lock (_gate)
        {
            File.WriteAllBytes(tmp, buf);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        return default;
    }

    public ValueTask RemovePendingPublishAsync(ushort packetId)
    {
        var path = Path.Combine(Directory, packetId + ".bin");
        lock (_gate)
        {
            if (File.Exists(path)) File.Delete(path);
        }
        return default;
    }

    public ValueTask<IReadOnlyList<(ushort PacketId, MqttMessage Message)>> ListPendingPublishesAsync()
    {
        var list = new List<(ushort, MqttMessage)>();
        lock (_gate)
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.bin"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!ushort.TryParse(name, out var packetId)) continue;
                byte[] buf;
                try { buf = File.ReadAllBytes(file); }
                catch (IOException) { continue; }
                if (buf.Length < 7) continue;
                var s = buf.AsSpan();
                var qos = (MqttQoS)BinaryPrimitives.ReadUInt16BigEndian(s.Slice(0, 2));
                var retain = s[2] != 0;
                var topicLen = BinaryPrimitives.ReadUInt16BigEndian(s.Slice(3, 2));
                if (buf.Length < 5 + topicLen + 4) continue;
                var topic = Encoding.UTF8.GetString(s.Slice(5, topicLen));
                var payloadLen = BinaryPrimitives.ReadInt32BigEndian(s.Slice(5 + topicLen, 4));
                if (payloadLen < 0 || buf.Length < 5 + topicLen + 4 + payloadLen) continue;
                var payload = new byte[payloadLen];
                s.Slice(5 + topicLen + 4, payloadLen).CopyTo(payload);
                list.Add((packetId, new MqttMessage { Topic = topic, Payload = payload, QoS = qos, Retain = retain }));
            }
        }
        return new ValueTask<IReadOnlyList<(ushort, MqttMessage)>>(list);
    }

    public ValueTask ClearAsync()
    {
        lock (_gate)
        {
            if (!System.IO.Directory.Exists(Directory)) return default;
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.bin"))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
        return default;
    }
}
