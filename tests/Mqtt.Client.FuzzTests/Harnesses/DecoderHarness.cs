// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
using Mqtt.Client;
namespace Mqtt.Client.FuzzTests;

/// <summary>
/// libFuzzer harness for <see cref="MqttPacketDecoder.TryDecode"/>.
/// Contract: the decoder must never throw anything other than
/// <see cref="MqttProtocolException"/> for arbitrary byte sequences.
/// </summary>
internal static class DecoderHarness
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        var arr = data.ToArray();
        try
        {
            MqttPacketDecoder.TryDecode(
                new ReadOnlySequence<byte>(arr),
                MqttProtocolVersion.V500,
                out _,
                out _,
                out _);
        }
        catch (MqttProtocolException)
        {
            // Expected; broker would respond with DISCONNECT.
        }
    }
}
