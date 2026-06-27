// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// All netstandard polyfills for this package consolidated into one file. Compiled only on the
// older target frameworks; not part of the net8.0/net9.0/net10.0 builds:
//   * #if NETSTANDARD2_0 || NETSTANDARD2_1 — the init/required language-feature attributes.
//   * #if NETSTANDARD2_0 — the span Encoding and Memory-based Stream I/O extension methods the
//     BCL added after netstandard2.0.

#if NETSTANDARD2_0
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#endif

#if NETSTANDARD2_0 || NETSTANDARD2_1
// Language-feature attributes (init-only setters, required members). Present in the BCL from
// netstandard2.1; defined here so the C# compiler can consume them on the older TFMs.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit;

    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field
            | AttributeTargets.Property,
        Inherited = false,
        AllowMultiple = false)]
    internal sealed class RequiredMemberAttribute : Attribute;

    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;
        public string FeatureName { get; }
        public bool IsOptional { get; init; }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute;
}
#endif

#if NETSTANDARD2_0
// Span-based Encoding overloads and Memory-based Stream async I/O added in netstandard2.1. As
// internal extension methods they bind only on netstandard2.0 (the BCL instance methods win
// elsewhere), so the broker code compiles unchanged on every TFM.
namespace Mqtt.Client.Testing
{
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
}
#endif
