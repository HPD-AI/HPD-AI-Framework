using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Network;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Network;

public sealed class MitmCertificateAuthorityTests
{
    [Fact]
    public async Task CreateEphemeralAsync_WritesCertificateAndPrivateKeyPemFiles()
    {
        await using var authority = await MitmCertificateAuthority.CreateEphemeralAsync();

        File.Exists(authority.CertificatePath).Should().BeTrue();
        File.Exists(authority.PrivateKeyPath).Should().BeTrue();

        var certificatePem = await File.ReadAllTextAsync(authority.CertificatePath);
        var privateKeyPem = await File.ReadAllTextAsync(authority.PrivateKeyPath);

        certificatePem.Should().Contain("BEGIN CERTIFICATE");
        privateKeyPem.Should().Contain("BEGIN PRIVATE KEY");
    }

    [Fact]
    public async Task CreateEphemeralAsync_CreatesCertificateAuthorityCertificate()
    {
        await using var authority = await MitmCertificateAuthority.CreateEphemeralAsync();

        authority.Certificate.Subject.Should().Contain("HPD Sandbox Local Ephemeral CA");

        var basicConstraints = authority.Certificate.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .Single();
        basicConstraints.CertificateAuthority.Should().BeTrue();

        var keyUsage = authority.Certificate.Extensions
            .OfType<X509KeyUsageExtension>()
            .Single();
        keyUsage.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.KeyCertSign);
        keyUsage.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.CrlSign);
    }

    [Fact]
    public async Task ResolveAsync_WithExplicitCaPaths_LoadsAuthorityWithoutChangingConfig()
    {
        await using var originalAuthority = await MitmCertificateAuthority.CreateEphemeralAsync();
        var config = new TlsTerminationConfig
        {
            CaCertificatePath = originalAuthority.CertificatePath,
            CaPrivateKeyPath = originalAuthority.PrivateKeyPath
        };

        var (resolved, authority) = await MitmCertificateAuthority.ResolveAsync(config);
        await using (authority)
        {
            resolved.Should().BeSameAs(config);
            authority.Should().NotBeNull();
            authority.CertificatePath.Should().Be(config.CaCertificatePath);
            authority.PrivateKeyPath.Should().Be(config.CaPrivateKeyPath);
            authority.Certificate.HasPrivateKey.Should().BeTrue();
        }

        File.Exists(config.CaCertificatePath).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_WithEphemeralConfig_ReturnsConcreteCaPaths()
    {
        var config = new TlsTerminationConfig();

        var (resolved, authority) = await MitmCertificateAuthority.ResolveAsync(config);
        await using (authority)
        {
            authority.Should().NotBeNull();
            resolved.CaCertificatePath.Should().Be(authority!.CertificatePath);
            resolved.CaPrivateKeyPath.Should().Be(authority.PrivateKeyPath);
            File.Exists(resolved.CaCertificatePath).Should().BeTrue();
            File.Exists(resolved.CaPrivateKeyPath).Should().BeTrue();
        }
    }

    [Fact]
    public async Task DisposeAsync_RemovesEphemeralDirectory()
    {
        var authority = await MitmCertificateAuthority.CreateEphemeralAsync();
        var directory = Path.GetDirectoryName(authority.CertificatePath)!;

        await authority.DisposeAsync();

        Directory.Exists(directory).Should().BeFalse();
    }
}
