// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mqtt.Client;

/// <summary>
/// A live MQTT subscription. Inbound messages are exposed through <see cref="Reader"/>
/// (channels-style) or the <see cref="MqttSubscriptionExtensions.ReadAllAsync"/> extension.
/// </summary>
public sealed class MqttSubscription : IAsyncDisposable
{
    private readonly Channel<MqttMessage> _channel;
    private readonly Func<MqttSubscription, ValueTask> _onDispose;
    private int _disposed;

    internal MqttSubscription(string topicFilter, MqttSubscriptionOptions options, Func<MqttSubscription, ValueTask> onDispose)
    {
        TopicFilter = topicFilter;
        Options = options;
        _onDispose = onDispose;
        _channel = options.Overflow switch
        {
            MqttOverflowMode.Wait => Channel.CreateBounded<MqttMessage>(new BoundedChannelOptions(options.Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            }),
            MqttOverflowMode.DropOldest => Channel.CreateBounded<MqttMessage>(new BoundedChannelOptions(options.Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            }),
            MqttOverflowMode.DropNewest => Channel.CreateBounded<MqttMessage>(new BoundedChannelOptions(options.Capacity)
            {
                FullMode = BoundedChannelFullMode.DropNewest,
                SingleReader = true,
                SingleWriter = true,
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
    }

    public string TopicFilter { get; }
    public MqttSubscriptionOptions Options { get; }

    /// <summary>Channel reader exposing inbound messages.</summary>
    public ChannelReader<MqttMessage> Reader => _channel.Reader;

    internal ChannelWriter<MqttMessage> Writer => _channel.Writer;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _channel.Writer.TryComplete();
        await _onDispose(this).ConfigureAwait(false);
    }
}

/// <summary>Convenience extensions for consuming subscriptions.</summary>
public static class MqttSubscriptionExtensions
{
    /// <summary>Reads all messages until the subscription is disposed or cancellation occurs.</summary>
    public static System.Collections.Generic.IAsyncEnumerable<MqttMessage> ReadAllAsync(this MqttSubscription subscription, CancellationToken cancellationToken = default)
    {
        if (subscription is null) throw new ArgumentNullException(nameof(subscription));
        return subscription.Reader.ReadAllAsync(cancellationToken);
    }
}
