// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Mqtt.Client;

/// <summary>
/// File-system-backed <see cref="IPersistentSessionStore"/>. Each pending publish is stored as
/// a single binary file named <c>{packetId}.bin</c> inside <see cref="Directory"/>. The format is
/// compact and NativeAOT-friendly (no reflection / JSON):
/// <c>[ushort QoS][byte Retain][ushort topicLen][topic UTF-8][int payloadLen][payload bytes]</c>.
/// Optional MQTT 5 publish properties are intentionally not persisted; restore best-effort.
/// </summary>
public sealed class FileSessionStore : IPersistentSessionStore, IPersistentInboundQoS2Store
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
        var topic = message.Topic;
        var topicLen = Encoding.UTF8.GetByteCount(topic);
        var payload = message.PayloadMemory.Span;
        var bufLen = 2 + 1 + 2 + topicLen + 4 + payload.Length;
        var buf = ArrayPool<byte>.Shared.Rent(bufLen);
        try
        {
            var s = buf.AsSpan();
            BinaryPrimitives.WriteUInt16BigEndian(s.Slice(0, 2), (ushort)message.QoS);
            s[2] = (byte)(message.Retain ? 1 : 0);
            BinaryPrimitives.WriteUInt16BigEndian(s.Slice(3, 2), (ushort)topicLen);
            Encoding.UTF8.GetBytes(topic.AsSpan(), s.Slice(5, topicLen));
            BinaryPrimitives.WriteInt32BigEndian(s.Slice(5 + topicLen, 4), payload.Length);
            payload.CopyTo(s.Slice(5 + topicLen + 4));

            var path = Path.Combine(Directory, packetId + ".bin");
            var tmp = path + ".tmp";
            lock (_gate)
            {
                using (var fs = File.Create(tmp))
                {
                    fs.Write(buf, 0, bufLen);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
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

    public ValueTask<IReadOnlyList<(ushort PacketId, MqttMessage Message)>>
        ListPendingPublishesAsync()
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
                // Owned copy: the restored payload escapes to the caller (and survives this method
                // and the file handle) with indefinite lifetime, so it cannot be pooled.
                var payload = new byte[payloadLen];
                s.Slice(5 + topicLen + 4, payloadLen).CopyTo(payload);
                list.Add((packetId, new MqttMessage {
                    Topic = topic,
                    PayloadMemory = payload,
                    QoS = qos,
                    Retain = retain }));
            }
        }
        return new ValueTask<IReadOnlyList<(ushort, MqttMessage)>>(list);
    }

    public ValueTask SaveReceivedQoS2Async(ushort packetId)
    {
        var path = Path.Combine(Directory, packetId + ".q2");
        lock (_gate)
        {
            if (!File.Exists(path)) File.Create(path).Dispose();
        }
        return default;
    }

    public ValueTask RemoveReceivedQoS2Async(ushort packetId)
    {
        var path = Path.Combine(Directory, packetId + ".q2");
        lock (_gate)
        {
            if (File.Exists(path)) File.Delete(path);
        }
        return default;
    }

    public ValueTask<IReadOnlyList<ushort>> ListReceivedQoS2Async()
    {
        var list = new List<ushort>();
        lock (_gate)
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.q2"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (ushort.TryParse(name, out var packetId)) list.Add(packetId);
            }
        }
        return new ValueTask<IReadOnlyList<ushort>>(list);
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
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.q2"))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
        return default;
    }
}
