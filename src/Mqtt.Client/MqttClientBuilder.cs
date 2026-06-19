// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
namespace Mqtt.Client;

/// <summary>
/// Fluent builder for <see cref="MqttClient"/>.
/// </summary>
public sealed class MqttClientBuilder
{
    private readonly MqttClientOptions _options = new();
    private ILoggerFactory? _loggerFactory;
    private IPersistentSessionStore? _persistence;

    /// <summary>
    /// Sets endpoint from a URI like <c>mqtt://host:1883</c>, <c>mqtts://host:8883</c>,
    /// <c>ws://...</c>, <c>wss://...</c>.
    /// </summary>
    public MqttClientBuilder ConnectTo(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("URI is required.", nameof(uri));
        }
        var u = new Uri(uri);
        _options.Host = u.Host;
        _options.Transport = u.Scheme.ToLowerInvariant() switch
        {
            "mqtt" or "tcp" => MqttTransportType.Tcp,
            "mqtts" or "ssl" or "tls" => MqttTransportType.Tls,
            "ws" => MqttTransportType.WebSocket,
            "wss" => MqttTransportType.WebSocketSecure,
            _ => throw new ArgumentException($"Unknown MQTT scheme '{u.Scheme}'.", nameof(uri)),
        };
        _options.Port = u.IsDefaultPort ? _options.Transport switch
        {
            MqttTransportType.Tcp => 1883,
            MqttTransportType.Tls => 8883,
            MqttTransportType.WebSocket => 80,
            MqttTransportType.WebSocketSecure => 443,
            _ => 1883,
        } : u.Port;
        return this;
    }

    public MqttClientBuilder WithClientId(string clientId)
    {
        _options.ClientId = clientId ?? string.Empty;
        return this;
    }

    public MqttClientBuilder WithCredentials(string username, string password)
    {
        _options.Username = username;
        _options.Password = Encoding.UTF8.GetBytes(password);
        return this;
    }

    public MqttClientBuilder WithCredentials(string username, byte[] password)
    {
        _options.Username = username;
        _options.Password = password;
        return this;
    }

    public MqttClientBuilder WithProtocol(MqttProtocolVersion version)
    {
        _options.ProtocolVersion = version;
        return this;
    }

    public MqttClientBuilder WithKeepAlive(ushort seconds)
    {
        _options.KeepAliveSeconds = seconds;
        return this;
    }

    public MqttClientBuilder WithCleanStart(bool cleanStart)
    {
        _options.CleanStart = cleanStart;
        return this;
    }

    public MqttClientBuilder WithReconnect(MqttReconnectPolicy? policy)
    {
        _options.Reconnect = policy;
        return this;
    }

    public MqttClientBuilder WithLastWill(MqttLastWill will)
    {
        _options.Will = will;
        return this;
    }

    public MqttClientBuilder WithClientCertificate(X509Certificate2 certificate)
    {
        _options.Transport = MqttTransportType.Tls;
        _options.Tls ??= new SslClientAuthenticationOptions { TargetHost = _options.Host };
        _options.Tls.ClientCertificates = new X509CertificateCollection { certificate };
        return this;
    }

    public MqttClientBuilder WithTls(Action<SslClientAuthenticationOptions> configure)
    {
        _options.Tls ??= new SslClientAuthenticationOptions { TargetHost = _options.Host };
        configure?.Invoke(_options.Tls);
        return this;
    }

    /// <summary>
    /// Routes the broker connection through a SOCKS5 proxy (RFC 1928). Supported for TCP and TLS
    /// transports. Supply <paramref name="username"/>/<paramref name="password"/> for RFC 1929
    /// username/password authentication.
    /// </summary>
    public MqttClientBuilder WithSocks5Proxy(
        string host,
        int port = 1080,
        string? username = null,
        string? password = null)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Proxy host is required.", nameof(host));
        }
        _options.Proxy = new Socks5ProxyOptions
        {
            Host = host,
            Port = port,
            Username = username,
            Password = password,
        };
        return this;
    }

    /// <summary>
    /// Routes the broker connection through a SOCKS5 proxy using the supplied options.
    /// </summary>
    public MqttClientBuilder WithSocks5Proxy(Socks5ProxyOptions options)
    {
        _options.Proxy = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    public MqttClientBuilder WithLogging(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        return this;
    }

    public MqttClientBuilder WithPersistence(IPersistentSessionStore store)
    {
        _persistence = store;
        return this;
    }

    /// <summary>
    /// Configures an MQTT 5 enhanced authentication handler. Driven on CONNECT and re-auth.
    /// </summary>
    public MqttClientBuilder WithAuthentication(IMqttAuthenticationHandler handler)
    {
        _options.AuthenticationHandler = handler
            ?? throw new ArgumentNullException(nameof(handler));
        return this;
    }

    public MqttClientBuilder Configure(Action<MqttClientOptions> configure)
    {
        configure?.Invoke(_options);
        return this;
    }

    public MqttClient Build() => new(_options, _loggerFactory, _persistence);
}
