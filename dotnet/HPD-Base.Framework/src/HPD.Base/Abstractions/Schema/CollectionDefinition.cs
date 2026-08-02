using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a collection definition.</summary>
public sealed record CollectionDefinition
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the display name.</summary>
    public string? DisplayName { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets or sets the enabled.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Gets or sets the exposed.</summary>
    public bool Exposed { get; init; } = true;
    /// <summary>Gets or sets the system.</summary>
    public bool System { get; init; }
    /// <summary>Gets or sets the read only.</summary>
    public bool ReadOnly { get; init; }
    /// <summary>Gets or sets the read only reason.</summary>
    public string? ReadOnlyReason { get; init; }
    /// <summary>Gets or sets the operations.</summary>
    public CollectionOperationMatrix? Operations { get; init; }
    /// <summary>Gets or sets the schema mode.</summary>
    public required SchemaMode SchemaMode { get; init; }
    /// <summary>Gets or sets the unknown fields.</summary>
    public required UnknownFieldPolicy UnknownFields { get; init; }
    /// <summary>Gets or sets the validation mode.</summary>
    public ValidationMode ValidationMode { get; init; } = ValidationMode.Runtime;
    /// <summary>Gets or sets the source.</summary>
    public SchemaSourceDescriptor? Source { get; init; }
    /// <summary>Gets or sets the fields.</summary>
    public FieldDefinition[]? Fields { get; init; }
    /// <summary>Gets or sets the indexes.</summary>
    public IndexDefinition[]? Indexes { get; init; }
    /// <summary>Gets or sets the policy refs.</summary>
    public string[]? PolicyRefs { get; init; }
    /// <summary>Gets or sets the store.</summary>
    public StoreAnnotation? Store { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public CollectionVisibility? Visibility { get; init; }
    /// <summary>Gets or sets the required capabilities.</summary>
    public string[]? RequiredCapabilities { get; init; }
    /// <summary>Gets or sets the diagnostics.</summary>
    public DiagnosticDescriptor[]? Diagnostics { get; init; }
    /// <summary>Gets or sets the schema version.</summary>
    public string? SchemaVersion { get; init; }
    /// <summary>Gets or sets the refreshed at.</summary>
    public DateTimeOffset? RefreshedAt { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a collection operation matrix.</summary>
public sealed record CollectionOperationMatrix
{
    /// <summary>Gets or sets the list.</summary>
    public bool List { get; init; }
    /// <summary>Gets or sets the get.</summary>
    public bool Get { get; init; }
    /// <summary>Gets or sets the create.</summary>
    public bool Create { get; init; }
    /// <summary>Gets or sets the patch.</summary>
    public bool Patch { get; init; }
    /// <summary>Gets or sets the replace.</summary>
    public bool Replace { get; init; }
    /// <summary>Gets or sets the upsert.</summary>
    public bool Upsert { get; init; }
    /// <summary>Gets or sets the delete.</summary>
    public bool Delete { get; init; }
}
