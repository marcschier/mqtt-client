// Copyright (c) 2026 marcschier. Licensed under the MIT License.

using System.Buffers;
namespace Mqtt.Client.UnitTests;

public class SequenceReaderTests
{
    [Test]
    public async Task ReadString_segmented_input_decodes_correctly()
    {
        // 2-byte length 0x00 0x05 + "hello" split across two segments.
        var first = new byte[] { 0x00, 0x05, (byte)'h' };
        var second = new byte[] { (byte)'e', (byte)'l', (byte)'l', (byte)'o' };
        var firstSeg = new TestSegment(first);
        var secondSeg = firstSeg.Append(second);
        var seq = new ReadOnlySequence<byte>(firstSeg, 0, secondSeg, secondSeg.Memory.Length);

        var reader = new MqttSequenceReader(seq);
        var s = reader.ReadString();
        await Assert.That(s).IsEqualTo("hello");
    }

    [Test]
    public async Task ReadBinaryData_returns_correct_length()
    {
        var bytes = new byte[] { 0x00, 0x03, 0xAA, 0xBB, 0xCC };
        var reader = new MqttSequenceReader(new ReadOnlySequence<byte>(bytes));
        var b = reader.ReadBinaryData();
        await Assert.That(b.Length).IsEqualTo(3);
        await Assert.That(b[0]).IsEqualTo((byte)0xAA);
    }

    [Test]
    public async Task ReadBinaryData_zero_length_returns_empty()
    {
        var bytes = new byte[] { 0x00, 0x00 };
        var reader = new MqttSequenceReader(new ReadOnlySequence<byte>(bytes));
        var b = reader.ReadBinaryData();
        await Assert.That(b.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ReadString_empty_returns_empty_string()
    {
        var bytes = new byte[] { 0x00, 0x00 };
        var reader = new MqttSequenceReader(new ReadOnlySequence<byte>(bytes));
        var s = reader.ReadString();
        await Assert.That(s).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadByte_throws_at_end_of_buffer()
    {
        var reader = new MqttSequenceReader(ReadOnlySequence<byte>.Empty);
        var exec = () =>
        {
            var r = new MqttSequenceReader(ReadOnlySequence<byte>.Empty);
            r.ReadByte();
        };
        await Assert.That(exec).Throws<MqttProtocolException>();
    }

    private sealed class TestSegment : System.Buffers.ReadOnlySequenceSegment<byte>
    {
        public TestSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }
        public TestSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new TestSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
