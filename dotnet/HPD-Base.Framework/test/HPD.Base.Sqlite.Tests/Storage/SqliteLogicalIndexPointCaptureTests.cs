using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteLogicalIndexPointCaptureTests
{
    private static readonly RecordMutationExecutionRequest Execution = new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(1),
        TransactionTimeout = TimeSpan.FromSeconds(1),
        CommitCompletionTimeout = TimeSpan.FromSeconds(1),
    };

    [Fact]
    public async Task Empty_required_index_returns_canonical_point_miss_evidence()
    {
        CollectionDefinition collection = Collection();
        await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions
        {
            StoreId = "sqlite-logical-index-point",
            Collections = [collection],
        });
        BaseAtomicExecutionRequest request = await PointRequestAsync(store, collection, "x");
        var probe = new CaptureProbe(request);

        RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(probe, Execution);

        Assert.Equal(RecordMutationExecutionOutcome.RollbackConfirmed, result.Outcome);
        Assert.True(probe.CaptureResult?.Value is not null,
            probe.CaptureResult?.Error is { } error ? $"{error.Code}:{error.Message}" : "capture returned no result");
        BaseCapturedAtomicExecution captured = Assert.IsType<BaseCapturedAtomicExecution>(probe.Captured);
        BaseLogicalIndexSelectionEvidence evidence = Assert.IsType<BaseLogicalIndexSelectionEvidence>(
            captured.Selection!.LogicalIndexEvidence);
        Assert.Empty(captured.Selection.Records);
        Assert.Equal(request.Selection!.Selection.LogicalIndexPoint!.IndexId, evidence.IndexId);
        Assert.True(BaseLogicalIndexSelectionEvidenceContract.Validate(evidence));
        Assert.Equal(evidence.EvidenceBytes, captured.Selection.Accounting.EvidenceBytes);
        Assert.Equal(BaseIndexAccessShape.LogicalIndexPoint, evidence.AccessShape);
    }

    [Fact]
    public async Task Logical_index_quarantine_is_store_unhealthy_with_safe_reason()
    {
        CollectionDefinition collection = Collection();
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "sqlite-logical-index-health",
            Collections = [collection],
        };
        await using SqliteRecordStore store = SqliteTestFactory.Create(options);
        store.QuarantineLogicalIndexes();

        HealthDescriptor health = Assert.Single(await new SqliteHealthContributor(
            Options.Create(options), store).GetHealthAsync());

        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Contains(health.Metrics!, metric => metric.Name == "logicalIndexQuarantined"
            && metric.BooleanValue == true);
        Assert.Contains(health.Metrics!, metric => metric.Name == "logicalIndexReasonCode"
            && metric.TextValue == BaseSchemaErrorCodes.ProviderEvidenceInvalid);
    }

    [Fact]
    public async Task Certification_inspection_is_owned_and_corruption_closes_point_execution()
    {
        CollectionDefinition collection = Collection();
        await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions
        {
            StoreId = "sqlite-logical-index-certification",
            Collections = [collection],
        });
        var inspection = (IBaseLogicalIndexCertificationInspection)store;
        BaseLogicalIndexCertificationSnapshot snapshot = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Id, collection.Indexes![0].Checksum);
        Assert.Empty(snapshot.Directory.EqualityPostings);
        Assert.Equal(BaseLogicalIndexDirectoryContract.EmptyDirectoryRetainedBytes,
            snapshot.Directory.Accounting.RetainedDirectoryBytes);
        await inspection.CorruptLogicalIndexMemberSetForCertificationAsync(
            collection.Id, collection.Indexes![0].Checksum);
        var probe = new CaptureProbe(await PointRequestAsync(store, collection, "x"));

        await store.ExecuteAtomicAsync(probe, Execution);

        Assert.Equal(BaseSchemaErrorCodes.ProviderEvidenceInvalid, probe.CaptureResult!.Error!.Code);
        Assert.True(store.LogicalIndexStoreIsQuarantined);
    }

    private static async ValueTask<BaseAtomicExecutionRequest> PointRequestAsync(
        SqliteRecordStore store,
        CollectionDefinition collection,
        string code)
    {
        BaseSelectionOperationLimits selectionLimits = SelectionLimits();
        BaseAtomicMutationExecutionLimits limits = BaseAtomicSchemaContract.AttachLimits(
            DefaultBaseSelectionMutationRuntime.CreateExecutionLimits(selectionLimits), [collection]);
        BaseAtomicMutationAuthorityRequirement authority = (await store
            .CaptureAtomicMutationAuthorityRequirementAsync("point-test", [collection], limits)).Value!;
        RecordQuery sourceQuery = new()
        {
            Filter = new FilterExpression
            {
                Kind = FilterNodeKind.And,
                Children = [Equal("a-tenant", "a"), Equal("b-code", code)],
            },
            Sort = [new QuerySort { Field = "id", Direction = QuerySortDirection.Asc }],
            Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 4 },
        };
        BaseLogicalIndexPointSelection point = BaseLogicalIndexPointPlanContract.Derive(
            collection, sourceQuery.Filter)!;
        return new BaseAtomicExecutionRequest
        {
            Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
            Intent = new BaseAtomicMutationIntent
            {
                IntentDigest = "sqlite-point-capture-intent-" + code,
                Authority = authority,
                Items = [],
            },
            Selection = new BaseSelectionMutationCaptureExtension
            {
                OperationProfileId = "point-test.patch",
                OperationProfileVersion = 1,
                OperationProfileChecksum = new string('a', 64),
                Selection = new BaseAtomicSelectionRequest
                {
                    Collection = collection,
                    Query = BaseQueryFieldResolver.ToStoredNames(collection, sourceQuery),
                    CanonicalRecordCodecVersion = 1,
                    LogicalIndexPoint = BaseLogicalIndexPointPlanContract.Clone(point),
                },
            },
            Limits = limits,
            Schema = BaseAtomicSchemaContract.CaptureRequest(authority, [collection], limits),
        };
    }

    private sealed class CaptureProbe(BaseAtomicExecutionRequest request) : IAtomicMutationProcessor
    {
        internal BaseCapturedAtomicExecution? Captured { get; private set; }
        internal OperationResult<BaseCapturedAtomicExecution>? CaptureResult { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            OperationResult<BaseCapturedAtomicExecution> captured = await session
                .CaptureAtomicExecutionAsync(request, cancellationToken);
            CaptureResult = captured;
            Captured = captured.Value;
            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed,
                [],
                captured.Error ?? new BaseError
                {
                    Code = "base.test.rollback",
                    Message = "The capture-only proof rolls back intentionally.",
                    Category = ErrorCategory.Store,
                });
        }
    }

    private static CollectionDefinition Collection()
    {
        FieldDefinition[] fields = [Field("a-tenant", "tenant"), Field("b-code", "code")];
        BaseLogicalIndexDefinition index = BaseSchemaContract.SealIndex(new BaseLogicalIndexDefinition
        {
            Id = BaseLogicalIndexId.Create("items.by-tenant-code"),
            Version = 1,
            CollectionId = "items",
            Parts = [Part(0), Part(1)],
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
                Checksum = BaseSchemaAuthorityChecksum.Create(Enumerable.Repeat((byte)0x21, 32).ToArray()),
            },
            Checksum = default,
        }, fields.OrderBy(static field => field.Id, StringComparer.Ordinal).ToArray());
        return new CollectionDefinition
        {
            Id = "items",
            Name = "items",
            Kind = "record",
            SchemaMode = SchemaMode.Strict,
            UnknownFields = UnknownFieldPolicy.Reject,
            Fields = fields,
            Indexes = [index],
        };
    }

    private static FieldDefinition Field(string id, string wireName) => new()
    {
        Id = id,
        ApplicationName = wireName,
        WireName = wireName,
        Type = "string",
        ScalarKind = BaseScalarKind.String,
        ScalarCodec = BaseGeneratedSchemaRegistration.ScalarCodec(BaseScalarKind.String),
        ScalarConstraints = new BaseScalarConstraintSet(),
    };

    private static BaseLogicalIndexPart Part(int ordinal) => new()
    {
        FieldOrdinal = ordinal,
        Direction = BaseIndexSortDirection.Ascending,
        Collation = BaseIndexCollation.OrdinalBinary,
        NullOrder = BaseIndexNullOrder.MissingThenNullThenValue,
    };

    private static FilterExpression Equal(string field, string value) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = field,
        Operator = FilterOperator.Equal,
        Value = new QueryValue { Kind = QueryValueKind.String, String = value },
    };

    private static BaseSelectionOperationLimits SelectionLimits() => new()
    {
        MaximumQueryNodes = 16,
        MaximumQueryDepth = 4,
        MaximumLiteralValues = 8,
        MaximumSelectedRecords = 4,
        MaximumSelectedBytes = 16_384,
        MaximumProducedMutations = 4,
        MaximumQueryExecutions = 1,
        MaximumReadIntervals = 2,
        MaximumWrittenBytes = 16_384,
        MaximumFactBytes = 16_384,
        MaximumJournalBytes = 32_768,
        MaximumReceiptBytes = 32_768,
        MaximumRelationChecks = 0,
        MaximumUniqueConstraintChecks = 16,
        MaximumPreviousStateRequirements = 0,
        MaximumTransientBytes = 65_536,
        MaximumResultBytes = 4_096,
        AcquisitionTimeout = TimeSpan.FromSeconds(1),
        ExecutionTimeout = TimeSpan.FromSeconds(1),
        CallerCommitObservationTimeout = TimeSpan.FromSeconds(1),
    };
}
