// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client;

/// <summary>
/// Connects to a SOCKS5 proxy (RFC 1928) and tunnels to the target host/port, optionally
/// performing username/password authentication (RFC 1929).
/// </summary>
internal sealed class Socks5SocketConnector : ISocketConnector
{
    private const byte Version = 0x05;
    private const byte AuthVersion = 0x01;
    private const byte MethodNoAuth = 0x00;
    private const byte MethodUserPass = 0x02;
    private const byte MethodNone = 0xFF;
    private const byte CmdConnect = 0x01;
    private const byte Reserved = 0x00;
    private const byte AtypIpv4 = 0x01;
    private const byte AtypDomain = 0x03;
    private const byte AtypIpv6 = 0x04;
    private const byte ReplySucceeded = 0x00;

    // Method-selection greetings live in the assembly data section (no per-connect allocation):
    // VER, NMETHODS, METHODS...
    private static ReadOnlySpan<byte> GreetingNoAuth => new byte[] { Version, 1, MethodNoAuth };
    private static ReadOnlySpan<byte> GreetingUserPass
        => new byte[] { Version, 2, MethodNoAuth, MethodUserPass };

    private readonly Socks5ProxyOptions _options;

    public Socks5SocketConnector(Socks5ProxyOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<Socket> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        Socket socket;
        try
        {
            socket = await SocketConnect.ConnectAsync(
                _options.Host,
                _options.Port,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new Socks5ProxyException(
                $"Failed to connect to SOCKS5 proxy {_options.Host}:{_options.Port}.", ex);
        }

        try
        {
            await HandshakeAsync(socket, host, port, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async ValueTask HandshakeAsync(
        Socket socket,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        // ownsSocket:false — the handshake borrows the socket; the transport wraps it afterwards.
        var stream = new NetworkStream(socket, ownsSocket: false);
        var scratch = ArrayPool<byte>.Shared.Rent(512);
        try
        {
            await NegotiateMethodAsync(stream, scratch, cancellationToken).ConfigureAwait(false);
            await RequestConnectAsync(stream, scratch, host, port, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask NegotiateMethodAsync(
        Stream stream,
        byte[] scratch,
        CancellationToken cancellationToken)
    {
        var greetingLength = CopyGreeting(scratch);
        await WriteAsync(stream, scratch.AsMemory(0, greetingLength), cancellationToken)
            .ConfigureAwait(false);

        await ReadExactlyAsync(stream, scratch.AsMemory(0, 2), cancellationToken)
            .ConfigureAwait(false);
        if (scratch[0] != Version)
        {
            throw new Socks5ProxyException(
                $"Proxy replied with unexpected SOCKS version 0x{scratch[0]:x2}.");
        }

        var method = scratch[1];
        switch (method)
        {
            case MethodNoAuth:
                return;
            case MethodUserPass:
                await AuthenticateAsync(stream, scratch, cancellationToken).ConfigureAwait(false);
                return;
            case MethodNone:
                throw new Socks5ProxyException(
                    "Proxy rejected all offered authentication methods.");
            default:
                throw new Socks5ProxyException(
                    $"Proxy selected unsupported authentication method 0x{method:x2}.");
        }
    }

    private int CopyGreeting(byte[] scratch)
    {
        var greeting = string.IsNullOrEmpty(_options.Username) ? GreetingNoAuth : GreetingUserPass;
        greeting.CopyTo(scratch);
        return greeting.Length;
    }

    private async ValueTask AuthenticateAsync(
        Stream stream,
        byte[] scratch,
        CancellationToken cancellationToken)
    {
        var user = Encoding.UTF8.GetBytes(_options.Username ?? string.Empty);
        var pass = Encoding.UTF8.GetBytes(_options.Password ?? string.Empty);
        if (user.Length is 0 or > 255)
        {
            throw new Socks5ProxyException("SOCKS5 username must be 1..255 bytes.");
        }
        if (pass.Length > 255)
        {
            throw new Socks5ProxyException("SOCKS5 password must be at most 255 bytes.");
        }

        var msg = new byte[3 + user.Length + pass.Length];
        msg[0] = AuthVersion;
        msg[1] = (byte)user.Length;
        user.CopyTo(msg.AsSpan(2));
        msg[2 + user.Length] = (byte)pass.Length;
        pass.CopyTo(msg.AsSpan(3 + user.Length));
        await WriteAsync(stream, msg, cancellationToken).ConfigureAwait(false);

        await ReadExactlyAsync(stream, scratch.AsMemory(0, 2), cancellationToken)
            .ConfigureAwait(false);
        if (scratch[0] != AuthVersion || scratch[1] != 0x00)
        {
            throw new Socks5ProxyException(
                "SOCKS5 username/password authentication was rejected by the proxy.");
        }
    }

    private async ValueTask RequestConnectAsync(
        Stream stream,
        byte[] scratch,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var (atyp, addr) = await ResolveTargetAsync(host, cancellationToken).ConfigureAwait(false);

        var request = new byte[4 + addr.Length + 2];
        request[0] = Version;
        request[1] = CmdConnect;
        request[2] = Reserved;
        request[3] = atyp;
        addr.CopyTo(request.AsSpan(4));
        request[4 + addr.Length] = (byte)(port >> 8);
        request[5 + addr.Length] = (byte)port;
        await WriteAsync(stream, request, cancellationToken).ConfigureAwait(false);

        await ReadExactlyAsync(stream, scratch.AsMemory(0, 4), cancellationToken)
            .ConfigureAwait(false);
        if (scratch[0] != Version)
        {
            throw new Socks5ProxyException(
                $"Proxy CONNECT reply has unexpected SOCKS version 0x{scratch[0]:x2}.");
        }

        var reply = scratch[1];
        if (reply != ReplySucceeded)
        {
            throw new Socks5ProxyException(
                $"Proxy refused CONNECT to {host}:{port} — {DescribeReply(reply)} (0x{reply:x2}).");
        }

        // Consume the bound address + port so the socket is positioned at the broker byte stream.
        var replyAtyp = scratch[3];
        int boundLength;
        switch (replyAtyp)
        {
            case AtypIpv4:
                boundLength = 4;
                break;
            case AtypIpv6:
                boundLength = 16;
                break;
            case AtypDomain:
                await ReadExactlyAsync(stream, scratch.AsMemory(0, 1), cancellationToken)
                    .ConfigureAwait(false);
                boundLength = scratch[0];
                break;
            default:
                throw new Socks5ProxyException(
                    $"Proxy CONNECT reply has unsupported address type 0x{replyAtyp:x2}.");
        }

        await ReadExactlyAsync(stream, scratch.AsMemory(0, boundLength + 2), cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<(byte Atyp, byte[] Address)> ResolveTargetAsync(
        string host,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IPAddress.TryParse(host, out var literal))
        {
            return literal.AddressFamily switch
            {
                AddressFamily.InterNetwork => (AtypIpv4, literal.GetAddressBytes()),
                AddressFamily.InterNetworkV6 => (AtypIpv6, literal.GetAddressBytes()),
                _ => throw new Socks5ProxyException(
                    $"Unsupported target address family {literal.AddressFamily}."),
            };
        }

        if (_options.ResolveHostnamesRemotely)
        {
            var domain = Encoding.UTF8.GetBytes(host);
            if (domain.Length is 0 or > 255)
            {
                throw new Socks5ProxyException(
                    "Target host name must be 1..255 bytes for SOCKS5 remote resolution.");
            }
            var addr = new byte[1 + domain.Length];
            addr[0] = (byte)domain.Length;
            domain.CopyTo(addr.AsSpan(1));
            return (AtypDomain, addr);
        }

#if NETSTANDARD2_1
        var resolved = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
#else
        var resolved = await Dns.GetHostAddressesAsync(host, cancellationToken)
            .ConfigureAwait(false);
#endif
        foreach (var a in resolved)
        {
            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                return (AtypIpv4, a.GetAddressBytes());
            }
        }
        foreach (var a in resolved)
        {
            if (a.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return (AtypIpv6, a.GetAddressBytes());
            }
        }
        throw new Socks5ProxyException($"Could not resolve target host '{host}' to an IP address.");
    }

    private static string DescribeReply(byte reply) => reply switch
    {
        0x01 => "general SOCKS server failure",
        0x02 => "connection not allowed by ruleset",
        0x03 => "network unreachable",
        0x04 => "host unreachable",
        0x05 => "connection refused",
        0x06 => "TTL expired",
        0x07 => "command not supported",
        0x08 => "address type not supported",
        _ => "unknown failure",
    };

    private static async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new Socks5ProxyException(
                    "Proxy closed the connection during the SOCKS5 handshake.");
            }
            total += read;
        }
    }
}
