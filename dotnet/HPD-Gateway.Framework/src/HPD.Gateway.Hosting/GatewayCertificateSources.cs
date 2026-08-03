using System.Collections.Immutable;
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
                if (!certificate.HasPrivateKey || certificate.NotBefore > DateTime.Now || certificate.NotAfter <= DateTime.Now)
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
