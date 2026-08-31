using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace HPD.Base.Tests.InMemory.Mutations;

public sealed class InMemoryLogicalIndexPointCaptureTests
{
    private static readonly RecordMutationExecutionRequest Execution = new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(1),
        TransactionTimeout = TimeSpan.FromSeconds(1),
        CommitCompletionTimeout = TimeSpan.FromSeconds(1),
    };

    [Fact]
    public async Task Empty_required_directory_returns_owned_point_miss_evidence()
    {
        CollectionDefinition collection = Collection();
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            StoreId = "logical-index-point-test",
            Collections = [collection],
        });
        BaseLogicalIndexCertificationSnapshot initial = await ((IBaseLogicalIndexCertificationInspection)store)
            .InspectLogicalIndexForCertificationAsync(collection.Id, collection.Indexes![0].Checksum);
        Assert.Empty(initial.Directory.EqualityPostings);
        Assert.Equal(BaseLogicalIndexDirectoryContract.EmptyDirectoryRetainedBytes,
            initial.Directory.Accounting.RetainedDirectoryBytes);
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
                Children =
                [
                    Equal("a-tenant", "a"),
                    Equal("b-code", "x"),
                ],
            },
            Sort = [new QuerySort { Field = "id", Direction = QuerySortDirection.Asc }],
            Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 4 },
        };
        BaseLogicalIndexPointSelection point = BaseLogicalIndexPointPlanContract
            .Derive(collection, sourceQuery.Filter)!;
        var request = new BaseAtomicExecutionRequest
        {
            Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
            Intent = new BaseAtomicMutationIntent
            {
                IntentDigest = "point-capture-intent",
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
        var probe = new CaptureProbe(request);

        RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(probe, Execution);

        Assert.Equal(RecordMutationExecutionOutcome.RollbackConfirmed, result.Outcome);
        BaseCapturedAtomicExecution captured = Assert.IsType<BaseCapturedAtomicExecution>(probe.Captured);
        BaseLogicalIndexSelectionEvidence evidence = Assert.IsType<BaseLogicalIndexSelectionEvidence>(
            captured.Selection!.LogicalIndexEvidence);
        Assert.Empty(captured.Selection.Records);
        Assert.Equal(point.IndexId, evidence.IndexId);
        Assert.Equal(point.PredicateConjunctChecksum.ToArray(), evidence.MatchedPredicateChecksum.ToArray());
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(point.EqualityKey.AsSpan()),
            evidence.EqualityKeyChecksum.ToArray());
        Assert.True(BaseLogicalIndexSelectionEvidenceContract.Validate(evidence));
        Assert.Single(captured.ReadIntervals);
        Assert.Equal(evidence.EvidenceBytes, captured.Selection.Accounting.EvidenceBytes);
    }

    [Fact]
    public async Task Corrupt_required_directory_quarantines_point_execution_without_scan_fallback()
    {
        CollectionDefinition collection = Collection();
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            StoreId = "logical-index-corruption-test",
            Collections = [collection],
        });
        await ((IBaseLogicalIndexCertificationInspection)store)
            .CorruptLogicalIndexMemberSetForCertificationAsync(
            collection.Id, collection.Indexes![0].Checksum);
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
                Children = [Equal("a-tenant", "a"), Equal("b-code", "x")],
            },
            Sort = [new QuerySort { Field = "id", Direction = QuerySortDirection.Asc }],
            Page = new QueryPage { Mode = QueryPaginationMode.Offset, Offset = 0, Limit = 4 },
        };
        BaseLogicalIndexPointSelection point = BaseLogicalIndexPointPlanContract.Derive(
            collection, sourceQuery.Filter)!;
        var request = new BaseAtomicExecutionRequest
        {
            Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
            Intent = new BaseAtomicMutationIntent
            {
                IntentDigest = "corrupt-point-capture-intent",
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
        var first = new CaptureProbe(request);
        var second = new CaptureProbe(request);

        await store.ExecuteAtomicAsync(first, Execution);
        await store.ExecuteAtomicAsync(second, Execution);

        Assert.Equal(BaseSchemaErrorCodes.ProviderEvidenceInvalid, first.CaptureResult!.Error!.Code);
        Assert.Equal(BaseSchemaErrorCodes.ProviderEvidenceInvalid, second.CaptureResult!.Error!.Code);
        Assert.Null(first.Captured);
        Assert.Null(second.Captured);
        Assert.True(store.LogicalIndexQuarantinedForTesting);

        HealthDescriptor health = Assert.Single(await new InMemoryHealthContributor(
            Options.Create(new HPDBaseInMemoryStoreOptions
            {
                StoreId = "logical-index-corruption-test",
                Collections = [collection],
            }), store).GetHealthAsync());
        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Contains(health.Metrics!, metric => metric.Name == "logicalIndexQuarantined"
            && metric.BooleanValue == true);
        Assert.Contains(health.Metrics!, metric => metric.Name == "logicalIndexReasonCode"
            && metric.TextValue == BaseSchemaErrorCodes.ProviderEvidenceInvalid);
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
                new BaseError
                {
                    Code = "base.test.rollback",
                    Message = "The capture-only proof rolls back intentionally.",
                    Category = ErrorCategory.Store,
                });
        }
    }

    private static CollectionDefinition Collection()
    {
        FieldDefinition[] fields =
        [
            Field("a-tenant", "tenant"),
            Field("b-code", "code"),
        ];
        BaseLogicalIndexDefinition index = new()
        {
            Id = BaseLogicalIndexId.Create("items.by-tenant-code"),
            Version = 1,
            CollectionId = "items",
            Parts =
            [
                Part(0),
                Part(1),
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
                Checksum = BaseSchemaAuthorityChecksum.Create(Enumerable.Repeat((byte)0x21, 32).ToArray()),
            },
            Checksum = BaseLogicalIndexChecksum.Create(Enumerable.Repeat((byte)0x31, 32).ToArray()),
        };
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
