using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace HPD.Gateway;

public sealed record RouteDeclaration
{
    public required RouteId Id { get; init; }

    public bool Enabled { get; init; } = true;

    public ListenerId? Listener { get; init; }

    public required HttpRouteMatch Match { get; init; }

    public int? Order { get; init; }

    public required UpstreamId Upstream { get; init; }

    public RouteDeclarations? Declarations { get; init; } = new();

    public ResourceMetadata Metadata { get; init; } = ResourceMetadata.Empty;
}

public sealed record HttpRouteMatch
{
    public ImmutableArray<string> Methods { get; init; } = [];

    public ImmutableArray<string> Hosts { get; init; } = [];

    public string? Path { get; init; }

    public ImmutableArray<HttpHeaderMatch> Headers { get; init; } = [];

    public ImmutableArray<HttpQueryMatch> Query { get; init; } = [];
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<TextMatchKind>))]
public enum TextMatchKind
{
    Exact = 0,
    Prefix = 1,
    Contains = 2,
    Exists = 3,
    NotExists = 4
}

public sealed record HttpHeaderMatch
{
    public required string Name { get; init; }

    public required TextMatchKind Kind { get; init; }

    public ImmutableArray<string> Values { get; init; } = [];

    public bool CaseSensitive { get; init; }
}

public sealed record HttpQueryMatch
{
    public required string Name { get; init; }

    public required TextMatchKind Kind { get; init; }

    public ImmutableArray<string> Values { get; init; } = [];

    public bool CaseSensitive { get; init; }
}
