// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    /// Sets how the client reacts when an outbound PUBLISH would violate a broker-advertised limit
    /// (Maximum QoS / Retain Available). Default is <see cref="MqttBrokerLimitBehavior.Reject"/>.
    /// </summary>
    public MqttClientBuilder WithBrokerLimitBehavior(MqttBrokerLimitBehavior behavior)
    {
        _options.BrokerLimitBehavior = behavior;
        return this;
    }

    /// <summary>
    /// Sets how the client reacts when in-flight QoS&gt;0 publishes reach the broker's advertised
    /// Receive Maximum. Default is <see cref="MqttReceiveMaximumBehavior.Backpressure"/>.
    /// </summary>
    public MqttClientBuilder WithReceiveMaximumBehavior(MqttReceiveMaximumBehavior behavior)
    {
        _options.ReceiveMaximumBehavior = behavior;
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

    /// <summary>
    /// Configures an async credentials provider consulted on every connect (initial and each
    /// reconnect), so freshly-loaded username/password — e.g. a rotated SAS token or refreshed JWT
    /// used as the password — are presented each time. Overrides any static
    /// <see cref="WithCredentials(string, string)"/> values.
    /// </summary>
    public MqttClientBuilder WithCredentialsProvider(IMqttCredentialsProvider provider)
    {
        _options.CredentialsProvider = provider
            ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    /// <summary>
    /// Convenience overload of <see cref="WithCredentialsProvider(IMqttCredentialsProvider)"/> that
    /// adapts a delegate.
    /// </summary>
    public MqttClientBuilder WithCredentialsProvider(
        Func<CancellationToken, ValueTask<MqttCredentials>> load)
    {
        if (load is null) throw new ArgumentNullException(nameof(load));
        _options.CredentialsProvider = new DelegateCredentialsProvider(load);
        return this;
    }

    /// <summary>
    /// Authenticates with a Kubernetes service-account token (SAT) read from a mounted file and
    /// reconnects automatically when the token is rotated. The token is presented as the MQTT
    /// password (with an optional fixed <paramref name="username"/>). The provider is disposed with
    /// the client.
    /// </summary>
    /// <param name="tokenPath">
    /// Token file path; defaults to
    /// <see cref="KubernetesServiceAccountTokenCredentialsProvider.DefaultTokenPath"/>.
    /// </param>
    /// <param name="username">Optional fixed MQTT username.</param>
    /// <param name="pollInterval">
    /// How often to re-check the token file for rotation; defaults to 5 minutes.
    /// </param>
    public MqttClientBuilder WithKubernetesServiceAccountToken(
        string? tokenPath = null,
        string? username = null,
        TimeSpan? pollInterval = null)
    {
        _options.CredentialsProvider = new KubernetesServiceAccountTokenCredentialsProvider(
            tokenPath, username, pollInterval);
        return this;
    }

    public MqttClientBuilder Configure(Action<MqttClientOptions> configure)
    {
        configure?.Invoke(_options);
        return this;
    }

    public MqttClient Build() => new(_options, _loggerFactory, _persistence);
}
