// Copyright (c) 2026 marcschier. Licensed under the MIT License.

// All netstandard polyfills consolidated into this one file. Every type here is compiled only on
// the older target frameworks; none of it is part of the net8.0/net9.0/net10.0 builds:
//   * #if NETSTANDARD2_0 || NETSTANDARD2_1 — the init/required language-feature attributes.
//   * #if NETSTANDARD2_0 — types and members the BCL added after netstandard2.0: the nullable
//     annotation attributes, System.Buffers.SequenceReader<T>, an SslClientAuthenticationOptions
//     shim, and the span Encoding / Memory-based Stream I/O extension methods.
// (The always-compiled ReadOnlySequence<byte>.FirstSpan compat wrapper lives in SequenceCompat.cs.)

#if NETSTANDARD2_0
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
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
// Nullable reference-type annotation attributes (present in the BCL from netstandard2.1).
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property)]
    internal sealed class AllowNullAttribute : Attribute;

    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property)]
    internal sealed class DisallowNullAttribute : Attribute;

    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property
            | AttributeTargets.ReturnValue)]
    internal sealed class MaybeNullAttribute : Attribute;

    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property
            | AttributeTargets.ReturnValue)]
    internal sealed class NotNullAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class MaybeNullWhenAttribute : Attribute
    {
        public MaybeNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;
        public bool ReturnValue { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;
        public bool ReturnValue { get; }
    }

    [AttributeUsage(
        AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue,
        AllowMultiple = true)]
    internal sealed class NotNullIfNotNullAttribute : Attribute
    {
        public NotNullIfNotNullAttribute(string parameterName) => ParameterName = parameterName;
        public string ParameterName { get; }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class DoesNotReturnAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class DoesNotReturnIfAttribute : Attribute
    {
        public DoesNotReturnIfAttribute(bool parameterValue) => ParameterValue = parameterValue;
        public bool ParameterValue { get; }
    }

    [AttributeUsage(
        AttributeTargets.Method | AttributeTargets.Property,
        Inherited = false,
        AllowMultiple = true)]
    internal sealed class MemberNotNullAttribute : Attribute
    {
        public MemberNotNullAttribute(string member) => Members = new[] { member };
        public MemberNotNullAttribute(params string[] members) => Members = members;
        public string[] Members { get; }
    }

    [AttributeUsage(
        AttributeTargets.Method | AttributeTargets.Property,
        Inherited = false,
        AllowMultiple = true)]
    internal sealed class MemberNotNullWhenAttribute : Attribute
    {
        public MemberNotNullWhenAttribute(bool returnValue, string member)
        {
            ReturnValue = returnValue;
            Members = new[] { member };
        }

        public MemberNotNullWhenAttribute(bool returnValue, params string[] members)
        {
            ReturnValue = returnValue;
            Members = members;
        }

        public bool ReturnValue { get; }
        public string[] Members { get; }
    }
}

// Minimal System.Buffers.SequenceReader<T> (the BCL type is .NET Core 3.0). Implements only the
// members the multi-segment decode fallback in MqttSequenceReader uses.
namespace System.Buffers
{
    internal ref struct SequenceReader<T>
        where T : unmanaged, IEquatable<T>
    {
        private readonly ReadOnlySequence<T> _sequence;
        private long _consumed;

        public SequenceReader(ReadOnlySequence<T> sequence)
        {
            _sequence = sequence;
            _consumed = 0;
        }

        public readonly long Consumed => _consumed;

        public readonly long Remaining => _sequence.Length - _consumed;

        public readonly bool End => _consumed >= _sequence.Length;

        public readonly SequencePosition Position => _sequence.GetPosition(_consumed);

        public void Advance(long count) => _consumed += count;

        public bool TryRead(out T value)
        {
            foreach (var segment in _sequence.Slice(_consumed))
            {
                if (segment.Length > 0)
                {
                    value = segment.Span[0];
                    _consumed++;
                    return true;
                }
            }
            value = default;
            return false;
        }

        public readonly bool TryCopyTo(Span<T> destination)
        {
            if (Remaining < destination.Length) return false;
            _sequence.Slice(_consumed, destination.Length).CopyTo(destination);
            return true;
        }
    }

    internal static class SequenceReaderExtensions
    {
        public static bool TryReadBigEndian(ref this SequenceReader<byte> reader, out short value)
        {
            Span<byte> buffer = stackalloc byte[2];
            if (!reader.TryCopyTo(buffer)) { value = 0; return false; }
            reader.Advance(2);
            value = BinaryPrimitives.ReadInt16BigEndian(buffer);
            return true;
        }

        public static bool TryReadBigEndian(ref this SequenceReader<byte> reader, out int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            if (!reader.TryCopyTo(buffer)) { value = 0; return false; }
            reader.Advance(4);
            value = BinaryPrimitives.ReadInt32BigEndian(buffer);
            return true;
        }
    }
}

// Polyfill of System.Net.Security.SslClientAuthenticationOptions (a netstandard2.1+/.NET type) so
// the TLS configuration surface (MqttClientOptions.Tls, MqttClientBuilder.WithTls) compiles on
// netstandard2.0. The TLS path consumes TargetHost, ClientCertificates, EnabledSslProtocols and
// CertificateRevocationCheckMode; the remaining members mirror the real type.
namespace System.Net.Security
{
    /// <summary>netstandard2.0 shim for the BCL <c>SslClientAuthenticationOptions</c>.</summary>
    public sealed class SslClientAuthenticationOptions
    {
        public bool AllowRenegotiation { get; set; } = true;

        public string? TargetHost { get; set; }

        public X509CertificateCollection? ClientCertificates { get; set; }

        public LocalCertificateSelectionCallback? LocalCertificateSelectionCallback { get; set; }

        public RemoteCertificateValidationCallback?
            RemoteCertificateValidationCallback { get; set; }

        public EncryptionPolicy EncryptionPolicy { get; set; } = EncryptionPolicy.RequireEncryption;

        public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;

        public X509RevocationMode CertificateRevocationCheckMode { get; set; }
    }
}

// Span-based Encoding overloads and Memory-based Stream async I/O added in netstandard2.1. As
// internal extension methods they bind only on netstandard2.0 (the BCL instance methods win
// elsewhere), so the shared code compiles unchanged on every TFM.
namespace Mqtt.Client
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
