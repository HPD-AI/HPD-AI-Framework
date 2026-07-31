
namespace HPD.Base;

public sealed record ExpandedBaseManifest
{
    public required BaseManifest Manifest { get; init; }
    public SchemaMetadata? Schema { get; init; }
    public CapabilityDescriptor? Capabilities { get; init; }
    public HealthDescriptor[]? Health { get; init; }
    public DiagnosticDescriptor[]? Diagnostics { get; init; }
    public CollectionDefinition[]? Collections { get; init; }
    public string? ETag { get; init; }
}
