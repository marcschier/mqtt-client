// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

public class ExceptionsAndEventArgsTests
{
    [Test]
    public async Task MqttProtocolException_ctors()
    {
        var ex1 = new MqttProtocolException("msg");
        await Assert.That(ex1.Message).IsEqualTo("msg");
        var inner = new InvalidOperationException("inner");
        var ex2 = new MqttProtocolException("msg", inner);
        await Assert.That(ex2.InnerException).IsSameReferenceAs(inner);
    }

    [Test]
    public async Task MqttConnectionException_ctors()
    {
        var ex1 = new MqttConnectionException("conn");
        await Assert.That(ex1.Message).IsEqualTo("conn");
        var inner = new InvalidOperationException();
        var ex2 = new MqttConnectionException("conn", inner);
        await Assert.That(ex2.InnerException).IsSameReferenceAs(inner);
    }

    [Test]
    public async Task MqttDisconnectedEventArgs_captures_reason_and_exception()
    {
        var ex = new MqttConnectionException("boom");
        var args = new MqttDisconnectedEventArgs("test", ex);
        await Assert.That(args.Reason).IsEqualTo("test");
        await Assert.That(args.Exception).IsSameReferenceAs(ex);
    }

    [Test]
    public async Task MqttReconnectPolicy_Fixed_sets_constant_delay()
    {
        var p = MqttReconnectPolicy.Fixed(TimeSpan.FromSeconds(5));
        await Assert.That(p.InitialDelay).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(p.MaxDelay).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(p.BackoffFactor).IsEqualTo(1.0);
        await Assert.That(p.JitterFactor).IsEqualTo(0.0);
    }

    [Test]
    public async Task MqttReconnectPolicy_Exponential_has_jitter_and_backoff()
    {
        var p = MqttReconnectPolicy.Exponential();
        await Assert.That(p.BackoffFactor).IsGreaterThan(1.0);
        await Assert.That(p.JitterFactor).IsGreaterThan(0.0);
    }

    [Test]
    public async Task MqttSubscriptionOptions_defaults_are_sane()
    {
        var o = new MqttSubscriptionOptions();
        await Assert.That(o.QoS).IsEqualTo(MqttQoS.AtMostOnce);
        await Assert.That(o.Capacity).IsEqualTo(1024);
        await Assert.That(o.Overflow).IsEqualTo(MqttOverflowMode.Wait);
        await Assert.That(o.NoLocal).IsFalse();
    }

    [Test]
    public async Task MqttPublishResult_IsSuccess_for_low_reason_codes()
    {
        await Assert.That(new MqttPublishResult(MqttReasonCode.Success).IsSuccess).IsTrue();
        await Assert.That(new MqttPublishResult(MqttReasonCode.NoMatchingSubscribers).IsSuccess)
            .IsTrue();
        await Assert.That(new MqttPublishResult(MqttReasonCode.NotAuthorized).IsSuccess).IsFalse();
    }
}
