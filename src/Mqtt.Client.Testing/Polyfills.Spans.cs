// Copyright (c) 2026 marcschier. Licensed under the MIT License.

#if NETSTANDARD2_0
using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mqtt.Client.Testing;

// netstandard2.0 lacks the span-based Encoding overloads and the Memory-based Stream async I/O
// added in netstandard2.1/.NET Core 2.1. These internal extension methods supply them so the
// broker code compiles unchanged; on net8.0+/netstandard2.1 the BCL instance methods bind instead
// and these are not compiled at all.
internal static class Ns20Polyfills
{
    public static string GetString(this Encoding encoding, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return string.Empty;
        var rented = ArrayPool<byte>.Shared.Rent(bytes.Length);
        try
        {
            bytes.CopyTo(rented);
            return encoding.GetString(rented, 0, bytes.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static int GetBytes(
        this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        if (chars.IsEmpty) return 0;
        var rentedChars = ArrayPool<char>.Shared.Rent(chars.Length);
        var rentedBytes = ArrayPool<byte>.Shared.Rent(bytes.Length);
        try
        {
            chars.CopyTo(rentedChars);
            var written = encoding.GetBytes(rentedChars, 0, chars.Length, rentedBytes, 0);
            rentedBytes.AsSpan(0, written).CopyTo(bytes);
            return written;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rentedChars);
            ArrayPool<byte>.Shared.Return(rentedBytes);
        }
    }

    public static ValueTask<int> ReadAsync(
        this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)buffer, out var seg))
        {
            return new ValueTask<int>(
                stream.ReadAsync(seg.Array!, seg.Offset, seg.Count, cancellationToken));
        }
        return ReadCopyAsync(stream, buffer, cancellationToken);
    }

    private static async ValueTask<int> ReadCopyAsync(
        Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = await stream.ReadAsync(rented, 0, buffer.Length, cancellationToken)
                .ConfigureAwait(false);
            rented.AsSpan(0, read).CopyTo(buffer.Span);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static ValueTask WriteAsync(
        this Stream stream,
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (MemoryMarshal.TryGetArray(buffer, out var seg))
        {
            return new ValueTask(
                stream.WriteAsync(seg.Array!, seg.Offset, seg.Count, cancellationToken));
        }
        var copy = buffer.ToArray();
        return new ValueTask(stream.WriteAsync(copy, 0, copy.Length, cancellationToken));
    }

    public static ValueTask DisposeAsync(this Stream stream)
    {
        stream.Dispose();
        return default;
    }
}
#endif
