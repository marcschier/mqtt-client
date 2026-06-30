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

    [Test]
    public async Task ValidatePublish_throws_on_oversized_content_type()
    {
        var props = new MqttPublishProperties { ContentType = OversizedString };
        await Assert.That(() => MqttOutboundValidation.ValidatePublish("t", 0, props))
            .Throws<MqttProtocolException>();
    }

    [Test]
    public async Task ValidatePublish_throws_on_oversized_correlation_data()
    {
        var props = new MqttPublishProperties { CorrelationData = new byte[66_000] };
        await Assert.That(() => MqttOutboundValidation.ValidatePublish("t", 0, props))
            .Throws<MqttProtocolException>();
    }

    [Test]
    public async Task ValidatePublish_throws_on_oversized_user_property_name()
    {
        var props = new MqttPublishProperties
        {
            UserProperties = new[] { new MqttUserProperty(OversizedString, "v") },
        };
        await Assert.That(() => MqttOutboundValidation.ValidatePublish("t", 0, props))
            .Throws<MqttProtocolException>();
    }

    [Test]
    public async Task ValidatePublish_throws_on_oversized_user_property_value()
    {
        var props = new MqttPublishProperties
        {
            UserProperties = new[] { new MqttUserProperty("k", OversizedString) },
        };
        await Assert.That(() => MqttOutboundValidation.ValidatePublish("t", 0, props))
            .Throws<MqttProtocolException>();
    }

    [Test]
    public async Task ComputePublishPacketSize_computes_exact_wire_size()
    {
        static long Size(string topic, long payload, MqttQoS qos, MqttProtocolVersion v)
            => MqttOutboundValidation.ComputePublishPacketSize(topic, payload, qos, null, v);

        var v3Qos0 = Size("t", 10, MqttQoS.AtMostOnce, MqttProtocolVersion.V311);
        var v3Qos1 = Size("t", 10, MqttQoS.AtLeastOnce, MqttProtocolVersion.V311);
        var v5Qos0 = Size("t", 10, MqttQoS.AtMostOnce, MqttProtocolVersion.V500);
        var rem2Byte = Size("t", 200, MqttQoS.AtMostOnce, MqttProtocolVersion.V311);
        var rem3Byte = Size("t", 20_000, MqttQoS.AtMostOnce, MqttProtocolVersion.V311);
        var rem4Byte = Size("t", 3_000_000, MqttQoS.AtMostOnce, MqttProtocolVersion.V311);

        await Assert.That(v3Qos0).IsEqualTo(15L);     // 1 + 1 + (2 + 1) + 10
        await Assert.That(v3Qos1).IsEqualTo(17L);     // + 2-byte packet identifier
        await Assert.That(v5Qos0).IsEqualTo(16L);     // + 1-byte (zero) property length
        await Assert.That(rem2Byte).IsEqualTo(206L);  // 1 + 2 + 3 + 200
        await Assert.That(rem3Byte).IsEqualTo(20_007L);    // 1 + 3 + 3 + 20000
        await Assert.That(rem4Byte).IsEqualTo(3_000_008L); // 1 + 4 + 3 + 3_000_000
    }

    [Test]
    public async Task ComputePublishPacketSize_includes_v5_properties()
    {
        // ContentType "aj" contributes 3 (id + 2-byte len) + 2 (value) = 5 property bytes.
        var props = new MqttPublishProperties { ContentType = "aj" };
        var size = MqttOutboundValidation.ComputePublishPacketSize(
            "t", 0, MqttQoS.AtMostOnce, props, MqttProtocolVersion.V500);

        // 1 (fixed) + 1 (remlen) + 3 (topic) + 6 (props: 1 len + 5) + 0 payload = 11.
        await Assert.That(size).IsEqualTo(11L);
    }
}
