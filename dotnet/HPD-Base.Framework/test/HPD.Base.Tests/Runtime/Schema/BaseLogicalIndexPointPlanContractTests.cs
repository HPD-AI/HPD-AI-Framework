using System.Collections.Immutable;

namespace HPD.Base.Tests.Schema;

public sealed class BaseLogicalIndexPointPlanContractTests
{
    [Fact]
    public void Derivation_is_independent_of_index_declaration_order()
    {
        BaseLogicalIndexDefinition lower = Index("items.a-by-tenant-code", 0x11);
        BaseLogicalIndexDefinition higher = Index("items.z-by-tenant-code", 0x22);
        CollectionDefinition collection = Collection([higher, lower]);
        FilterExpression query = And(Equal("a-tenant", "a"), Equal("b-code", "x"));

        BaseLogicalIndexPointSelection? first = BaseLogicalIndexPointPlanContract.Derive(collection, query);
        BaseLogicalIndexPointSelection? reversed = BaseLogicalIndexPointPlanContract.Derive(
            collection with { Indexes = [lower, higher] }, query);

        Assert.NotNull(first);
        Assert.NotNull(reversed);
        Assert.Equal(lower.Id, first.IndexId);
        Assert.Equal(lower.Id, reversed.IndexId);
        Assert.Equal(first.EqualityKey.ToArray(), reversed.EqualityKey.ToArray());
        Assert.Equal(first.PredicateConjunctChecksum.ToArray(), reversed.PredicateConjunctChecksum.ToArray());
        Assert.Equal(["a", "x"], first.EqualityParts.Select(static value => value.String));
    }

    [Fact]
    public void Unsupported_or_contradictory_filters_do_not_claim_point_authority()
    {
        CollectionDefinition collection = Collection([Index("items.by-tenant-code", 0x11)]);

        Assert.Null(BaseLogicalIndexPointPlanContract.Derive(collection,
            new FilterExpression
            {
                Kind = FilterNodeKind.Compare,
                Field = "a-tenant",
                Operator = FilterOperator.GreaterThan,
                Value = String("a"),
            }));
        Assert.Null(BaseLogicalIndexPointPlanContract.Derive(collection,
            And(Equal("a-tenant", "a"), Equal("a-tenant", "b"), Equal("b-code", "x"))));
        Assert.Null(BaseLogicalIndexPointPlanContract.Derive(collection,
            And(Equal("a-tenant", "a"), new FilterExpression
            {
                Kind = FilterNodeKind.Or,
                Children = [Equal("b-code", "x"), Equal("b-code", "y")],
            })));
        Assert.Null(BaseLogicalIndexPointPlanContract.Derive(collection,
            And(Equal("a-tenant", "a"), new FilterExpression
            {
                Kind = FilterNodeKind.Compare,
                Field = "b-code",
                Operator = FilterOperator.Equal,
                Value = new QueryValue { Kind = QueryValueKind.Null },
            })));
    }

    [Fact]
    public void Membership_predicate_must_be_proven_by_query_conjuncts()
    {
        BaseLogicalIndexDefinition index = Index("items.by-tenant-code", 0x11) with
        {
            MembershipPredicate = new BaseIndexPredicateRegistry
            {
                Root = BaseIndexPredicateId.Create("p0"),
                Nodes =
                [
                    new BaseIndexPredicateNode
                    {
                        Id = BaseIndexPredicateId.Create("p0"),
                        Kind = BaseIndexPredicateNodeKind.IsNotNull,
                        FieldOrdinal = 1,
                    },
                ],
                Checksum = BaseSchemaAuthorityChecksum.Create(Enumerable.Repeat((byte)0x33, 32).ToArray()),
            },
        };
        CollectionDefinition collection = Collection([index]);

        Assert.NotNull(BaseLogicalIndexPointPlanContract.Derive(collection,
            And(Equal("a-tenant", "a"), Equal("b-code", "x"))));
        Assert.Null(BaseLogicalIndexPointPlanContract.Derive(collection,
            And(Equal("a-tenant", "a"), IsNull("b-code"))));
    }

    private static CollectionDefinition Collection(BaseLogicalIndexDefinition[] indexes) => new()
    {
        Id = "items",
        Name = "items",
        Kind = "record",
        SchemaMode = SchemaMode.Strict,
        UnknownFields = UnknownFieldPolicy.Reject,
        Fields =
        [
            Field("a-tenant", "tenant", BaseFieldNullability.NonNullable),
            Field("b-code", "code", BaseFieldNullability.Nullable),
        ],
        Indexes = indexes,
    };

    private static BaseLogicalIndexDefinition Index(string id, byte checksum) => new()
    {
        Id = BaseLogicalIndexId.Create(id),
        Version = 1,
        CollectionId = "items",
        Parts =
        [
            new BaseLogicalIndexPart
            {
                FieldOrdinal = 0,
                Direction = BaseIndexSortDirection.Ascending,
                Collation = BaseIndexCollation.OrdinalBinary,
                NullOrder = BaseIndexNullOrder.MissingThenNullThenValue,
            },
            new BaseLogicalIndexPart
            {
                FieldOrdinal = 1,
                Direction = BaseIndexSortDirection.Ascending,
                Collation = BaseIndexCollation.OrdinalBinary,
                NullOrder = BaseIndexNullOrder.MissingThenNullThenValue,
            },
        ],
        Unique = false,
        StoreRequired = true,
        MembershipPredicate = new BaseIndexPredicateRegistry
        {
            Root = BaseIndexPredicateId.Create("p0"),
            Nodes = [new BaseIndexPredicateNode
            {
                Id = BaseIndexPredicateId.Create("p0"),
                Kind = BaseIndexPredicateNodeKind.True,
            }],
            Checksum = BaseSchemaAuthorityChecksum.Create(Enumerable.Repeat((byte)0x44, 32).ToArray()),
        },
        Checksum = BaseLogicalIndexChecksum.Create(Enumerable.Repeat(checksum, 32).ToArray()),
    };

    private static FieldDefinition Field(string id, string wireName, BaseFieldNullability nullability) => new()
    {
        Id = id,
        ApplicationName = wireName,
        WireName = wireName,
        Type = "string",
        Nullability = nullability,
        ScalarKind = BaseScalarKind.String,
        ScalarCodec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.String),
        ScalarConstraints = new BaseScalarConstraintSet(),
    };

    private static FilterExpression And(params FilterExpression[] children) => new()
    {
        Kind = FilterNodeKind.And,
        Children = children,
    };

    private static FilterExpression Equal(string field, string value) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = field,
        Operator = FilterOperator.Equal,
        Value = String(value),
    };

    private static FilterExpression IsNull(string field) => new()
    {
        Kind = FilterNodeKind.IsNull,
        Field = field,
    };

    private static QueryValue String(string value) => new()
    {
        Kind = QueryValueKind.String,
        String = value,
    };
}
