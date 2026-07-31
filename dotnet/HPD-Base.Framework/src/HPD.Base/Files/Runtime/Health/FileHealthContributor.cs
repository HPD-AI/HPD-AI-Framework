using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class FileHealthContributor : IBaseHealthContributor, IBaseDiagnosticContributor
{
    private readonly HPDBaseFilesOptions _options;

    public FileHealthContributor(IOptions<HPDBaseFilesOptions> options)
    {
        _options = options.Value;
    }

    public string Id => FileModuleIds.Module;

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UnixEpoch;
        return ValueTask.FromResult<HealthDescriptor[]>(
        [
            new HealthDescriptor
            {
                Id = FileHealthIds.Registration,
                Scope = HealthScope.Module,
                TargetRef = FileModuleIds.Module,
                Status = _options.Enabled ? HealthStatus.Healthy : HealthStatus.Disabled,
                CheckedAt = now,
                Summary = _options.Enabled ? "Files module is registered." : "Files module is disabled.",
                PublicSafe = true,
                Visibility = VisibilityLevel.Public
            },
            new HealthDescriptor
            {
                Id = FileHealthIds.Provider,
                Scope = HealthScope.Dependency,
                TargetRef = FileModuleIds.Module,
                Status = HealthStatus.Degraded,
                CheckedAt = now,
                Summary = "No file provider health probe is configured in the scaffold.",
                PublicSafe = false,
                Visibility = VisibilityLevel.Admin
            },
            .. _options.Buckets.Select(bucket => new HealthDescriptor
            {
                Id = FileHealthIds.Bucket(bucket.BucketId),
                Scope = HealthScope.Dependency,
                TargetRef = bucket.BucketId.Value,
                Status = !_options.Enabled || !bucket.Enabled ? HealthStatus.Disabled : HealthStatus.Unknown,
                CheckedAt = now,
                Summary = bucket.Enabled ? "File bucket is registered." : "File bucket is disabled.",
                PublicSafe = bucket.Visibility == FileBucketVisibility.PublicRead && bucket.DescriptorVisibility == VisibilityLevel.Public,
                Visibility = bucket.DescriptorVisibility
            })
        ]);
    }

    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<DiagnosticDescriptor[]>(
        [
            Diagnostic(FileDiagnosticIds.NoProvider, DiagnosticSeverity.Warning, "No file storage provider is configured."),
            Diagnostic(FileDiagnosticIds.PolicyUnavailable, DiagnosticSeverity.Warning, "No file policy provider is configured."),
            Diagnostic(FileDiagnosticIds.BucketDisabled, DiagnosticSeverity.Info, "One or more file buckets may be disabled."),
            Diagnostic(FileDiagnosticIds.InvalidKey, DiagnosticSeverity.Info, "File object key validation is active."),
            Diagnostic(FileDiagnosticIds.ContentTypeRejected, DiagnosticSeverity.Info, "File content type validation is active."),
            Diagnostic(FileDiagnosticIds.SizeExceeded, DiagnosticSeverity.Info, "File size validation is active."),
            Diagnostic(FileDiagnosticIds.ChecksumRejected, DiagnosticSeverity.Info, "File checksum validation is active."),
            Diagnostic(FileDiagnosticIds.ProviderSecretRedacted, DiagnosticSeverity.Info, "Provider secret redaction is active."),
            Diagnostic(FileDiagnosticIds.PublicBucketWarning, DiagnosticSeverity.Warning, "Public file buckets require explicit configuration.")
        ]);
    }

    private static DiagnosticDescriptor Diagnostic(string id, DiagnosticSeverity severity, string message) => new()
    {
        Id = id,
        Code = id,
        Severity = severity,
        TargetRef = FileModuleIds.Module,
        Message = message,
        PublicMessage = "Files module diagnostic is available.",
        Category = DiagnosticCategory.Capability,
        Visibility = VisibilityLevel.Admin,
        EmittedAt = DateTimeOffset.UnixEpoch
    };
}
