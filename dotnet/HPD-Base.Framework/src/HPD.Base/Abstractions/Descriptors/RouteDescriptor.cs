
namespace HPD.Base;

/// <summary>Represents a route descriptor.</summary>
public sealed record RouteDescriptor
{
    /// <summary>Gets or sets the operation ID.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets or sets the method.</summary>
    public required HttpMethodKind Method { get; init; }
    /// <summary>Gets or sets the path.</summary>
    public required string Path { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the auth requirement.</summary>
    public RouteAuthRequirement AuthRequirement { get; init; } = RouteAuthRequirement.None;
    /// <summary>Gets or sets the request DTO ID.</summary>
    public string? RequestDtoId { get; init; }
    /// <summary>Gets or sets the response DTO ID.</summary>
    public required string ResponseDtoId { get; init; }
    /// <summary>Gets or sets the error DTO ID.</summary>
    public string? ErrorDtoId { get; init; }
    /// <summary>Gets or sets the result DTO ID.</summary>
    public string? ResultDtoId { get; init; }
    /// <summary>Gets or sets the required feature IDs.</summary>
    public string[]? RequiredFeatureIds { get; init; }
}

/// <summary>Defines the HTTP method kind contract.</summary>
public enum HttpMethodKind { /// <summary>Identifies get.</summary>
Get, /// <summary>Identifies post.</summary>
Post, /// <summary>Identifies put.</summary>
Put, /// <summary>Identifies patch.</summary>
Patch, /// <summary>Identifies delete.</summary>
Delete, /// <summary>Identifies head.</summary>
Head, /// <summary>Identifies options.</summary>
Options }
/// <summary>Defines the route auth requirement contract.</summary>
public enum RouteAuthRequirement { /// <summary>Identifies none.</summary>
None, /// <summary>Identifies public.</summary>
Public, /// <summary>Identifies authenticated.</summary>
Authenticated, /// <summary>Identifies admin.</summary>
Admin, /// <summary>Identifies internal.</summary>
Internal, /// <summary>Identifies host policy.</summary>
HostPolicy }
