
namespace HPD.Base;

public sealed record RouteDescriptor
{
    public required string OperationId { get; init; }
    public required HttpMethodKind Method { get; init; }
    public required string Path { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public RouteAuthRequirement AuthRequirement { get; init; } = RouteAuthRequirement.None;
    public string? RequestDtoId { get; init; }
    public required string ResponseDtoId { get; init; }
    public string? ErrorDtoId { get; init; }
    public string? ResultDtoId { get; init; }
    public string[]? RequiredFeatureIds { get; init; }
}

public enum HttpMethodKind { Get, Post, Put, Patch, Delete, Head, Options }
public enum RouteAuthRequirement { None, Public, Authenticated, Admin, Internal, HostPolicy }
