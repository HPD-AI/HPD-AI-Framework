using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Descriptors;

public sealed record BaseManifestExpansionRequest
{
    public required PrincipalContext Principal { get; init; }
    public required OperationContext Operation { get; init; }
    public VisibilityLevel View { get; init; } = VisibilityLevel.Public;
    public string[]? Expand { get; init; }
}
