using HPD.Base;
using HPD.Base.Files.Objects;

namespace HPD.Base.Files.Buckets;

public sealed record FileBucketDescriptor
{
    public required FileBucketId BucketId { get; init; }
    public string? DisplayName { get; init; }
    public bool Enabled { get; init; } = true;
    public FileBucketVisibility Visibility { get; init; } = FileBucketVisibility.Private;
    public long? MaxObjectBytes { get; init; }
    public string[]? AllowedContentTypes { get; init; }
    public string[]? AllowedExtensions { get; init; }
    public bool RequireChecksum { get; init; }
    public bool AllowOverwrite { get; init; }
    public string? DefaultCachePolicy { get; init; }
    public string[]? PolicyRefs { get; init; }
    public FileProviderRef? ProviderRef { get; init; }
    public FileBucketCapabilities? Capabilities { get; init; }
    public Dictionary<string, string>? PublicSafeMetadata { get; init; }
    public FileBucketAdminConfigSummary? AdminConfigSummary { get; init; }
    public string? HealthRef { get; init; }
    public string[]? DiagnosticRefs { get; init; }
    public VisibilityLevel DescriptorVisibility { get; init; } = VisibilityLevel.Admin;
}

public enum FileBucketVisibility
{
    Private,
    PublicRead,
    AdminOnly,
    Custom
}

public sealed record FileBucketCapabilities
{
    public bool Upload { get; init; }
    public bool Download { get; init; }
    public bool Metadata { get; init; } = true;
    public bool Delete { get; init; }
    public bool List { get; init; }
}

public sealed record FileBucketAdminConfigSummary
{
    public FileProviderRef? ProviderRef { get; init; }
    public string? StorageClassSummary { get; init; }
    public string[]? CapabilityFlags { get; init; }
    public Dictionary<string, string>? NonSecretMetadata { get; init; }
    public string[]? DiagnosticRefs { get; init; }
}
