using System.Text.Json;

namespace HPD.Base;

public sealed record FieldDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public required string Type { get; init; }
    public string? Format { get; init; }
    public CardinalityDescriptor? Cardinality { get; init; }
    public bool Required { get; init; }
    public bool Nullable { get; init; } = true;
    public bool System { get; init; }
    public bool Hidden { get; init; }
    public bool ReadOnly { get; init; }
    public DefaultValueDescriptor? Default { get; init; }
    public GenerationDescriptor? Generated { get; init; }
    public ConstraintAnnotations? Constraints { get; init; }
    public ValidationAnnotations? Validation { get; init; }
    public RelationAnnotation? Relation { get; init; }
    public FileAnnotation? File { get; init; }
    public FieldVisibilityAnnotation? Visibility { get; init; }
    public UiAnnotation? Ui { get; init; }
    public SdkAnnotation? Sdk { get; init; }
    public StoreAnnotation? Store { get; init; }
    public string[]? RequiredCapabilities { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
