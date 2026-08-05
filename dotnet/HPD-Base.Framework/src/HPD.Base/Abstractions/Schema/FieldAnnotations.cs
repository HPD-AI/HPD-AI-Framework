using System.Text.Json;

namespace HPD.Base;
/// <summary>Represents cardinality Descriptor.</summary>
public sealed record CardinalityDescriptor
{
    /// <summary>Gets or sets kind.</summary>
    public FieldCardinalityKind Kind { get; init; } = FieldCardinalityKind.Single;
    /// <summary>Gets or sets min Items.</summary>
    public int? MinItems { get; init; }
    /// <summary>Gets or sets max Items.</summary>
    public int? MaxItems { get; init; }
    /// <summary>Gets or sets ordered.</summary>
    public bool Ordered { get; init; } = true;
}

/// <summary>Represents default Value Descriptor.</summary>
public sealed record DefaultValueDescriptor
{
    /// <summary>Gets or sets kind.</summary>
    public DefaultValueKind Kind { get; init; } = DefaultValueKind.None;
    /// <summary>Gets or sets value.</summary>
    public JsonElement Value { get; init; }
    /// <summary>Gets or sets public Safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets owner.</summary>
    public EnforcementOwner Owner { get; init; } = EnforcementOwner.Runtime;
}

/// <summary>Represents generation Descriptor.</summary>
public sealed record GenerationDescriptor
{
    /// <summary>Gets or sets kind.</summary>
    public GenerationKind Kind { get; init; } = GenerationKind.None;
    /// <summary>Gets or sets generator Id.</summary>
    public string? GeneratorId { get; init; }
    /// <summary>Gets or sets on Create.</summary>
    public bool OnCreate { get; init; }
    /// <summary>Gets or sets on Update.</summary>
    public bool OnUpdate { get; init; }
    /// <summary>Gets or sets owner.</summary>
    public EnforcementOwner Owner { get; init; } = EnforcementOwner.Runtime;
    /// <summary>Gets or sets public Safe.</summary>
    public bool PublicSafe { get; init; }
}

/// <summary>Represents constraint Annotations.</summary>
public sealed record ConstraintAnnotations
{
    /// <summary>Gets or sets unique.</summary>
    public bool Unique { get; init; }
    /// <summary>Gets or sets primary Key.</summary>
    public bool PrimaryKey { get; init; }
    /// <summary>Gets or sets immutable.</summary>
    public bool Immutable { get; init; }
    /// <summary>Gets or sets optimistic Concurrency Token.</summary>
    public bool OptimisticConcurrencyToken { get; init; }
    /// <summary>Gets or sets enforcement.</summary>
    public EnforcementOwner Enforcement { get; init; } = EnforcementOwner.Runtime;
    /// <summary>Gets or sets index Refs.</summary>
    public string[]? IndexRefs { get; init; }
}

/// <summary>Represents validation Annotations.</summary>
public sealed record ValidationAnnotations
{
    /// <summary>Gets or sets mode.</summary>
    public ValidationMode Mode { get; init; } = ValidationMode.Runtime;
    /// <summary>Gets or sets rules.</summary>
    public ValidationRule[]? Rules { get; init; }
    /// <summary>Gets or sets custom Validators.</summary>
    public string[]? CustomValidators { get; init; }
    /// <summary>Gets or sets diagnostics.</summary>
    public DiagnosticDescriptor[]? Diagnostics { get; init; }
}

/// <summary>Represents validation Rule.</summary>
public sealed record ValidationRule
{
    /// <summary>Gets or sets kind.</summary>
    public required ValidationRuleKind Kind { get; init; }
    /// <summary>Gets or sets value.</summary>
    public JsonElement Value { get; init; }
    /// <summary>Gets or sets message Key.</summary>
    public string? MessageKey { get; init; }
    /// <summary>Gets or sets public Message.</summary>
    public string? PublicMessage { get; init; }
    /// <summary>Gets or sets severity.</summary>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;
    /// <summary>Gets or sets applies To.</summary>
    public ValidationAppliesTo[]? AppliesTo { get; init; }
    /// <summary>Gets or sets owner.</summary>
    public EnforcementOwner Owner { get; init; } = EnforcementOwner.Runtime;
    /// <summary>Gets or sets public Safe.</summary>
    public bool PublicSafe { get; init; }
}

