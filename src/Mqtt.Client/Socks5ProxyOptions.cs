// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;

namespace Mqtt.Client;

/// <summary>
/// Configuration for connecting to the broker through a SOCKS5 proxy (RFC 1928), with optional
/// username/password authentication (RFC 1929). Applies to the <see cref="MqttTransportType.Tcp"/>
/// and <see cref="MqttTransportType.Tls"/> transports; WebSocket transports do not support SOCKS5.
/// </summary>
public sealed class Socks5ProxyOptions
{
    /// <summary>
    /// SOCKS5 proxy host name or IP address.
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// SOCKS5 proxy port. Defaults to the IANA-registered SOCKS port 1080.
    /// </summary>
    public int Port { get; set; } = 1080;

    /// <summary>
    /// Optional username for RFC 1929 username/password authentication. When set together with
    /// <see cref="Password"/>, the client advertises the username/password method (0x02) in
    /// addition to "no authentication required" (0x00).
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Optional password for RFC 1929 username/password authentication. Sent in cleartext per the
    /// RFC; prefer a proxy on a trusted network and/or TLS to the broker.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// When <c>true</c> (the default), the broker host name is sent to the proxy for resolution
    /// (SOCKS5 address type <c>DOMAINNAME</c>), avoiding local DNS lookups and DNS leaks. When
    /// <c>false</c>, the host name is resolved locally and an IP address is sent to the proxy.
    /// </summary>
    public bool ResolveHostnamesRemotely { get; set; } = true;
}
