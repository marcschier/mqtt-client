// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Verifies that outbound length-limit violations are caught synchronously on the calling thread
/// (preserving error locality now that encoding happens on the write loop).
/// </summary>
public class OutboundValidationTests
{
    private static string OversizedString => new('x', 70_000);

    [Test]
    public async Task ValidatePublish_throws_on_oversized_topic()
    {
        await Assert.That(() => MqttOutboundValidation.ValidatePublish(OversizedString, 0, null))
            .Throws<MqttProtocolException>();
    }

    [Test]
    public async Task ValidatePublish_throws_on_oversized_payload()
    {
        await Assert.That(() => MqttOutboundValidation.ValidatePublish("t", 268_435_456, null))
            .Throws<MqttProtocolException>();
    }

    [Test]
    public async Task ValidatePublish_throws_on_oversized_response_topic()
    {
        var props = new MqttPublishProperties { ResponseTopic = OversizedString };
        await Assert.That(() => MqttOutboundValidation.ValidatePublish("t", 0, props))
            .Throws<MqttProtocolException>();
    }

    [Test]
    public async Task ValidatePublish_accepts_normal_inputs()
    {
        var props = new MqttPublishProperties
        {
            ResponseTopic = "rsp",
            ContentType = "application/json",
            UserProperties = new[] { new MqttUserProperty("k", "v") },
        };
        await Assert.That(() => MqttOutboundValidation.ValidatePublish("a/b/c", 1024, props))
            .ThrowsNothing();
    }

    [Test]
    public async Task ValidateTopicFilter_throws_on_oversized_filter()
    {
        await Assert.That(() => MqttOutboundValidation.ValidateTopicFilter(OversizedString))
            .Throws<MqttProtocolException>();
    }

    [Test]
    [Timeout(5_000)]
    public async Task PublishAsync_throws_synchronously_on_oversized_topic(CancellationToken ct)
    {
        var factory = new FakeTransportFactory();
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "fake",
            ClientId = "t",
            ProtocolVersion = MqttProtocolVersion.V500,
            CleanStart = true,
            KeepAliveSeconds = 0,
            Reconnect = null,
        }, factory);
        await using var _client = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadPacketAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        // The publish must fail at the call site, not on the write loop.
        await Assert.That(async () =>
            await client.PublishAsync(OversizedString, new byte[] { 1 }, cancellationToken: ct))
            .Throws<MqttProtocolException>();

        // TryPublish (synchronous) must throw too.
        await Assert.That(() => client.TryPublish(OversizedString, new byte[] { 1 }))
            .Throws<MqttProtocolException>();

        // The connection is still usable: a valid publish goes through.
        await client.PublishAsync("ok/topic", new byte[] { 1 }, cancellationToken: ct);
        var sent = await broker.ReadPacketAsync(ct);
        await Assert.That(sent.Type).IsEqualTo(3);
    }
}
