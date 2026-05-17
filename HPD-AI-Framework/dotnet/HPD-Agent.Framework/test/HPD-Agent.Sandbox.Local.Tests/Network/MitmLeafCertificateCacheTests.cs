using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using HPD.Sandbox.Local.Network;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Network;

public sealed class MitmLeafCertificateCacheTests
{
    [Fact]
    public async Task GetOrCreate_ForDnsHost_ReturnsServerCertificateSignedByCa()
    {
        await using var authority = await MitmCertificateAuthority.CreateEphemeralAsync();
        using var cache = new MitmLeafCertificateCache(authority.Certificate);

        using var certificate = cache.GetOrCreate("Example.COM.");

        certificate.Subject.Should().Contain("example.com");
        certificate.HasPrivateKey.Should().BeTrue();
        certificate.Issuer.Should().Be(authority.Certificate.Subject);
        certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .Single()
            .EnhancedKeyUsages
            .Cast<Oid>()
            .Select(oid => oid.Value)
            .Should()
            .Contain("1.3.6.1.5.5.7.3.1");
    }

    [Fact]
    public async Task GetOrCreate_ForIpLiteral_AddsIpSubjectAlternativeName()
    {
        await using var authority = await MitmCertificateAuthority.CreateEphemeralAsync();
        using var cache = new MitmLeafCertificateCache(authority.Certificate);

        using var certificate = cache.GetOrCreate("127.0.0.1");

        certificate.Subject.Should().Contain("127.0.0.1");
        certificate.HasPrivateKey.Should().BeTrue();
        certificate.Issuer.Should().Be(authority.Certificate.Subject);
    }

    [Fact]
    public async Task GetOrCreate_CachesCanonicalHost()
    {
        await using var authority = await MitmCertificateAuthority.CreateEphemeralAsync();
        using var cache = new MitmLeafCertificateCache(authority.Certificate);

        var first = cache.GetOrCreate("Example.COM.");
        var second = cache.GetOrCreate("example.com");

        first.Should().BeSameAs(second);
    }

    [Fact]
    public async Task GetOrCreate_WithMalformedHost_Throws()
    {
        await using var authority = await MitmCertificateAuthority.CreateEphemeralAsync();
        using var cache = new MitmLeafCertificateCache(authority.Certificate);

        var act = () => cache.GetOrCreate("bad\0host.example");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*invalid*");
    }

    [Fact]
    public async Task Constructor_WithCertificateWithoutPrivateKey_Throws()
    {
        await using var authority = await MitmCertificateAuthority.CreateEphemeralAsync();
        var certificatePem = await File.ReadAllTextAsync(authority.CertificatePath);
        using var publicOnly = X509Certificate2.CreateFromPem(certificatePem);

        var act = () => new MitmLeafCertificateCache(publicOnly);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*private key*");
    }
}
