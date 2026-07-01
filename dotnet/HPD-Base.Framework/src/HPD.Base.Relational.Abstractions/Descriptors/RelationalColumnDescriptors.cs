using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Relational.Descriptors;

public sealed record RelationalColumnDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string ParentObjectRef { get; init; }
    public required string NativeName { get; init; }
    public string? NativePath { get; init; }
    public int Ordinal { get; init; }
    public required RelationalColumnTypeDescriptor Type { get; init; }
    public bool Nullable { get; init; } = true;
    public bool HasDefault { get; init; }
    public string? DefaultSummary { get; init; }
    public bool DefaultSummaryPublicSafe { get; init; }
    public string? GeneratedColumnRef { get; init; }
    public string? JsonColumnRef { get; init; }
    public string[]? MappedFieldRefs { get; init; }
    public bool System { get; init; }
    public bool Hidden { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalColumnTypeDescriptor
{
    public required string NativeTypeName { get; init; }
    public RelationalColumnTypeFamily Family { get; init; } = RelationalColumnTypeFamily.Unknown;
    public int? Length { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }
    public bool? Unicode { get; init; }
    public bool? FixedLength { get; init; }
    public string? Collation { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalGeneratedColumnDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string ColumnRef { get; init; }
    public RelationalGeneratedColumnKind Kind { get; init; } = RelationalGeneratedColumnKind.None;
    public RelationalColumnWriteBehavior WriteBehavior { get; init; } = RelationalColumnWriteBehavior.StoreGenerated;
    public string? RefreshBehaviorSummary { get; init; }
    public string? ExpressionSummary { get; init; }
    public bool ExpressionRedacted { get; init; } = true;
    public string? BaseGeneratedAnnotationRef { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalJsonColumnDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string ColumnRef { get; init; }
    public RelationalJsonStorageKind StorageKind { get; init; } = RelationalJsonStorageKind.ProviderNative;
    public bool QueryablePathsSupported { get; init; }
    public bool PathIndexSupported { get; init; }
    public string? PayloadRootFieldPath { get; init; }
    public string? NullMissingSemanticsSummary { get; init; }
    public string? SerializationSummary { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
