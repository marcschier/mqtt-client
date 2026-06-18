// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.IO.Pipelines;
namespace Mqtt.Client.UnitTests;

/// <summary>
/// In-process <see cref="IMqttTransport"/> backed by two paired <see cref="Pipe"/>s.
/// The "client side" reads from <see cref="Input"/> (= what the test writes to <see cref="ToClient"/>)
/// and writes to <see cref="Output"/> (= what the test reads from <see cref="FromClient"/>).
/// </summary>
internal sealed class FakePipeTransport : IMqttTransport
{
    private readonly Pipe _toClient = new();
    private readonly Pipe _fromClient = new();
    public PipeReader Input => _toClient.Reader;
    public PipeWriter Output => _fromClient.Writer;
    public string? RemoteAddress => "fake://broker";
    public PipeWriter ToClient => _toClient.Writer;
    public PipeReader FromClient => _fromClient.Reader;

    public ValueTask DisposeAsync()
    {
        _toClient.Writer.Complete();
        _fromClient.Reader.Complete();
        return default;
    }
}

internal sealed class FakeTransportFactory : IMqttTransportFactory
{
    public FakePipeTransport Transport { get; } = new();
    public ValueTask<IMqttTransport> ConnectAsync(CancellationToken cancellationToken)
        => new(Transport);
}
