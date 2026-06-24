// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Threading.Channels;

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Transport factory that vends a fresh <see cref="FakePipeTransport"/> on every connect, so each
/// reconnect attempt gets its own pipes. Created transports are exposed via <see cref="Created"/>.
/// </summary>
internal sealed class MultiConnectFakeFactory : IMqttTransportFactory
{
    private readonly Channel<FakePipeTransport> _created =
        Channel.CreateUnbounded<FakePipeTransport>();

    public ChannelReader<FakePipeTransport> Created => _created.Reader;

    public ValueTask<IMqttTransport> ConnectAsync(CancellationToken cancellationToken)
    {
        var transport = new FakePipeTransport();
        _created.Writer.TryWrite(transport);
        return new ValueTask<IMqttTransport>(transport);
    }
}
