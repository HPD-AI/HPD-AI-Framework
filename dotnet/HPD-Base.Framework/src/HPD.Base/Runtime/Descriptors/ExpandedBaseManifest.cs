
namespace HPD.Base;

/// <summary>Represents a expanded base manifest.</summary>
public sealed record ExpandedBaseManifest
{
    /// <summary>Gets or sets the manifest.</summary>
    public required BaseManifest Manifest { get; init; }
    /// <summary>Gets or sets the schema.</summary>
    public SchemaMetadata? Schema { get; init; }
    /// <summary>Gets or sets the capabilities.</summary>
    public CapabilityDescriptor? Capabilities { get; init; }
    /// <summary>Gets or sets the health.</summary>
    public HealthDescriptor[]? Health { get; init; }
    /// <summary>Gets or sets the diagnostics.</summary>
    public DiagnosticDescriptor[]? Diagnostics { get; init; }
    /// <summary>Gets or sets the collections.</summary>
    public CollectionDefinition[]? Collections { get; init; }
    /// <summary>Gets or sets the etag.</summary>
    public string? ETag { get; init; }
}
