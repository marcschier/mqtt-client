// Copyright (c) 2026 marcschier. Licensed under the MIT License.

#if NETSTANDARD2_0
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

// Polyfill of System.Net.Security.SslClientAuthenticationOptions (a netstandard2.1+/.NET type) so
// the TLS configuration surface (MqttClientOptions.Tls, MqttClientBuilder.WithTls) compiles on
// netstandard2.0. The netstandard2.0 TLS path consumes TargetHost, ClientCertificates,
// EnabledSslProtocols and CertificateRevocationCheckMode (see TlsTransport); the remaining members
// mirror the real type so caller configuration code keeps compiling.
namespace System.Net.Security;

/// <summary>netstandard2.0 polyfill for the BCL <c>SslClientAuthenticationOptions</c>.</summary>
public sealed class SslClientAuthenticationOptions
{
    public bool AllowRenegotiation { get; set; } = true;

    public string? TargetHost { get; set; }

    public X509CertificateCollection? ClientCertificates { get; set; }

    public LocalCertificateSelectionCallback? LocalCertificateSelectionCallback { get; set; }

    public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

    public EncryptionPolicy EncryptionPolicy { get; set; } = EncryptionPolicy.RequireEncryption;

    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;

    public X509RevocationMode CertificateRevocationCheckMode { get; set; }
}
#endif
