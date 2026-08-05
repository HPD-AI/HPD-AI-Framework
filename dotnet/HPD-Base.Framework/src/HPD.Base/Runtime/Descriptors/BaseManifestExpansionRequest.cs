
namespace HPD.Base;

/// <summary>Represents a base manifest expansion request.</summary>
public sealed record BaseManifestExpansionRequest
{
    /// <summary>Gets or sets the principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets or sets the operation.</summary>
    public required OperationContext Operation { get; init; }
    /// <summary>Gets or sets the view.</summary>
    public VisibilityLevel View { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the expand.</summary>
    public string[]? Expand { get; init; }
}
