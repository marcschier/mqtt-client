// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client.Testing;

/// <summary>Handles a single client connection: CONNECT/CONNACK, the packet loop, and delivery.</summary>
internal sealed class BrokerConnection : IDisposable
{
    private const byte ProtocolV5 = 5;

    private readonly MqttTestBroker _broker;
    private readonly TcpClient _client;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _stateGate = new();
    private readonly List<(string Filter, byte Qos)> _subs = new();
    private readonly HashSet<ushort> _inboundQos2 = new();

    private bool _v5;
    private bool _connected;
    private bool _graceful;
    private ushort _nextId = 1;
    private long _lastActivityTicks;
    private int _disposed;

    // Captured Last Will (published if the client disconnects ungracefully).
    private string? _willTopic;
    private byte[]? _willPayload;
    private byte _willQos;
    private bool _willRetain;
    private byte[]? _willProps;

    public BrokerConnection(MqttTestBroker broker, TcpClient client)
    {
        _broker = broker;
        _client = client;
        _stream = client.GetStream();
        ClientId = string.Empty;
    }

    public string ClientId { get; private set; }

    public void Close()
    {
        try { _client.Close(); } catch (SocketException) { /* best effort */ }
        catch (ObjectDisposedException) { /* already closed */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _writeLock.Dispose();
        _stream.Dispose();
        _client.Dispose();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Touch();
        try
        {
            var first = await ReadPacketAsync(ct).ConfigureAwait(false);
            if (first is null || (PacketType)(first.Value.First >> 4) != PacketType.Connect)
            {
                return;     // protocol violation: first packet must be CONNECT
            }
            if (!await HandleConnectAsync(first.Value.Body).ConfigureAwait(false)) return;

            _connected = true;
            _broker.OnConnected(this);
            using var watchdog = StartKeepAliveWatchdog(ct);

            while (!ct.IsCancellationRequested)
            {
                var packet = await ReadPacketAsync(ct).ConfigureAwait(false);
                if (packet is null) break;      // EOF
                Touch();
                var keepGoing = await DispatchAsync(packet.Value.First, packet.Value.Body)
                    .ConfigureAwait(false);
                if (!keepGoing) break;          // DISCONNECT or fatal
            }
        }
        catch (MqttBrokerProtocolException) { /* malformed: drop the connection */ }
        catch (IOException) { /* socket reset */ }
        catch (ObjectDisposedException) { /* stream closed */ }
        catch (OperationCanceledException) { /* broker shutting down */ }
        finally
        {
            if (!_graceful && _willTopic is not null)
            {
                var will = new Message
                {
                    Topic = _willTopic,
                    Payload = _willPayload ?? Array.Empty<byte>(),
                    Qos = _willQos,
                    Retain = _willRetain,
                    V5Properties = _willProps,
                };
                try { await _broker.RouteAsync(will).ConfigureAwait(false); } catch { /* ignore */ }
            }
            Close();
            _broker.Remove(this, _connected);
            Dispose();
        }
    }

    private void Touch() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

    private CancellationTokenSource StartKeepAliveWatchdog(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = WatchAsync(cts.Token);
        return cts;
    }

    private async Task WatchAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
                var last = new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
                if (DateTime.UtcNow - last > Deadline) { Close(); return; }
            }
        }
        catch (OperationCanceledException) { /* connection ended */ }
    }

    private TimeSpan Deadline
    {
        get
        {
            var max = _broker.Options.MaxKeepAlive;
            if (KeepAlive == 0) return max;
            var k = TimeSpan.FromSeconds(KeepAlive * 1.5);
            return k < max ? k : max;
        }
    }

    private ushort KeepAlive { get; set; }

    // ---- packet reading -------------------------------------------------------------------

    private readonly byte[] _one = new byte[1];

    private async Task<(byte First, byte[] Body)?> ReadPacketAsync(CancellationToken ct)
    {
        if (!await ReadExactAsync(_one, 1, ct).ConfigureAwait(false)) return null;
        var first = _one[0];

        var multiplier = 1;
        var length = 0;
        byte b;
        var count = 0;
        do
        {
            if (!await ReadExactAsync(_one, 1, ct).ConfigureAwait(false)) return null;
            b = _one[0];
            length += (b & 0x7F) * multiplier;
            multiplier *= 128;
            if (++count > 4) throw new MqttBrokerProtocolException("Remaining length too long.");
        }
        while ((b & 0x80) != 0);

        var body = length == 0 ? Array.Empty<byte>() : new byte[length];
        if (length > 0 &&
            !await ReadExactAsync(body, length, ct).ConfigureAwait(false))
        {
            return null;
        }
        return (first, body);
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, int count, CancellationToken ct)
    {
        var read = 0;
        while (read < count)
        {
            var n = await _stream.ReadAsync(buffer.AsMemory(read, count - read), ct)
                .ConfigureAwait(false);
            if (n == 0) return false;       // EOF
            read += n;
        }
        return true;
    }

    // ---- dispatch -------------------------------------------------------------------------

    private Task<bool> DispatchAsync(byte first, byte[] body)
    {
        var type = (PacketType)(first >> 4);
        return type switch
        {
            PacketType.Publish => HandlePublishAsync(first, body),
            PacketType.PubAck => True,
            PacketType.PubRec => HandlePubRecAsync(body),
            PacketType.PubRel => HandlePubRelAsync(body),
            PacketType.PubComp => True,
            PacketType.Subscribe => HandleSubscribeAsync(body),
            PacketType.Unsubscribe => HandleUnsubscribeAsync(body),
            PacketType.PingReq => HandlePingAsync(),
            PacketType.Disconnect => Disconnect(),
            _ => True,
        };
    }

    private static Task<bool> True => Task.FromResult(true);

    private Task<bool> Disconnect()
    {
        _graceful = true;
        return Task.FromResult(false);
    }

    private async Task<bool> HandleConnectAsync(byte[] body)
    {
        var r = new BufReader(body);
        _ = r.ReadString();                 // protocol name
        var level = r.ReadByte();           // protocol level
        _v5 = level >= ProtocolV5;
        var flags = r.ReadByte();
        KeepAlive = r.ReadUInt16();
        if (_v5) _ = r.ReadProperties();    // CONNECT properties (ignored)

        var clientId = r.ReadString();

        var hasWill = (flags & 0x04) != 0;
        if (hasWill)
        {
            if (_v5) _willProps = r.ReadProperties().ToArray();
            _willTopic = r.ReadString();
            _willPayload = r.ReadBinary().ToArray();
            _willQos = (byte)((flags >> 3) & 0x03);
            _willRetain = (flags & 0x20) != 0;
        }

        string? username = null;
        ReadOnlyMemory<byte> password = default;
        if ((flags & 0x80) != 0) username = r.ReadString();
        if ((flags & 0x40) != 0) password = r.ReadBinary().ToArray();

        ClientId = clientId.Length == 0 ? "auto-" + Guid.NewGuid().ToString("N") : clientId;

        if (!_broker.Authenticate(ClientId, username, password))
        {
            await SendConnAckAsync(_v5 ? (byte)0x87 : (byte)0x05).ConfigureAwait(false);
            return false;
        }
        await SendConnAckAsync(0x00).ConfigureAwait(false);
        return true;
    }

    private Task<bool> SendConnAckAsync(byte reasonOrReturnCode)
    {
        var pb = new PacketBuilder();
        pb.Byte(0x00);                  // acknowledge flags: session present = 0
        pb.Byte(reasonOrReturnCode);
        if (_v5) pb.VarInt(0);          // empty CONNACK properties
        var frame = PacketBuilder.Frame(PacketBuilder.FirstByte(PacketType.ConnAck), pb.Body);
        return WriteAsync(frame);
    }

    private async Task<bool> HandleSubscribeAsync(byte[] body)
    {
        var r = new BufReader(body);
        var packetId = r.ReadUInt16();
        if (_v5) _ = r.ReadProperties();

        var granted = new List<byte>();
        var added = new List<string>();
        while (!r.End)
        {
            var filter = r.ReadString();
            var options = r.ReadByte();
            var qos = (byte)(options & 0x03);
            if (!TopicFilter.IsValidFilter(filter) || qos > 2)
            {
                granted.Add(0x80);      // failure
                continue;
            }
            lock (_stateGate)
            {
                _subs.RemoveAll(s => string.Equals(s.Filter, filter, StringComparison.Ordinal));
                _subs.Add((filter, qos));
            }
            granted.Add(qos);
            added.Add(filter);
        }

        var pb = new PacketBuilder();
        pb.UInt16(packetId);
        if (_v5) pb.VarInt(0);
        foreach (var g in granted) pb.Byte(g);
        await WriteAsync(PacketBuilder.Frame(PacketBuilder.FirstByte(PacketType.SubAck), pb.Body))
            .ConfigureAwait(false);

        foreach (var filter in added)
        {
            foreach (var retained in _broker.RetainedMatching(filter))
            {
                await TryDeliverAsync(retained, retainedFlag: true).ConfigureAwait(false);
            }
        }
        return true;
    }

    private async Task<bool> HandleUnsubscribeAsync(byte[] body)
    {
        var r = new BufReader(body);
        var packetId = r.ReadUInt16();
        if (_v5) _ = r.ReadProperties();

        var reasons = new List<byte>();
        while (!r.End)
        {
            var filter = r.ReadString();
            lock (_stateGate)
            {
                _subs.RemoveAll(s => string.Equals(s.Filter, filter, StringComparison.Ordinal));
            }
            reasons.Add(0x00);
        }

        var pb = new PacketBuilder();
        pb.UInt16(packetId);
        if (_v5)
        {
            pb.VarInt(0);
            foreach (var rc in reasons) pb.Byte(rc);
        }
        await WriteAsync(PacketBuilder.Frame(PacketBuilder.FirstByte(PacketType.UnsubAck), pb.Body))
            .ConfigureAwait(false);
        return true;
    }

    private async Task<bool> HandlePublishAsync(byte first, byte[] body)
    {
        var qos = (byte)((first >> 1) & 0x03);
        var retain = (first & 0x01) != 0;
        var r = new BufReader(body);
        var topic = r.ReadString();
        ushort packetId = 0;
        if (qos > 0) packetId = r.ReadUInt16();
        byte[]? props = null;
        if (_v5) props = r.ReadProperties().ToArray();
        var payload = r.ReadRemaining().ToArray();

        var msg = new Message
        {
            Topic = topic, Payload = payload, Qos = qos, Retain = retain, V5Properties = props,
        };

        if (qos == 2)
        {
            bool firstTime;
            lock (_stateGate) firstTime = _inboundQos2.Add(packetId);
            if (firstTime) await _broker.RouteAsync(msg).ConfigureAwait(false);
            await SendAckAsync(PacketType.PubRec, packetId).ConfigureAwait(false);
            return true;
        }

        await _broker.RouteAsync(msg).ConfigureAwait(false);
        if (qos == 1) await SendAckAsync(PacketType.PubAck, packetId).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> HandlePubRelAsync(byte[] body)
    {
        var r = new BufReader(body);
        var packetId = r.ReadUInt16();
        lock (_stateGate) _inboundQos2.Remove(packetId);
        await SendAckAsync(PacketType.PubComp, packetId).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> HandlePubRecAsync(byte[] body)
    {
        var r = new BufReader(body);
        var packetId = r.ReadUInt16();
        // Our outbound QoS 2 delivery: respond to the subscriber's PUBREC with PUBREL.
        await SendAckAsync(PacketType.PubRel, packetId, flags: 0x02).ConfigureAwait(false);
        return true;
    }

    private Task<bool> SendAckAsync(PacketType type, ushort packetId, int flags = 0)
    {
        var pb = new PacketBuilder();
        pb.UInt16(packetId);            // success short-form (no reason/props) for v5 too
        return WriteAsync(PacketBuilder.Frame(PacketBuilder.FirstByte(type, flags), pb.Body));
    }

    private async Task<bool> HandlePingAsync()
    {
        var packet = PacketBuilder.Frame(PacketBuilder.FirstByte(PacketType.PingResp), default);
        await WriteAsync(packet).ConfigureAwait(false);
        return true;
    }

    /// <summary>Delivers a message to this client if it has a matching subscription.</summary>
    public async Task TryDeliverAsync(Message msg, bool retainedFlag)
    {
        byte maxQos = 0;
        var match = false;
        lock (_stateGate)
        {
            foreach (var (filter, qos) in _subs)
            {
                if (!TopicFilter.Matches(filter, msg.Topic)) continue;
                match = true;
                if (qos > maxQos) maxQos = qos;
            }
        }
        if (!match) return;

        var effQos = Math.Min(msg.Qos, maxQos);
        ushort packetId = 0;
        if (effQos > 0) packetId = NextId();

        var flags = (effQos << 1) | (retainedFlag ? 1 : 0);
        var pb = new PacketBuilder(msg.Payload.Length + msg.Topic.Length + 16);
        pb.Str(msg.Topic);
        if (effQos > 0) pb.UInt16(packetId);
        if (_v5)
        {
            if (msg.V5Properties is { Length: > 0 } p)
            {
                pb.VarInt((uint)p.Length);
                pb.Raw(p);
            }
            else
            {
                pb.VarInt(0);
            }
        }
        pb.Raw(msg.Payload);

        var packet = PacketBuilder.Frame(
            PacketBuilder.FirstByte(PacketType.Publish, flags), pb.Body);
        await WriteAsync(packet).ConfigureAwait(false);
    }

    private ushort NextId()
    {
        lock (_stateGate)
        {
            if (_nextId == 0) _nextId = 1;
            return _nextId++;
        }
    }

    private async Task<bool> WriteAsync(byte[] packet)
    {
        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { return false; }
        try
        {
            await _stream.WriteAsync(packet.AsMemory()).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);
            return true;
        }
        catch (IOException) { Close(); return false; }
        catch (ObjectDisposedException) { return false; }
        finally
        {
            try { _writeLock.Release(); }
            catch (ObjectDisposedException) { /* disposed during shutdown */ }
        }
    }
}
