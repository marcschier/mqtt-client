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
