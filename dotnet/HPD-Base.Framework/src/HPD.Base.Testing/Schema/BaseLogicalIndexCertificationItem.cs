using System.Text.Json.Serialization;

namespace HPD.Base.Testing;

[BaseCollection("base.cert.logicalIndex.items.v1", typeof(BaseLogicalIndexCertificationJsonContext))]
[BaseIndex("base.cert.logicalIndex.tenantCode.v1", Unique = true, StoreRequired = true)]
[BaseIndexPart("base.cert.logicalIndex.tenantCode.v1", 0, nameof(Tenant),
    Direction = BaseIndexSortDirection.Ascending,
    Collation = BaseIndexCollation.OrdinalBinary,
    NullOrder = BaseIndexNullOrder.MissingThenNullThenValue)]
[BaseIndexPart("base.cert.logicalIndex.tenantCode.v1", 1, nameof(Code),
    Direction = BaseIndexSortDirection.Ascending,
    Collation = BaseIndexCollation.OrdinalBinary,
    NullOrder = BaseIndexNullOrder.MissingThenNullThenValue)]
[BaseIndexPredicate("base.cert.logicalIndex.tenantCode.v1", "p1",
    BaseIndexPredicateNodeKind.IsDefined, Field = nameof(Code))]
[BaseIndexPredicate("base.cert.logicalIndex.tenantCode.v1", "p2",
    BaseIndexPredicateNodeKind.IsNotNull, Field = nameof(Code))]
[BaseIndexPredicate("base.cert.logicalIndex.tenantCode.v1", "p0",
    BaseIndexPredicateNodeKind.And, Children = ["p1", "p2"])]
[BaseIndex("base.cert.logicalIndex.aTenantCode.v1", Unique = false, StoreRequired = true)]
[BaseIndexPart("base.cert.logicalIndex.aTenantCode.v1", 0, nameof(Tenant),
    Direction = BaseIndexSortDirection.Ascending,
    Collation = BaseIndexCollation.OrdinalBinary,
    NullOrder = BaseIndexNullOrder.MissingThenNullThenValue)]
[BaseIndexPart("base.cert.logicalIndex.aTenantCode.v1", 1, nameof(Code),
    Direction = BaseIndexSortDirection.Ascending,
    Collation = BaseIndexCollation.OrdinalBinary,
    NullOrder = BaseIndexNullOrder.MissingThenNullThenValue)]
[BaseIndexPredicate("base.cert.logicalIndex.aTenantCode.v1", "p1",
    BaseIndexPredicateNodeKind.IsDefined, Field = nameof(Code))]
[BaseIndexPredicate("base.cert.logicalIndex.aTenantCode.v1", "p2",
    BaseIndexPredicateNodeKind.IsNotNull, Field = nameof(Code))]
[BaseIndexPredicate("base.cert.logicalIndex.aTenantCode.v1", "p0",
    BaseIndexPredicateNodeKind.And, Children = ["p1", "p2"])]
[BaseIndex("base.cert.logicalIndex.tenantSequence.v1", Unique = false, StoreRequired = true)]
[BaseIndexPart("base.cert.logicalIndex.tenantSequence.v1", 0, nameof(Tenant),
    Direction = BaseIndexSortDirection.Descending,
    Collation = BaseIndexCollation.OrdinalBinary,
    NullOrder = BaseIndexNullOrder.MissingThenNullThenValue)]
[BaseIndexPart("base.cert.logicalIndex.tenantSequence.v1", 1, nameof(Sequence),
    Direction = BaseIndexSortDirection.Ascending,
    Collation = BaseIndexCollation.OrdinalBinary,
    NullOrder = BaseIndexNullOrder.MissingThenNullThenValue)]
[BaseIndexPredicate("base.cert.logicalIndex.tenantSequence.v1", "p0",
    BaseIndexPredicateNodeKind.True)]
internal sealed partial record BaseLogicalIndexCertificationItem
{
    [BaseField("base.cert.logicalIndex.tenant", Operators = BaseFieldOperator.Equal,
        Presence = BaseFieldPresence.Required, Nullability = BaseFieldNullability.NonNullable,
        MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 32)]
    public required string Tenant { get; init; }

    [BaseField("base.cert.logicalIndex.code", Operators = BaseFieldOperator.Equal,
        Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.Nullable,
        MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 32)]
    public string? Code { get; init; }

    [BaseField("base.cert.logicalIndex.sequence",
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order,
        Presence = BaseFieldPresence.Required, Nullability = BaseFieldNullability.NonNullable)]
    public required long Sequence { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BaseLogicalIndexCertificationItem))]
[JsonSerializable(typeof(long))]
internal sealed partial class BaseLogicalIndexCertificationJsonContext : JsonSerializerContext;
