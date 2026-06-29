using System.Text.Json;
using HPD.Base.Health;

namespace HPD.Base.Schema;

public sealed record CollectionDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public required string Kind { get; init; }
    public bool Enabled { get; init; } = true;
    public bool Exposed { get; init; } = true;
    public bool System { get; init; }
    public bool ReadOnly { get; init; }
    public string? ReadOnlyReason { get; init; }
    public CollectionOperationMatrix? Operations { get; init; }
    public required SchemaMode SchemaMode { get; init; }
    public required UnknownFieldPolicy UnknownFields { get; init; }
    public ValidationMode ValidationMode { get; init; } = ValidationMode.Runtime;
    public SchemaSourceDescriptor? Source { get; init; }
    public FieldDefinition[]? Fields { get; init; }
    public IndexDefinition[]? Indexes { get; init; }
    public string[]? PolicyRefs { get; init; }
    public StoreAnnotation? Store { get; init; }
    public CollectionVisibility? Visibility { get; init; }
    public string[]? RequiredCapabilities { get; init; }
    public DiagnosticDescriptor[]? Diagnostics { get; init; }
    public string? SchemaVersion { get; init; }
    public DateTimeOffset? RefreshedAt { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record CollectionOperationMatrix
{
    public bool List { get; init; }
    public bool Get { get; init; }
    public bool Create { get; init; }
    public bool Patch { get; init; }
    public bool Replace { get; init; }
    public bool Upsert { get; init; }
    public bool Delete { get; init; }
    public bool Batch { get; init; }
}
