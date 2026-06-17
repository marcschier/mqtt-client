// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client.Transport;

/// <summary>
/// Abstraction over an underlying byte-stream transport (TCP/TLS/WebSocket).
/// Exposes a <see cref="PipeReader"/>/<see cref="PipeWriter"/> pair which the
/// MQTT connection uses for framing.
/// </summary>
internal interface IMqttTransport : IAsyncDisposable
{
    PipeReader Input { get; }
    PipeWriter Output { get; }
    string? RemoteAddress { get; }
}

/// <summary>Factory for creating new transport instances on (re)connect.</summary>
internal interface IMqttTransportFactory
{
    ValueTask<IMqttTransport> ConnectAsync(CancellationToken cancellationToken);
}
