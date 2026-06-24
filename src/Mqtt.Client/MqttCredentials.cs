// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client;

/// <summary>
/// A username/password credential pair presented in the MQTT CONNECT packet.
/// </summary>
public readonly struct MqttCredentials
{
    /// <summary>
    /// Creates a credential pair. Either value may be <c>null</c> (e.g. anonymous, or a
    /// password-only / username-only broker configuration).
    /// </summary>
    public MqttCredentials(string? username, byte[]? password)
    {
        Username = username;
        Password = password;
    }

    /// <summary>
    /// MQTT username, or <c>null</c> for none.
    /// </summary>
    public string? Username { get; }

    /// <summary>
    /// MQTT password bytes, or <c>null</c> for none.
    /// </summary>
    public byte[]? Password { get; }

    /// <summary>
    /// Creates a credential pair from a username and a UTF-8 string password (mirrors
    /// <see cref="MqttClientBuilder.WithCredentials(string, string)"/>).
    /// </summary>
    public static MqttCredentials From(string username, string password)
        => new(username, Encoding.UTF8.GetBytes(password));
}

/// <summary>
/// Supplies MQTT username/password credentials asynchronously, consulted once per (re)connect so
/// that freshly-loaded credentials can be presented each time the client opens a connection.
/// <para>
/// This is the basic-authentication counterpart to <see cref="IMqttAuthenticationHandler"/> (MQTT 5
/// enhanced authentication): use it to supply a rotated shared-access-signature token, a
/// short-lived JWT/OAuth bearer carried as the MQTT password, or any credential that may have
/// changed since the previous connection. The provider is invoked on the initial connect and again
/// on every automatic reconnect, so the value it returns is always re-read rather than captured
/// once at construction time.
/// </para>
/// <para>
/// When a provider is configured it is the source of truth for the username/password on each
/// connect; the static <see cref="MqttClientOptions.Username"/> / <see cref="MqttClientOptions.Password"/>
/// are ignored. TLS client-certificate refresh is a separate concern and is not covered here.
/// </para>
/// </summary>
public interface IMqttCredentialsProvider
{
    /// <summary>
    /// Produces the credentials to present in the next CONNECT. Called on the connecting thread
    /// before the CONNECT packet is built, on both the initial connect and every reconnect. If it
    /// throws, the connect attempt fails; the auto-reconnect loop then retries with backoff.
    /// </summary>
    ValueTask<MqttCredentials> GetCredentialsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Adapts a delegate to <see cref="IMqttCredentialsProvider"/>.
/// </summary>
internal sealed class DelegateCredentialsProvider : IMqttCredentialsProvider
{
    private readonly Func<CancellationToken, ValueTask<MqttCredentials>> _load;

    public DelegateCredentialsProvider(Func<CancellationToken, ValueTask<MqttCredentials>> load)
        => _load = load ?? throw new ArgumentNullException(nameof(load));

    public ValueTask<MqttCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
        => _load(cancellationToken);
}
