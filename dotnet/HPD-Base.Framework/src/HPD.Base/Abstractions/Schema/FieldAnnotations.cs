using System.Text.Json;
using HPD.Base;
using HPD.Base.Health;

namespace HPD.Base.Schema;

public sealed record CardinalityDescriptor
{
    public FieldCardinalityKind Kind { get; init; } = FieldCardinalityKind.Single;
    public int? MinItems { get; init; }
    public int? MaxItems { get; init; }
    public bool Ordered { get; init; } = true;
}

public sealed record DefaultValueDescriptor
{
    public DefaultValueKind Kind { get; init; } = DefaultValueKind.None;
    public JsonElement Value { get; init; }
    public bool PublicSafe { get; init; }
    public EnforcementOwner Owner { get; init; } = EnforcementOwner.Runtime;
}

public sealed record GenerationDescriptor
{
    public GenerationKind Kind { get; init; } = GenerationKind.None;
    public string? GeneratorId { get; init; }
    public bool OnCreate { get; init; }
    public bool OnUpdate { get; init; }
    public EnforcementOwner Owner { get; init; } = EnforcementOwner.Runtime;
    public bool PublicSafe { get; init; }
}

public sealed record ConstraintAnnotations
{
    public bool Unique { get; init; }
    public bool PrimaryKey { get; init; }
    public bool Immutable { get; init; }
    public bool OptimisticConcurrencyToken { get; init; }
    public EnforcementOwner Enforcement { get; init; } = EnforcementOwner.Runtime;
    public string[]? IndexRefs { get; init; }
}

public sealed record ValidationAnnotations
{
    public ValidationMode Mode { get; init; } = ValidationMode.Runtime;
    public ValidationRule[]? Rules { get; init; }
    public string[]? CustomValidators { get; init; }
    public DiagnosticDescriptor[]? Diagnostics { get; init; }
}

public sealed record ValidationRule
{
    public required ValidationRuleKind Kind { get; init; }
    public JsonElement Value { get; init; }
    public string? MessageKey { get; init; }
    public string? PublicMessage { get; init; }
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;
    public ValidationAppliesTo[]? AppliesTo { get; init; }
    public EnforcementOwner Owner { get; init; } = EnforcementOwner.Runtime;
    public bool PublicSafe { get; init; }
}

public sealed record RelationAnnotation
{
    public required string TargetCollectionId { get; init; }
    public string? TargetFieldPath { get; init; }
    public string? LocalFieldPath { get; init; }
    public RelationKind Kind { get; init; } = RelationKind.Reference;
    public RelationCardinality Cardinality { get; init; } = RelationCardinality.ZeroOrOne;
    public DeleteBehavior DeleteBehavior { get; init; } = DeleteBehavior.None;
    public RelationIncludeAnnotation? Include { get; init; }
    public EnforcementOwner Enforcement { get; init; } = EnforcementOwner.Advisory;
    public string[]? RequiredCapabilities { get; init; }
}

public sealed record RelationIncludeAnnotation
{
    public bool Allowed { get; init; }
    public bool Default { get; init; }
    public int? MaxDepth { get; init; }
    public bool FilterAllowed { get; init; }
    public bool SortAllowed { get; init; }
}

public sealed record FileAnnotation
{
    public FileReferenceShape ReferenceShape { get; init; } = FileReferenceShape.ObjectRef;
    public long? MaxBytes { get; init; }
    public int? MaxCount { get; init; }
    public string[]? MimeTypes { get; init; }
    public bool Protected { get; init; }
    public string? BucketId { get; init; }
    public string? StorageModuleId { get; init; }
    public string[]? ThumbnailProfiles { get; init; }
    public FileCleanupPolicy CleanupPolicy { get; init; } = FileCleanupPolicy.Advisory;
    public string[]? RequiredCapabilities { get; init; }
}

public sealed record CollectionVisibility
{
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicList { get; init; } = true;
    public bool PublicSchema { get; init; } = true;
    public bool AdminOnly { get; init; }
}

public sealed record FieldVisibilityAnnotation
{
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool HiddenInList { get; init; }
    public bool HiddenInDetail { get; init; }
    public bool HiddenInCreate { get; init; }
    public bool HiddenInUpdate { get; init; }
    public bool WriteOnly { get; init; }
    public bool AdminOnly { get; init; }
}

public sealed record UiAnnotation
{
    public string? Label { get; init; }
    public string? HelpText { get; init; }
    public string? InputKind { get; init; }
    public int? Order { get; init; }
    public bool Presentable { get; init; }
}

public sealed record SdkAnnotation
{
    public string? PropertyName { get; init; }
    public string? TypeName { get; init; }
    public bool OptionalOverride { get; init; }
    public bool ReadOnlyOverride { get; init; }
    public bool LooseIndexSignature { get; init; }
}

public sealed record StoreAnnotation
{
    public string? StoreId { get; init; }
    public string? NativeNamespace { get; init; }
    public string? NativeName { get; init; }
    public string? NativePath { get; init; }
    public string? NativeType { get; init; }
    public EnforcementOwner Owner { get; init; } = EnforcementOwner.Store;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
