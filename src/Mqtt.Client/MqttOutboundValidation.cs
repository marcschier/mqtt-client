// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace Mqtt.Client;

/// <summary>
/// Validates outbound packet inputs on the calling thread before they are queued, so that
/// length-limit violations surface synchronously at the <c>PublishAsync</c>/<c>SubscribeAsync</c>
/// call site even though the actual encoding happens later on the write loop (see
/// <see cref="PipeBufferWriter"/>). Mirrors the limits the encoder enforces.
/// </summary>
internal static class MqttOutboundValidation
{
    private const int MaxStringBytes = 65535;
    private const long MaxRemainingLength = 268_435_455;

    public static void ValidatePublish(
        string topic, long payloadLength, MqttPublishProperties? properties)
    {
        ValidateString(topic, nameof(topic));
        if (payloadLength > MaxRemainingLength)
        {
            throw new MqttProtocolException("Publish payload exceeds the MQTT maximum length.");
        }
        if (properties is { } p)
        {
            if (p.ResponseTopic is { } rt) ValidateString(rt, nameof(p.ResponseTopic));
            if (p.ContentType is { } ct) ValidateString(ct, nameof(p.ContentType));
            if (p.CorrelationData is { } cd && cd.Length > MaxStringBytes)
            {
                throw new MqttProtocolException("CorrelationData exceeds 65535 bytes.");
            }
            ValidateUserProperties(p.UserProperties);
        }
    }

    public static void ValidateTopicFilter(string filter) => ValidateString(filter, nameof(filter));

    /// <summary>
    /// Computes the exact encoded size (bytes) of a PUBLISH control packet, so the publish path can
    /// reject a packet exceeding the broker's Maximum Packet Size before it is queued.
    /// </summary>
    public static long ComputePublishPacketSize(
        string topic,
        long payloadLength,
        MqttQoS qos,
        MqttPublishProperties? properties,
        MqttProtocolVersion version)
    {
        long varHeader = 2 + Encoding.UTF8.GetByteCount(topic);
        if (qos != MqttQoS.AtMostOnce) varHeader += 2; // packet identifier
        if (version == MqttProtocolVersion.V500)
        {
            long propsLen = ComputePublishPropertiesLength(properties);
            varHeader += VarIntLength(propsLen) + propsLen;
        }
        long remaining = varHeader + payloadLength;
        return 1 + VarIntLength(remaining) + remaining;
    }

    private static long ComputePublishPropertiesLength(MqttPublishProperties? p)
    {
        if (p is null) return 0;
        long len = 0;
        if (p.PayloadFormatIndicator is not null) len += 2;
        if (p.MessageExpiryInterval is not null) len += 5;
        if (p.TopicAlias is not null) len += 3;
        if (p.ResponseTopic is { } rt) len += 3 + Encoding.UTF8.GetByteCount(rt);
        if (p.CorrelationData is { } cd) len += 3 + cd.Length;
        if (p.ContentType is { } ct) len += 3 + Encoding.UTF8.GetByteCount(ct);
        if (p.SubscriptionIdentifiers is { } sids)
        {
            foreach (var id in sids) { len += 1 + VarIntLength(id); }
        }
        if (p.UserProperties is { } ups)
        {
            foreach (var up in ups)
            {
                len += 5 + Encoding.UTF8.GetByteCount(up.Name)
                    + Encoding.UTF8.GetByteCount(up.Value);
            }
        }
        return len;
    }

    private static int VarIntLength(long value)
    {
        if (value < 128) return 1;
        if (value < 16384) return 2;
        if (value < 2_097_152) return 3;
        return 4;
    }

    private static void ValidateUserProperties(IReadOnlyList<MqttUserProperty>? props)
    {
        if (props is null) return;
        for (var i = 0; i < props.Count; i++)
        {
            ValidateString(props[i].Name, "UserProperty.Name");
            ValidateString(props[i].Value, "UserProperty.Value");
        }
    }

    private static void ValidateString(string value, string field)
    {
        if (value is null)
        {
            throw new ArgumentNullException(field);
        }
        if (Encoding.UTF8.GetByteCount(value) > MaxStringBytes)
        {
            throw new MqttProtocolException($"{field} exceeds {MaxStringBytes} bytes.");
        }
    }
}