/// <summary>Represents relation Definition.</summary>
public sealed record RelationDefinition
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets source Collection Id.</summary>
    public required string SourceCollectionId { get; init; }
    /// <summary>Gets or sets source Field Id.</summary>
    public required string SourceFieldId { get; init; }
    /// <summary>Gets or sets target Collection Id.</summary>
    public required string TargetCollectionId { get; init; }
    /// <summary>Gets or sets target Field Id.</summary>
    public string TargetFieldId { get; init; } = "base.recordId";
    /// <summary>Gets or sets owning Side.</summary>
    public BaseRelationOwningSide OwningSide { get; init; } = BaseRelationOwningSide.Source;
    /// <summary>Gets or sets local Multiplicity.</summary>
    public BaseRelationMultiplicity LocalMultiplicity { get; init; } = BaseRelationMultiplicity.ZeroOrOne;
    /// <summary>Gets or sets inverse Multiplicity.</summary>
    public BaseRelationMultiplicity InverseMultiplicity { get; init; } = BaseRelationMultiplicity.Many;
    /// <summary>Gets or sets required.</summary>
    public bool Required { get; init; }
    /// <summary>Gets or sets ordered.</summary>
    public bool Ordered { get; init; } = true;
    /// <summary>Gets or sets minimum Count.</summary>
    public int? MinimumCount { get; init; }
    /// <summary>Gets or sets maximum Count.</summary>
    public int? MaximumCount { get; init; }
    /// <summary>Gets or sets inverse Navigation Id.</summary>
    public string? InverseNavigationId { get; init; }
    /// <summary>Gets or sets existence Enforcement.</summary>
    public EnforcementOwner ExistenceEnforcement { get; init; } = EnforcementOwner.Runtime;
    /// <summary>Gets or sets physical Enforcement.</summary>
    public EnforcementOwner PhysicalEnforcement { get; init; } = EnforcementOwner.Advisory;
    /// <summary>Gets or sets delete Behavior.</summary>
    public BaseRelationDeleteBehavior DeleteBehavior { get; init; } = BaseRelationDeleteBehavior.Restrict;
    /// <summary>Gets or sets include.</summary>
    public RelationIncludeDefinition? Include { get; init; }
    /// <summary>Gets or sets required Capabilities.</summary>
    public string[]? RequiredCapabilities { get; init; }
}

/// <summary>Represents relation Include Definition.</summary>
public sealed record RelationIncludeDefinition
{
    /// <summary>Gets or sets allowed.</summary>
    public bool Allowed { get; init; }
    /// <summary>Gets or sets default.</summary>
    public bool Default { get; init; }
    /// <summary>Gets or sets max Depth.</summary>
    public int? MaxDepth { get; init; }
    /// <summary>Gets or sets filter Allowed.</summary>
    public bool FilterAllowed { get; init; }
    /// <summary>Gets or sets sort Allowed.</summary>
    public bool SortAllowed { get; init; }
}

