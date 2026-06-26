// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client.Testing;

/// <summary>
/// Decides whether a CONNECT is accepted. Return <c>true</c> to accept, <c>false</c> to reject
/// with "Bad user name or password".
/// </summary>
/// <param name="clientId">The client identifier from the CONNECT packet.</param>
/// <param name="username">The user name, or <c>null</c> when none was supplied.</param>
/// <param name="password">The password bytes, or <c>null</c> when none was supplied.</param>
public delegate bool MqttTestBrokerAuthenticator(
    string clientId, string? username, ReadOnlyMemory<byte> password);

/// <summary>
/// Configuration for a <see cref="MqttTestBroker"/>.
/// </summary>
public sealed class MqttTestBrokerOptions
{
    /// <summary>
    /// TCP port to listen on. <c>0</c> (the default) binds an ephemeral port chosen by the OS;
    /// read the actual port back from <see cref="MqttTestBroker.Port"/>.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// When <c>true</c> (the default) any CONNECT is accepted. Set <c>false</c> and supply
    /// <see cref="Authenticate"/> to require credentials.
    /// </summary>
    public bool AllowAnonymous { get; set; } = true;

    /// <summary>
    /// Optional credential check. When set it overrides <see cref="AllowAnonymous"/>.
    /// </summary>
    public MqttTestBrokerAuthenticator? Authenticate { get; set; }

    /// <summary>
    /// Maximum time a connection may be idle past its negotiated keep-alive before the broker
    /// drops it. The effective deadline is <c>keepAlive * 1.5</c> per [MQTT-3.1.2-22]; this caps
    /// the wait when a client requests keep-alive 0 (disabled). Default: 5 minutes.
    /// </summary>
    public TimeSpan MaxKeepAlive { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Information about a connected client, surfaced via <see cref="MqttTestBroker.ClientConnected"/>
/// and <see cref="MqttTestBroker.ClientDisconnected"/>.
/// </summary>
/// <param name="ClientId">The client identifier.</param>
public readonly record struct MqttTestBrokerClient(string ClientId);
