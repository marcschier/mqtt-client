// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

/// <summary>
/// Delegate for encoding into a <see cref="PipeBufferWriter"/> by reference, mirroring
/// <see cref="RefBufferWriterEncode"/> for the pipe-backed writer.
/// </summary>
internal delegate void RefPipeBufferWriterEncode(ref PipeBufferWriter w);