/// <summary>Represents file Annotation.</summary>
public sealed record FileAnnotation
{
    /// <summary>Gets or sets reference Shape.</summary>
    public FileReferenceShape ReferenceShape { get; init; } = FileReferenceShape.ObjectRef;
    /// <summary>Gets or sets max Bytes.</summary>
    public long? MaxBytes { get; init; }
    /// <summary>Gets or sets max Count.</summary>
    public int? MaxCount { get; init; }
    /// <summary>Gets or sets mime Types.</summary>
    public string[]? MimeTypes { get; init; }
    /// <summary>Gets or sets protected.</summary>
    public bool Protected { get; init; }
    /// <summary>Gets or sets bucket Id.</summary>
    public string? BucketId { get; init; }
    /// <summary>Gets or sets storage Module Id.</summary>
    public string? StorageModuleId { get; init; }
    /// <summary>Gets or sets thumbnail Profiles.</summary>
    public string[]? ThumbnailProfiles { get; init; }
    /// <summary>Gets or sets cleanup Policy.</summary>
    public FileCleanupPolicy CleanupPolicy { get; init; } = FileCleanupPolicy.Advisory;
    /// <summary>Gets or sets required Capabilities.</summary>
    public string[]? RequiredCapabilities { get; init; }
}

/// <summary>Represents collection Visibility.</summary>
public sealed record CollectionVisibility
{
    /// <summary>Gets or sets visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets public List.</summary>
    public bool PublicList { get; init; } = true;
    /// <summary>Gets or sets public Schema.</summary>
    public bool PublicSchema { get; init; } = true;
    /// <summary>Gets or sets admin Only.</summary>
    public bool AdminOnly { get; init; }
}

/// <summary>Represents field Visibility Annotation.</summary>
public sealed record FieldVisibilityAnnotation
{
    /// <summary>Gets or sets visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets hidden In List.</summary>
    public bool HiddenInList { get; init; }
    /// <summary>Gets or sets hidden In Detail.</summary>
    public bool HiddenInDetail { get; init; }
    /// <summary>Gets or sets hidden In Create.</summary>
    public bool HiddenInCreate { get; init; }
    /// <summary>Gets or sets hidden In Update.</summary>
    public bool HiddenInUpdate { get; init; }
    /// <summary>Gets or sets write Only.</summary>
    public bool WriteOnly { get; init; }
    /// <summary>Gets or sets admin Only.</summary>
    public bool AdminOnly { get; init; }
}

/// <summary>Represents ui Annotation.</summary>
public sealed record UiAnnotation
{
    /// <summary>Gets or sets label.</summary>
    public string? Label { get; init; }
    /// <summary>Gets or sets help Text.</summary>
    public string? HelpText { get; init; }
    /// <summary>Gets or sets input Kind.</summary>
    public string? InputKind { get; init; }
    /// <summary>Gets or sets order.</summary>
    public int? Order { get; init; }
    /// <summary>Gets or sets presentable.</summary>
    public bool Presentable { get; init; }
}

/// <summary>Represents sdk Annotation.</summary>
public sealed record SdkAnnotation
{
    /// <summary>Gets or sets property Name.</summary>
    public string? PropertyName { get; init; }
    /// <summary>Gets or sets type Name.</summary>
    public string? TypeName { get; init; }
    /// <summary>Gets or sets optional Override.</summary>
    public bool OptionalOverride { get; init; }
    /// <summary>Gets or sets read Only Override.</summary>
    public bool ReadOnlyOverride { get; init; }
    /// <summary>Gets or sets loose Index Signature.</summary>
    public bool LooseIndexSignature { get; init; }
}

/// <summary>Represents store Annotation.</summary>
public sealed record StoreAnnotation
{
    /// <summary>Gets or sets store Id.</summary>
    public string? StoreId { get; init; }
    /// <summary>Gets or sets native Namespace.</summary>
    public string? NativeNamespace { get; init; }
    /// <summary>Gets or sets native Name.</summary>
    public string? NativeName { get; init; }
    /// <summary>Gets or sets native Path.</summary>
    public string? NativePath { get; init; }
    /// <summary>Gets or sets native Type.</summary>
    public string? NativeType { get; init; }
    /// <summary>Gets or sets owner.</summary>
    public EnforcementOwner Owner { get; init; } = EnforcementOwner.Store;
    /// <summary>Gets or sets public Safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
