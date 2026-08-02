using System.Collections.Immutable;

namespace HPD.Gateway.Abstractions;

public sealed record GatewayConfiguration
{
    public required GatewaySchemaVersion SchemaVersion { get; init; }

    public required ushort CanonicalizationVersion { get; init; }

    public ResourceMetadata Metadata { get; init; } = ResourceMetadata.Empty;

    public ImmutableArray<RouteDeclaration> Routes { get; init; } = [];

    public ImmutableArray<UpstreamDeclaration> Upstreams { get; init; } = [];

    public GatewayDefinitions? Definitions { get; init; } = new();

    public GatewayRootDeclarations? RootDefaults { get; init; } = new();
}

public sealed record ResourceMetadata
{
    public static ResourceMetadata Empty { get; } = new();

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public ImmutableArray<MetadataEntry> Labels { get; init; } = [];

    public ImmutableArray<MetadataEntry> Annotations { get; init; } = [];
}

public sealed record MetadataEntry(string Name, string Value);

public sealed record SecretReference(
    ProviderId Provider,
    ProviderObjectId Name,
    string? Version = null);
