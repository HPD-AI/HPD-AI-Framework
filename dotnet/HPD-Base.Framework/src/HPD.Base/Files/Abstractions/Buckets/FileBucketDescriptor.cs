
namespace HPD.Base;

/// <summary>Represents a file bucket descriptor.</summary>
public sealed record FileBucketDescriptor
{
    /// <summary>Gets or sets the bucket ID.</summary>
    public required FileBucketId BucketId { get; init; }
    /// <summary>Gets or sets the display name.</summary>
    public string? DisplayName { get; init; }
    /// <summary>Gets or sets the enabled.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Gets or sets the visibility.</summary>
    public FileBucketVisibility Visibility { get; init; } = FileBucketVisibility.Private;
    /// <summary>Gets or sets the max object bytes.</summary>
    public long? MaxObjectBytes { get; init; }
    /// <summary>Gets or sets the allowed content types.</summary>
    public string[]? AllowedContentTypes { get; init; }
    /// <summary>Gets or sets the allowed extensions.</summary>
    public string[]? AllowedExtensions { get; init; }
    /// <summary>Gets or sets the require checksum.</summary>
    public bool RequireChecksum { get; init; }
    /// <summary>Gets or sets the allow overwrite.</summary>
    public bool AllowOverwrite { get; init; }
    /// <summary>Gets or sets the default cache policy.</summary>
    public string? DefaultCachePolicy { get; init; }
    /// <summary>Gets or sets the policy refs.</summary>
    public string[]? PolicyRefs { get; init; }
    /// <summary>Gets or sets the provider ref.</summary>
    public FileProviderRef? ProviderRef { get; init; }
    /// <summary>Gets or sets the capabilities.</summary>
    public FileBucketCapabilities? Capabilities { get; init; }
    /// <summary>Gets or sets the public safe metadata.</summary>
    public Dictionary<string, string>? PublicSafeMetadata { get; init; }
    /// <summary>Gets or sets the admin config summary.</summary>
    public FileBucketAdminConfigSummary? AdminConfigSummary { get; init; }
    /// <summary>Gets or sets the health ref.</summary>
    public string? HealthRef { get; init; }
    /// <summary>Gets or sets the diagnostic refs.</summary>
    public string[]? DiagnosticRefs { get; init; }
    /// <summary>Gets or sets the descriptor visibility.</summary>
    public VisibilityLevel DescriptorVisibility { get; init; } = VisibilityLevel.Admin;
}

/// <summary>Defines the file bucket visibility contract.</summary>
public enum FileBucketVisibility
{
    /// <summary>Identifies private.</summary>
Private,
    /// <summary>Identifies public read.</summary>
PublicRead,
    /// <summary>Identifies admin only.</summary>
AdminOnly,
    /// <summary>Identifies custom.</summary>
Custom
}

/// <summary>Represents a file bucket capabilities.</summary>
public sealed record FileBucketCapabilities
{
    /// <summary>Gets or sets the upload.</summary>
    public bool Upload { get; init; }
    /// <summary>Gets or sets the download.</summary>
    public bool Download { get; init; }
    /// <summary>Gets or sets the metadata.</summary>
    public bool Metadata { get; init; } = true;
    /// <summary>Gets or sets the delete.</summary>
    public bool Delete { get; init; }
    /// <summary>Gets or sets the list.</summary>
    public bool List { get; init; }
}

/// <summary>Represents a file bucket admin config summary.</summary>
public sealed record FileBucketAdminConfigSummary
{
    /// <summary>Gets or sets the provider ref.</summary>
    public FileProviderRef? ProviderRef { get; init; }
    /// <summary>Gets or sets the storage class summary.</summary>
    public string? StorageClassSummary { get; init; }
    /// <summary>Gets or sets the capability flags.</summary>
    public string[]? CapabilityFlags { get; init; }
    /// <summary>Gets or sets the non secret metadata.</summary>
    public Dictionary<string, string>? NonSecretMetadata { get; init; }
    /// <summary>Gets or sets the diagnostic refs.</summary>
    public string[]? DiagnosticRefs { get; init; }
}
