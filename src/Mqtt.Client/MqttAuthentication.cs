// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Mqtt.Client.Protocol;

namespace Mqtt.Client;

/// <summary>
/// MQTT 5 enhanced authentication handler. Implementations carry their own state across the
/// multi-round-trip exchange. The same instance is consulted on initial CONNECT (with a null
/// challenge) and once per inbound AUTH from the broker.
/// </summary>
public interface IMqttAuthenticationHandler
{
    /// <summary>SASL-style method name, e.g. <c>"SCRAM-SHA-256"</c>, <c>"OAUTH-BEARER"</c>.</summary>
    string Method { get; }

    /// <summary>
    /// Produce the next client-side authentication payload. Called once with
    /// <c>challenge</c>=null at the very start, then once per inbound AUTH
    /// <c>0x18 (Continue)</c> from the broker.
    /// </summary>
    ValueTask<MqttAuthenticationResult> ContinueAsync(ReadOnlyMemory<byte>? challenge, CancellationToken cancellationToken);
}

/// <summary>Result of an <see cref="IMqttAuthenticationHandler.ContinueAsync"/> call.</summary>
public readonly struct MqttAuthenticationResult
{
    private MqttAuthenticationResult(
        MqttAuthenticationResultKind kind,
        ReadOnlyMemory<byte> data,
        MqttReasonCode reasonCode,
        string? reasonString)
    {
        Kind = kind;
        Data = data;
        ReasonCode = reasonCode;
        ReasonString = reasonString;
    }

    public MqttAuthenticationResultKind Kind { get; }
    public ReadOnlyMemory<byte> Data { get; }
    public MqttReasonCode ReasonCode { get; }
    public string? ReasonString { get; }

    /// <summary>Send <paramref name="data"/> in AUTH 0x18 (Continue) and expect another inbound AUTH.</summary>
    public static MqttAuthenticationResult Continue(ReadOnlyMemory<byte> data)
        => new(MqttAuthenticationResultKind.Continue, data, MqttReasonCode.ContinueAuthentication, null);

    /// <summary>Send <paramref name="data"/> as the final client message. The broker is expected to
    /// respond with CONNACK 0x00 (connect path) or AUTH 0x00 (re-auth path).</summary>
    public static MqttAuthenticationResult Final(ReadOnlyMemory<byte> data = default)
        => new(MqttAuthenticationResultKind.Final, data, MqttReasonCode.Success, null);

    /// <summary>Abort the handshake with the given reason. The client sends DISCONNECT and throws.</summary>
    public static MqttAuthenticationResult Abort(MqttReasonCode reasonCode = MqttReasonCode.NotAuthorized, string? reasonString = null)
        => new(MqttAuthenticationResultKind.Abort, default, reasonCode, reasonString);
}

public enum MqttAuthenticationResultKind : byte
{
    Continue = 0,
    Final = 1,
    Abort = 2,
}

/// <summary>Raised when MQTT 5 enhanced authentication fails.</summary>
public sealed class MqttAuthenticationException : Exception
{
    public MqttAuthenticationException(MqttReasonCode reasonCode, string? reasonString = null)
        : base($"MQTT authentication failed: {reasonCode} ({reasonString ?? "no reason"})")
    {
        ReasonCode = reasonCode;
        ReasonString = reasonString;
    }

    public MqttReasonCode ReasonCode { get; }
    public string? ReasonString { get; }
}
