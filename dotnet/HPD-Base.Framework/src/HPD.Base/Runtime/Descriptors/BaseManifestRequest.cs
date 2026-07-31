
namespace HPD.Base;

public sealed record BaseManifestRequest
{
    public required PrincipalContext Principal { get; init; }
    public required OperationContext Operation { get; init; }
    public VisibilityLevel View { get; init; } = VisibilityLevel.Public;
}
