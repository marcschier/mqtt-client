// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Delegate for encoding into a <see cref="MqttBufferWriter"/> by reference, so tests can hold a
/// table of encode actions even though the writer is a mutable struct passed by <c>ref</c>.
/// </summary>
internal delegate void RefBufferWriterEncode(ref MqttBufferWriter w);
