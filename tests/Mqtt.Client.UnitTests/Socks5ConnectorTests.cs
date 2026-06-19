// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Exercises <see cref="Socks5SocketConnector"/> against an in-process fake SOCKS5 proxy
/// (loopback <see cref="TcpListener"/>) that speaks RFC 1928 / RFC 1929.
/// </summary>
public class Socks5ConnectorTests
{
    private static Socks5SocketConnector Connector(
        FakeSocks5Server server,
        string? username = null,
        string? password = null,
        bool remoteDns = true)
        => new(new Socks5ProxyOptions
        {
            Host = server.Host,
            Port = server.Port,
            Username = username,
            Password = password,
            ResolveHostnamesRemotely = remoteDns,
        });

    private static async Task<byte[]> ReadTrailerAsync(
        Socket socket,
        int count,
        CancellationToken ct)
    {
        var buf = new byte[count];
        var total = 0;
        while (total < count)
        {
            var n = await socket.ReceiveAsync(buf.AsMemory(total), SocketFlags.None, ct);
            if (n == 0) break;
            total += n;
        }
        return buf;
    }

    [Test]
    [Timeout(5_000)]
    public async Task NoAuth_remote_dns_sends_domain_connect_and_tunnels(CancellationToken ct)
    {
        await using var server = new FakeSocks5Server();
        var serverTask = server.RunAsync(ct);

        using var socket = await Connector(server).ConnectAsync("broker.example.com", 1883, ct);
        var trailer = await ReadTrailerAsync(socket, 2, ct);
        await serverTask;

        await Assert.That(server.OfferedMethods).Contains((byte)0x00);
        await Assert.That(server.RequestAtyp).IsEqualTo((byte)0x03);
        await Assert.That(server.RequestHost).IsEqualTo("broker.example.com");
        await Assert.That(server.RequestPort).IsEqualTo(1883);
        await Assert.That(trailer[0]).IsEqualTo((byte)0xAB);
        await Assert.That(trailer[1]).IsEqualTo((byte)0xCD);
    }

