
namespace HPD.Base;

/// <summary>Represents a base manifest request.</summary>
public sealed record BaseManifestRequest
{
    /// <summary>Gets or sets the principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets or sets the operation.</summary>
    public required OperationContext Operation { get; init; }
    /// <summary>Gets or sets the view.</summary>
    public VisibilityLevel View { get; init; } = VisibilityLevel.Public;
}
