
namespace HPD.Base;

public sealed record ManifestLinkDescriptor
{
    public required ManifestLinkKind Rel { get; init; }
    public required string Href { get; init; }
    public HttpMethodKind Method { get; init; } = HttpMethodKind.Get;
    public required string ResponseDtoId { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public string[]? RequiredFeatureIds { get; init; }
}

public enum ManifestLinkKind
{
    Manifest,
    Capabilities,
    Schema,
    Health,
    Diagnostics,
    Collection,
    Records,
    Custom
}
