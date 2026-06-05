using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HPD.Agent.Sandbox.Policy;

namespace HPD.Agent.Sandbox.Network;

internal sealed class MitmLeafCertificateCache : IDisposable
{
    private static readonly Oid ServerAuthenticationOid = new("1.3.6.1.5.5.7.3.1");

    private readonly X509Certificate2 _issuerCertificate;
    private readonly ConcurrentDictionary<string, Lazy<X509Certificate2>> _certificates = [];

    public MitmLeafCertificateCache(X509Certificate2 issuerCertificate)
    {
        ArgumentNullException.ThrowIfNull(issuerCertificate);

        if (!issuerCertificate.HasPrivateKey)
            throw new ArgumentException("Issuer certificate must include a private key.", nameof(issuerCertificate));

        _issuerCertificate = issuerCertificate;
    }

    public X509Certificate2 GetOrCreate(string host)
    {
        if (!HostCanonicalizer.TryCanonicalize(host, out var canonical, out var error))
            throw new ArgumentException(error, nameof(host));

        return _certificates.GetOrAdd(
            canonical.Value,
            key => new Lazy<X509Certificate2>(
                () => CreateLeafCertificate(canonical),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private X509Certificate2 CreateLeafCertificate(CanonicalHost host)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={host.Value}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { ServerAuthenticationOid },
                critical: true));

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        if (host.IsIpLiteral && IPAddress.TryParse(host.Value, out var address))
            subjectAlternativeNames.AddIpAddress(address);
        else
            subjectAlternativeNames.AddDnsName(host.Value);
        request.CertificateExtensions.Add(subjectAlternativeNames.Build(critical: false));

        var serialNumber = RandomNumberGenerator.GetBytes(16);
        var now = DateTimeOffset.UtcNow;
        var notAfter = now.AddDays(1);
        if (notAfter >= _issuerCertificate.NotAfter)
            notAfter = new DateTimeOffset(_issuerCertificate.NotAfter).AddSeconds(-1);

        using var signedCertificate = request.Create(
            _issuerCertificate,
            now.AddMinutes(-5),
            notAfter,
            serialNumber);

        return signedCertificate.CopyWithPrivateKey(rsa);
    }

    public void Dispose()
    {
        foreach (var lazy in _certificates.Values)
        {
            if (lazy.IsValueCreated)
                lazy.Value.Dispose();
        }

        _certificates.Clear();
    }
}
