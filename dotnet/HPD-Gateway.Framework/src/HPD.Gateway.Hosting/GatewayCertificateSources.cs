using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HPD.Gateway.Abstractions;

namespace HPD.Gateway.Hosting;

public sealed record GatewayPfxCertificateSource
{
    public required string Path { get; init; }
    public string? Password { get; init; }
}

public sealed class GatewayCertificateSourceRegistryBuilder
{
    private readonly Dictionary<SecretReference, GatewayPfxCertificateSource> _sources = [];

    public GatewayCertificateSourceRegistryBuilder Add(SecretReference reference, GatewayPfxCertificateSource source)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(source);
        if (!GatewayIdentifier.IsCanonical(reference.Provider.Value) || !GatewayIdentifier.IsCanonical(reference.Name.Value) ||
            reference.Version is not null && !GatewayIdentifier.IsCanonical(reference.Version))
            throw new ArgumentException("Certificate source reference must be canonical.", nameof(reference));
        if (!System.IO.Path.IsPathFullyQualified(source.Path) || !File.Exists(source.Path))
            throw new ArgumentException("Certificate source must be an existing absolute file.", nameof(source));
        if (!_sources.TryAdd(reference, source)) throw new ArgumentException("Certificate source references must be unique.", nameof(reference));
        return this;
    }

    internal GatewayCertificateSourceRegistry Build()
    {
        if (_sources.Count is < 1 or > 1_024) throw new InvalidOperationException("One to 1,024 certificate sources are required.");
        foreach (var source in _sources.Values)
        {
            try
            {
                using var certificate = X509CertificateLoader.LoadPkcs12FromFile(source.Path, source.Password, X509KeyStorageFlags.DefaultKeySet);
                if (!certificate.HasPrivateKey || certificate.NotBefore > DateTime.Now || certificate.NotAfter <= DateTime.Now ||
                    !IsSuitableForServerAuthentication(certificate))
                    throw new InvalidOperationException("Certificate source is invalid for server authentication.");
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Certificate source cannot be loaded.");
            }
        }
        return new(_sources.ToImmutableDictionary());
    }

    private static bool IsSuitableForServerAuthentication(X509Certificate2 certificate)
    {
        const string serverAuthenticationOid = "1.3.6.1.5.5.7.3.1";
        var enhancedKeyUsage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (enhancedKeyUsage is not null &&
            !enhancedKeyUsage.EnhancedKeyUsages.Cast<Oid>()
                .Any(oid => StringComparer.Ordinal.Equals(oid.Value, serverAuthenticationOid)))
            return false;

        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        return keyUsage is null ||
            (keyUsage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment)) != 0;
    }
}

internal sealed class GatewayCertificateSourceRegistry(ImmutableDictionary<SecretReference, GatewayPfxCertificateSource> sources)
{
    internal GatewayPfxCertificateSource Resolve(SecretReference reference, string hostnamePattern)
    {
        if (!sources.TryGetValue(reference, out var source))
            throw new InvalidOperationException("A referenced certificate source is not installed.");
        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(source.Path, source.Password, X509KeyStorageFlags.DefaultKeySet);
            var names = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>()
                .SelectMany(static extension => extension.EnumerateDnsNames())
                .Select(static name => name.ToLowerInvariant().TrimEnd('.'));
            if (!names.Contains(hostnamePattern, StringComparer.Ordinal))
                throw new InvalidOperationException("A referenced certificate does not cover its declared SNI pattern.");
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Certificate source cannot be loaded.");
        }
        return source;
    }
}
