using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HPD.Agent.Sandbox.Network;

internal sealed record TlsTerminationConfig
{
    public string? CaCertificatePath { get; init; }
    public string? CaPrivateKeyPath { get; init; }
    public bool InjectTrustEnvironmentVariables { get; init; } = true;
}

internal sealed class MitmCertificateAuthority : IAsyncDisposable
{
    private readonly string _directoryPath;
    private readonly bool _deleteDirectoryOnDispose;

    private MitmCertificateAuthority(
        string directoryPath,
        string certificatePath,
        string privateKeyPath,
        X509Certificate2 certificate,
        bool deleteDirectoryOnDispose)
    {
        _directoryPath = directoryPath;
        _deleteDirectoryOnDispose = deleteDirectoryOnDispose;
        CertificatePath = certificatePath;
        PrivateKeyPath = privateKeyPath;
        Certificate = certificate;
    }

    public string CertificatePath { get; }

    public string PrivateKeyPath { get; }

    public X509Certificate2 Certificate { get; }

    public TlsTerminationConfig ToTlsTerminationConfig(TlsTerminationConfig config) =>
        config with
        {
            CaCertificatePath = CertificatePath,
            CaPrivateKeyPath = PrivateKeyPath
        };

    public static async Task<MitmCertificateAuthority> CreateEphemeralAsync(
        CancellationToken cancellationToken = default)
    {
        var directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"hpd-sandbox-ca-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);

        var certificatePath = Path.Combine(directoryPath, "ca-cert.pem");
        var privateKeyPath = Path.Combine(directoryPath, "ca-key.pem");

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=HPD Process Isolation Local Ephemeral CA",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(
                request.PublicKey,
                critical: false));

        var now = DateTimeOffset.UtcNow;
        using var generated = request.CreateSelfSigned(
            now.AddMinutes(-5),
            now.AddDays(1));
        var certificate = new X509Certificate2(
            generated.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.Exportable);

        await File.WriteAllTextAsync(
            certificatePath,
            certificate.ExportCertificatePem(),
            cancellationToken);
        await File.WriteAllTextAsync(
            privateKeyPath,
            rsa.ExportPkcs8PrivateKeyPem(),
            cancellationToken);

        return new MitmCertificateAuthority(
            directoryPath,
            certificatePath,
            privateKeyPath,
            certificate,
            deleteDirectoryOnDispose: true);
    }

    public static Task<MitmCertificateAuthority> LoadAsync(
        string certificatePath,
        string privateKeyPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var certificate = X509Certificate2.CreateFromPemFile(certificatePath, privateKeyPath);
        return Task.FromResult(new MitmCertificateAuthority(
            Path.GetDirectoryName(certificatePath) ?? string.Empty,
            certificatePath,
            privateKeyPath,
            certificate,
            deleteDirectoryOnDispose: false));
    }

    public static async Task<(TlsTerminationConfig Config, MitmCertificateAuthority Authority)> ResolveAsync(
        TlsTerminationConfig config,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(config.CaCertificatePath))
        {
            var loadedAuthority = await LoadAsync(
                config.CaCertificatePath,
                config.CaPrivateKeyPath!,
                cancellationToken);
            return (config, loadedAuthority);
        }

        var authority = await CreateEphemeralAsync(cancellationToken);
        return (authority.ToTlsTerminationConfig(config), authority);
    }

    public ValueTask DisposeAsync()
    {
        Certificate.Dispose();

        try
        {
            if (_deleteDirectoryOnDispose && Directory.Exists(_directoryPath))
                Directory.Delete(_directoryPath, recursive: true);
        }
        catch
        {
            // Ephemeral CA cleanup is best effort. The directory is unique and
            // under the OS temp path, so a failed delete should not break
            // sandbox disposal.
        }

        return ValueTask.CompletedTask;
    }
}
