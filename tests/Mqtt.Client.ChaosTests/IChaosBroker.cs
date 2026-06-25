// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Security.Cryptography.X509Certificates;

namespace Mqtt.Client.ChaosTests;

/// <summary>
/// A controllable in-process MQTT broker used as the chaos target. Implementations host the broker
/// over a specific transport (plain TCP, TLS, or WebSocket) but expose the same fault controls so
/// the soak orchestration is transport-agnostic.
/// </summary>
public interface IChaosBroker : IAsyncDisposable
{
    /// <summary>The TCP port the broker (or its host) listens on.</summary>
    int Port { get; }

    /// <summary>When true, the broker refuses new CONNECTs (via a validating interceptor).</summary>
    bool RejectConnections { get; set; }

    /// <summary>
    /// Stops and restarts the broker on the same port, dropping all session state so a reconnecting
    /// client sees session-present=false (exercising the resubscribe path).
    /// </summary>
    Task RestartAsync();

    /// <summary>Forcibly disconnects every currently-connected client.</summary>
    Task ForceDisconnectAllAsync();

    /// <summary>
    /// The server certificate for TLS/WSS transports, so the chaos client can trust it; null for
    /// plain transports.
    /// </summary>
    X509Certificate2? ServerCertificate => null;
}
