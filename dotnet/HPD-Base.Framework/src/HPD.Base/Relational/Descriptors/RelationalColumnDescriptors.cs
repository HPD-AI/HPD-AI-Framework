using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a relational column descriptor.</summary>
public sealed record RelationalColumnDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the parent object ref.</summary>
    public required string ParentObjectRef { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public required string NativeName { get; init; }
    /// <summary>Gets or sets the native path.</summary>
    public string? NativePath { get; init; }
    /// <summary>Gets or sets the ordinal.</summary>
    public int Ordinal { get; init; }
    /// <summary>Gets or sets the type.</summary>
    public required RelationalColumnTypeDescriptor Type { get; init; }
    /// <summary>Gets or sets the nullable.</summary>
    public bool Nullable { get; init; } = true;
    /// <summary>Gets or sets the has default.</summary>
    public bool HasDefault { get; init; }
    /// <summary>Gets or sets the default summary.</summary>
    public string? DefaultSummary { get; init; }
    /// <summary>Gets or sets the default summary public safe.</summary>
    public bool DefaultSummaryPublicSafe { get; init; }
    /// <summary>Gets or sets the generated column ref.</summary>
    public string? GeneratedColumnRef { get; init; }
    /// <summary>Gets or sets the JSON column ref.</summary>
    public string? JsonColumnRef { get; init; }
    /// <summary>Gets or sets the mapped field refs.</summary>
    public string[]? MappedFieldRefs { get; init; }
    /// <summary>Gets or sets the system.</summary>
    public bool System { get; init; }
    /// <summary>Gets or sets the hidden.</summary>
    public bool Hidden { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational column type descriptor.</summary>
public sealed record RelationalColumnTypeDescriptor
{
    /// <summary>Gets or sets the native type name.</summary>
    public required string NativeTypeName { get; init; }
    /// <summary>Gets or sets the family.</summary>
    public RelationalColumnTypeFamily Family { get; init; } = RelationalColumnTypeFamily.Unknown;
    /// <summary>Gets or sets the length.</summary>
    public int? Length { get; init; }
    /// <summary>Gets or sets the precision.</summary>
    public int? Precision { get; init; }
    /// <summary>Gets or sets the scale.</summary>
    public int? Scale { get; init; }
    /// <summary>Gets or sets the unicode.</summary>
    public bool? Unicode { get; init; }
    /// <summary>Gets or sets the fixed length.</summary>
    public bool? FixedLength { get; init; }
    /// <summary>Gets or sets the collation.</summary>
    public string? Collation { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational generated column descriptor.</summary>
public sealed record RelationalGeneratedColumnDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the column ref.</summary>
    public required string ColumnRef { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public RelationalGeneratedColumnKind Kind { get; init; } = RelationalGeneratedColumnKind.None;
    /// <summary>Gets or sets the write behavior.</summary>
    public RelationalColumnWriteBehavior WriteBehavior { get; init; } = RelationalColumnWriteBehavior.StoreGenerated;
    /// <summary>Gets or sets the refresh behavior summary.</summary>
    public string? RefreshBehaviorSummary { get; init; }
    /// <summary>Gets or sets the expression summary.</summary>
    public string? ExpressionSummary { get; init; }
    /// <summary>Gets or sets the expression redacted.</summary>
    public bool ExpressionRedacted { get; init; } = true;
    /// <summary>Gets or sets the base generated annotation ref.</summary>
    public string? BaseGeneratedAnnotationRef { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational JSON column descriptor.</summary>
public sealed record RelationalJsonColumnDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the column ref.</summary>
    public required string ColumnRef { get; init; }
    /// <summary>Gets or sets the storage kind.</summary>
    public RelationalJsonStorageKind StorageKind { get; init; } = RelationalJsonStorageKind.ProviderNative;
    /// <summary>Gets or sets the queryable paths supported.</summary>
    public bool QueryablePathsSupported { get; init; }
    /// <summary>Gets or sets the path index supported.</summary>
    public bool PathIndexSupported { get; init; }
    /// <summary>Gets or sets the payload root field path.</summary>
    public string? PayloadRootFieldPath { get; init; }
    /// <summary>Gets or sets the null missing semantics summary.</summary>
    public string? NullMissingSemanticsSummary { get; init; }
    /// <summary>Gets or sets the serialization summary.</summary>
    public string? SerializationSummary { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
