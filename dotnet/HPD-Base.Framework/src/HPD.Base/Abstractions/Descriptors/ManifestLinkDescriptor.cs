
namespace HPD.Base;

/// <summary>Represents a manifest link descriptor.</summary>
public sealed record ManifestLinkDescriptor
{
    /// <summary>Gets or sets the rel.</summary>
    public required ManifestLinkKind Rel { get; init; }
    /// <summary>Gets or sets the href.</summary>
    public required string Href { get; init; }
    /// <summary>Gets or sets the method.</summary>
    public HttpMethodKind Method { get; init; } = HttpMethodKind.Get;
    /// <summary>Gets or sets the response DTO ID.</summary>
    public required string ResponseDtoId { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the required feature IDs.</summary>
    public string[]? RequiredFeatureIds { get; init; }
}

/// <summary>Defines the manifest link kind contract.</summary>
public enum ManifestLinkKind
{
    /// <summary>Identifies manifest.</summary>
Manifest,
    /// <summary>Identifies capabilities.</summary>
Capabilities,
    /// <summary>Identifies schema.</summary>
Schema,
    /// <summary>Identifies health.</summary>
Health,
    /// <summary>Identifies diagnostics.</summary>
Diagnostics,
    /// <summary>Identifies collection.</summary>
Collection,
    /// <summary>Identifies records.</summary>
Records,
    /// <summary>Identifies custom.</summary>
Custom
}
