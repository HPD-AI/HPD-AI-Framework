using System.Text.Json;

namespace HPD.Base;
/// <summary>Represents field Definition.</summary>
public sealed record FieldDefinition
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets display Name.</summary>
    public string? DisplayName { get; init; }
    /// <summary>Gets or sets type.</summary>
    public required string Type { get; init; }
    /// <summary>Gets or sets format.</summary>
    public string? Format { get; init; }
    /// <summary>Gets or sets cardinality.</summary>
    public CardinalityDescriptor? Cardinality { get; init; }
    /// <summary>Gets or sets required.</summary>
    public bool Required { get; init; }
    /// <summary>Gets or sets nullable.</summary>
    public bool Nullable { get; init; } = true;
    /// <summary>Gets or sets system.</summary>
    public bool System { get; init; }
    /// <summary>Gets or sets hidden.</summary>
    public bool Hidden { get; init; }
    /// <summary>Gets or sets read Only.</summary>
    public bool ReadOnly { get; init; }
    /// <summary>Gets or sets default.</summary>
    public DefaultValueDescriptor? Default { get; init; }
    /// <summary>Gets or sets generated.</summary>
    public GenerationDescriptor? Generated { get; init; }
    /// <summary>Gets or sets constraints.</summary>
    public ConstraintAnnotations? Constraints { get; init; }
    /// <summary>Gets or sets validation.</summary>
    public ValidationAnnotations? Validation { get; init; }
    /// <summary>Gets or sets relation.</summary>
    public RelationDefinition? Relation { get; init; }
    /// <summary>Gets or sets file.</summary>
    public FileAnnotation? File { get; init; }
    /// <summary>Gets or sets visibility.</summary>
    public FieldVisibilityAnnotation? Visibility { get; init; }
    /// <summary>Gets or sets ui.</summary>
    public UiAnnotation? Ui { get; init; }
    /// <summary>Gets or sets sdk.</summary>
    public SdkAnnotation? Sdk { get; init; }
    /// <summary>Gets or sets store.</summary>
    public StoreAnnotation? Store { get; init; }
    /// <summary>Gets or sets required Capabilities.</summary>
    public string[]? RequiredCapabilities { get; init; }
    /// <summary>Gets or sets extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
