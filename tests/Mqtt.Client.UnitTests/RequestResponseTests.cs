// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Exercises the MQTT 5 request/response helper: a request carries a Response Topic and unique
/// Correlation Data, and the matching reply completes the awaiting call.
/// </summary>
public class RequestResponseTests
{
    [Test]
    [Timeout(10_000)]
    public async Task RequestAsync_correlates_and_returns_the_response(CancellationToken ct)
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
        await using var _0 = client;
        var broker = new FakeBroker(factory.Transport);
        var connectTask = client.ConnectAsync(ct);
        await broker.ReadConnectAsync(ct);
        await broker.SendConnAckAsync(ct: ct);
        await connectTask;

        var reqTask = client.RequestAsync("svc/req", new byte[] { 1, 2, 3 }, cancellationToken: ct);

        // 1) The client subscribes to its response topic.
        var subSent = await broker.ReadPacketAsync(ct);
        await Assert.That(subSent.Type).IsEqualTo(8);   // SUBSCRIBE
        await broker.SendSubAckAsync(subSent.PacketId, MqttReasonCode.Success, ct);

        // 2) The client publishes the request (QoS 1) with Response Topic + Correlation Data.
        var reqPkt = (PublishPacket)(await broker.ReadDecodedPacketAsync(ct))!;
        await Assert.That(reqPkt.Topic).IsEqualTo("svc/req");
        var responseTopic = reqPkt.Properties!.ResponseTopic!;
        var correlation = reqPkt.Properties!.CorrelationData!.Value;
        await Assert.That(responseTopic.Length).IsGreaterThan(0);
        await broker.SendPubAckAsync(reqPkt.PacketId, ct: ct);

        // 3) Responder replies on the response topic echoing the correlation data.
        await broker.SendPublishWithCorrelationAsync(
            responseTopic, correlation, new byte[] { 9 }, ct: ct);

        using var response = await reqTask;
        await Assert.That(response.PayloadMemory.Span[0]).IsEqualTo((byte)9);
    }
}