    [Test]
    [Timeout(5_000)]
    public async Task UsernamePassword_offers_and_authenticates(CancellationToken ct)
    {
        await using var server = new FakeSocks5Server { SelectedMethod = 0x02 };
        var serverTask = server.RunAsync(ct);

        using var socket = await Connector(server, "alice", "s3cret")
            .ConnectAsync("broker.example.com", 8883, ct);
        await ReadTrailerAsync(socket, 2, ct);
        await serverTask;

        await Assert.That(server.OfferedMethods).Contains((byte)0x02);
        await Assert.That(server.Username).IsEqualTo("alice");
        await Assert.That(server.Password).IsEqualTo("s3cret");
        await Assert.That(server.RequestPort).IsEqualTo(8883);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Authentication_rejected_throws(CancellationToken ct)
    {
        await using var server = new FakeSocks5Server
        {
            SelectedMethod = 0x02,
            AuthSucceeds = false,
        };
        var serverTask = server.RunAsync(ct);

        await Assert.ThrowsAsync<Socks5ProxyException>(
            async () => await Connector(server, "alice", "wrong")
                .ConnectAsync("broker.example.com", 1883, ct));
        await serverTask;
    }

    [Test]
    [Timeout(5_000)]
    public async Task No_acceptable_methods_throws(CancellationToken ct)
    {
        await using var server = new FakeSocks5Server { SelectedMethod = 0xFF };
        var serverTask = server.RunAsync(ct);

        await Assert.ThrowsAsync<Socks5ProxyException>(
            async () => await Connector(server).ConnectAsync("broker.example.com", 1883, ct));
        await serverTask;
    }

    [Test]
    [Timeout(5_000)]
    public async Task Connect_refused_reply_throws(CancellationToken ct)
    {
        await using var server = new FakeSocks5Server { ReplyCode = 0x05 };
        var serverTask = server.RunAsync(ct);

        var ex = await Assert.ThrowsAsync<Socks5ProxyException>(
            async () => await Connector(server).ConnectAsync("broker.example.com", 1883, ct));
        await serverTask;

        await Assert.That(ex!.Message).Contains("connection refused");
    }

    [Test]
    [Timeout(5_000)]
    public async Task Ipv4_literal_sends_atyp_ipv4(CancellationToken ct)
    {
        await using var server = new FakeSocks5Server();
        var serverTask = server.RunAsync(ct);

        using var socket = await Connector(server).ConnectAsync("93.184.216.34", 1883, ct);
        await ReadTrailerAsync(socket, 2, ct);
        await serverTask;

        await Assert.That(server.RequestAtyp).IsEqualTo((byte)0x01);
        await Assert.That(server.RequestHost).IsEqualTo("93.184.216.34");
    }

    [Test]
    [Timeout(5_000)]
    public async Task Ipv6_literal_sends_atyp_ipv6(CancellationToken ct)
    {
        await using var server = new FakeSocks5Server();
        var serverTask = server.RunAsync(ct);

        using var socket = await Connector(server).ConnectAsync("::1", 1883, ct);
        await ReadTrailerAsync(socket, 2, ct);
        await serverTask;

        await Assert.That(server.RequestAtyp).IsEqualTo((byte)0x04);
        await Assert.That(IPAddress.Parse(server.RequestHost!)).IsEqualTo(IPAddress.IPv6Loopback);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Local_dns_resolution_sends_ip_address(CancellationToken ct)
    {
        await using var server = new FakeSocks5Server();
        var serverTask = server.RunAsync(ct);

        using var socket = await Connector(server, remoteDns: false)
            .ConnectAsync("localhost", 1883, ct);
        await ReadTrailerAsync(socket, 2, ct);
        await serverTask;

        await Assert.That(server.RequestAtyp).IsNotEqualTo((byte)0x03);
        await Assert.That(IPAddress.IsLoopback(IPAddress.Parse(server.RequestHost!))).IsTrue();
    }

    [Test]
    [Timeout(5_000)]
    public async Task Domain_bound_reply_is_drained(CancellationToken ct)
    {
        await using var server = new FakeSocks5Server { ReplyBoundAtyp = 0x03 };
        var serverTask = server.RunAsync(ct);

        using var socket = await Connector(server).ConnectAsync("broker.example.com", 1883, ct);
        var trailer = await ReadTrailerAsync(socket, 2, ct);
        await serverTask;

        await Assert.That(trailer[0]).IsEqualTo((byte)0xAB);
        await Assert.That(trailer[1]).IsEqualTo((byte)0xCD);
    }

    [Test]
    [Timeout(5_000)]
    public async Task Proxy_unreachable_throws_socks_exception(CancellationToken ct)
    {
        // Port 1 on loopback is not listening: connect fails fast.
        var connector = new Socks5SocketConnector(new Socks5ProxyOptions
        {
            Host = "127.0.0.1",
            Port = 1,
        });
        await Assert.ThrowsAsync<Socks5ProxyException>(
            async () => await connector.ConnectAsync("broker.example.com", 1883, ct));
    }
}

/// <summary>
/// Minimal scripted SOCKS5 proxy on loopback. Captures the client's greeting/auth/CONNECT and
/// replies according to the configured properties; on success it emits a 2-byte trailer so tests
/// can verify the tunnel carries post-handshake bytes intact.
/// </summary>
internal sealed class FakeSocks5Server : IAsyncDisposable
{
    private readonly TcpListener _listener;

    public FakeSocks5Server()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
    }

    public string Host { get; } = "127.0.0.1";
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public byte SelectedMethod { get; init; }
    public bool AuthSucceeds { get; init; } = true;
    public byte ReplyCode { get; init; }
    public byte ReplyBoundAtyp { get; init; } = 0x01;

    public byte[] OfferedMethods { get; private set; } = System.Array.Empty<byte>();
    public string? Username { get; private set; }
    public string? Password { get; private set; }
    public byte RequestAtyp { get; private set; }
    public string? RequestHost { get; private set; }
    public int RequestPort { get; private set; }

    public Task RunAsync(CancellationToken ct) => Task.Run(() => ServeAsync(ct), ct);

    private async Task ServeAsync(CancellationToken ct)
    {
        using var client = await _listener.AcceptTcpClientAsync(ct);
        using var stream = client.GetStream();

        // Greeting: VER, NMETHODS, METHODS...
        var head = await ReadExactlyAsync(stream, 2, ct);
        OfferedMethods = await ReadExactlyAsync(stream, head[1], ct);
        await stream.WriteAsync(new byte[] { 0x05, SelectedMethod }, ct);

        if (SelectedMethod == 0x02)
        {
            await ReadExactlyAsync(stream, 1, ct); // auth version
            var ulen = (await ReadExactlyAsync(stream, 1, ct))[0];
            Username = Encoding.UTF8.GetString(await ReadExactlyAsync(stream, ulen, ct));
            var plen = (await ReadExactlyAsync(stream, 1, ct))[0];
            Password = Encoding.UTF8.GetString(await ReadExactlyAsync(stream, plen, ct));
            var status = AuthSucceeds ? (byte)0x00 : (byte)0x01;
            await stream.WriteAsync(new byte[] { 0x01, status }, ct);
            if (!AuthSucceeds)
            {
                return;
            }
        }
        else if (SelectedMethod != 0x00)
        {
            return; // no acceptable methods — client aborts
        }

        // CONNECT request: VER, CMD, RSV, ATYP, ADDR, PORT
        var req = await ReadExactlyAsync(stream, 4, ct);
        RequestAtyp = req[3];
        RequestHost = RequestAtyp switch
        {
            0x01 => new IPAddress(await ReadExactlyAsync(stream, 4, ct)).ToString(),
            0x04 => new IPAddress(await ReadExactlyAsync(stream, 16, ct)).ToString(),
            0x03 => await ReadDomainAsync(stream, ct),
            _ => null,
        };
        var port = await ReadExactlyAsync(stream, 2, ct);
        RequestPort = (port[0] << 8) | port[1];

        await stream.WriteAsync(BuildReply(), ct);
        if (ReplyCode == 0x00)
        {
            await stream.WriteAsync(new byte[] { 0xAB, 0xCD }, ct);
        }
    }

    private byte[] BuildReply()
    {
        byte[] bound = ReplyBoundAtyp switch
        {
            0x01 => new byte[] { 0, 0, 0, 0 },
            0x04 => new byte[16],
            0x03 => new byte[] { 1, (byte)'x' },
            _ => new byte[] { 0, 0, 0, 0 },
        };
        var reply = new byte[4 + bound.Length + 2];
        reply[0] = 0x05;
        reply[1] = ReplyCode;
        reply[2] = 0x00;
        reply[3] = ReplyBoundAtyp;
        bound.CopyTo(reply.AsSpan(4));
        return reply;
    }

    private static async Task<string> ReadDomainAsync(NetworkStream stream, CancellationToken ct)
    {
        var len = (await ReadExactlyAsync(stream, 1, ct))[0];
        return Encoding.UTF8.GetString(await ReadExactlyAsync(stream, len, ct));
    }

    private static async Task<byte[]> ReadExactlyAsync(
        NetworkStream stream,
        int count,
        CancellationToken ct)
    {
        var buf = new byte[count];
        var total = 0;
        while (total < count)
        {
            var n = await stream.ReadAsync(buf.AsMemory(total), ct);
            if (n == 0)
            {
                throw new System.IO.EndOfStreamException("Client closed during handshake.");
            }
            total += n;
        }
        return buf;
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return default;
    }
}
