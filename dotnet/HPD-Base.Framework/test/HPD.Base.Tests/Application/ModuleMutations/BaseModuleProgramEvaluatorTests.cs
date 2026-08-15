using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base.Tests.Application.ModuleMutations;

public sealed class BaseModuleProgramEvaluatorTests
{
    [Fact]
    public async Task Generation_only_operation_commits_and_replays_through_the_real_in_memory_boundary()
    {
        var storeOptions = new HPDBaseInMemoryStoreOptions { StoreId = "module-store", Collections = [] };
        var store = new InMemoryRecordStore(storeOptions);
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store });
        BaseModuleGenerationCellDefinition cell = new()
        {
            Id = "module.generation", Version = 1, OwningModuleId = "module",
            Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
        };
        BaseRegisteredModuleMutationDefinition definition = GenerationDefinition();
        var registry = new BaseModuleMutationRegistry([definition], [cell]);
        var runtime = new DefaultBaseModuleMutationRuntime(
            stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()), registry, TimeProvider.System);
        BaseGeneratedModuleMutationIdentity<GenerationRequest, GenerationResult> identity = GenerationIdentity();
        var session = new BaseSession(
            null!, TimeProvider.System,
            new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectId = "system" },
            new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane },
            applicationId: "module.application");
        BaseMutationRequestIdentity requestIdentity = BaseMutationRequestIdentity.Create(
            "module", "increment", "one", BaseMutationRequestFingerprint.Create(new byte[32]));

        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> first = await runtime.ExecuteAsync(
            session, definition, identity, new GenerationRequest(), requestIdentity, null, default);
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> duplicate = await runtime.ExecuteAsync(
            session, definition, identity, new GenerationRequest(), requestIdentity, null, default);
        BaseResult<BaseModuleMutationExecutionResult<GenerationResult>> resolved = await runtime.ResolveAsync(
            session, definition, identity, requestIdentity, default);

        first.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<GenerationResult>>>(
            first is BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>> failed ? failed.Error.Code : string.Empty);
        duplicate.Should().BeOfType<BaseSuccess<BaseModuleMutationExecutionResult<GenerationResult>>>(
            duplicate is BaseFailure<BaseModuleMutationExecutionResult<GenerationResult>> duplicateFailure ? duplicateFailure.Error.Code : string.Empty);
        first.RequireValue().Result.Generation.Should().Be("1");
        first.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
        duplicate.RequireValue().Result.Generation.Should().Be("1");
        duplicate.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
        resolved.RequireValue().Result.Generation.Should().Be("1");
        resolved.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
    }

    [Fact]
    public void Stable_request_edges_drive_guards_and_result_projection()
    {
        BaseGeneratedModuleMutationIdentity<EvaluatorRequest, EvaluatorResult> identity = Identity();
        BaseRegisteredModuleMutationDefinition definition = Definition();
        BaseCapturedAtomicMutationAuthority captured = Captured();
        var evaluator = new BaseModuleProgramEvaluator<EvaluatorRequest, EvaluatorResult>(
            definition,
            identity,
            new EvaluatorRequest { Amount = 41, Enabled = true },
            captured,
            new Dictionary<string, CollectionDefinition>());

        evaluator.Guard("enabled").Should().BeTrue();
        BaseModuleProgramValue sum = evaluator.Evaluate(new BaseModuleBinaryNumericExpression
        {
            Id = "amount-plus-one",
            ResultTypeId = "int64",
            Operator = BaseModuleNumericOperator.IntegerAddChecked,
            Left = Request("request.amount", "amount"),
            Right = Constant("one", "1"u8),
        });

        sum.Value.GetInt64().Should().Be(42);
        EvaluatorResult result = evaluator.ProjectResult(
            definition.Template.Result,
            new Dictionary<string, BaseRecordMutationFact>(),
            new Dictionary<string, BaseModuleCommittedGeneration>(),
            out ImmutableArray<byte> bytes);
        result.Amount.Should().Be(41);
        bytes.Should().Equal("{\"Amount\":41}"u8.ToArray());
    }

    private static BaseGeneratedModuleMutationIdentity<EvaluatorRequest, EvaluatorResult> Identity() => new(
        "module.test", 1, new byte[32], EvaluatorJsonContext.Default.EvaluatorRequest,
        EvaluatorJsonContext.Default.EvaluatorResult,
        [
            new BaseModuleDtoPropertyBinding { StablePropertyId = "request.amount", DeclaringType = typeof(EvaluatorRequest), ApplicationName = nameof(EvaluatorRequest.Amount) },
            new BaseModuleDtoPropertyBinding { StablePropertyId = "request.enabled", DeclaringType = typeof(EvaluatorRequest), ApplicationName = nameof(EvaluatorRequest.Enabled) },
        ],
        [new BaseModuleDtoPropertyBinding { StablePropertyId = "result.amount", DeclaringType = typeof(EvaluatorResult), ApplicationName = nameof(EvaluatorResult.Amount) }]);

    private static BaseGeneratedModuleMutationIdentity<GenerationRequest, GenerationResult> GenerationIdentity() => new(
        "module.increment", 1, new byte[32], EvaluatorJsonContext.Default.GenerationRequest,
        EvaluatorJsonContext.Default.GenerationResult, [],
        [new BaseModuleDtoPropertyBinding { StablePropertyId = "result.generation", DeclaringType = typeof(GenerationResult), ApplicationName = nameof(GenerationResult.Generation) }]);

    private static BaseRegisteredModuleMutationDefinition GenerationDefinition() => new()
    {
        Id = "module.increment", Version = 1, OwningModuleId = "module", GrantId = "module.increment",
        Audience = BaseModuleMutationAudience.System, RequestTypeId = "request", ResultTypeId = "result",
        SystemCollectionIds = [], GenerationCellIds = ["module.generation"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures =
            [
                new BaseModuleGenerationCapture
                {
                    Id = "generation", CellId = "module.generation", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither,
                },
            ],
            Guards = [],
            Body = new BaseModuleMutationBlock
            {
                Statements = [new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result", ResultTypeId = "result",
                    Properties =
                    [
                        new BaseModuleObjectPropertyExpression
                        {
                            StablePropertyId = "result.generation",
                            Value = new BaseModuleResultingGenerationExpression { Id = "result-generation", ResultTypeId = "string", CaptureId = "generation" },
                        },
                    ],
                },
            },
        },
        Limits = Limits(),
        ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
    };

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 8, MaximumRecordCaptures = 8, MaximumRelationTargetCaptures = 8,
        MaximumGenerationCaptures = 8, MaximumRecordMutations = 8, MaximumGenerationReads = 8,
        MaximumGenerationComparisons = 8, MaximumGenerationIncrements = 8, MaximumGuardNodes = 8,
        MaximumGuardDepth = 8, MaximumStatements = 8, MaximumBranches = 8, MaximumExpressionNodes = 32,
        MaximumReadIntervals = 16, MaximumSubjectValidations = 8, MaximumAuthorityReads = 16,
        MaximumRelationChecks = 8, MaximumUniqueConstraintChecks = 8, MaximumRequestBytes = 4096,
        MaximumSelectedBytes = 4096, MaximumGenerationBytes = 4096, MaximumEvidenceBytes = 4096,
        MaximumWrittenBytes = 4096, MaximumFactBytes = 4096, MaximumJournalBytes = 4096,
        MaximumReceiptBytes = 4096, MaximumResultBytes = 4096, MaximumTransientBytes = 65536,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };

    private static BaseRegisteredModuleMutationDefinition Definition() => new()
    {
        Id = "module.test", Version = 1, OwningModuleId = "module", GrantId = "module.execute",
        Audience = BaseModuleMutationAudience.Service, RequestTypeId = "request", ResultTypeId = "result",
        SystemCollectionIds = [], GenerationCellIds = [], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [],
            Guards =
            [
                new BaseModuleRecordPresenceGuard
                {
                    Id = "enabled",
                    CaptureId = "existing",
                    MustBePresent = true,
                },
            ],
            Body = new BaseModuleMutationBlock
            {
                Statements = [new BaseModuleRequireStatement { Id = "require", GuardId = "enabled", RequirementId = "enabled" }],
            },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result", ResultTypeId = "result",
                    Properties =
                    [
                        new BaseModuleObjectPropertyExpression
                        {
                            StablePropertyId = "result.amount",
                            Value = Request("request.amount", "result-amount"),
                        },
                    ],
                },
            },
        },
        Limits = null!, ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
    };

    private static BaseModuleRequestPropertyExpression Request(string stableId, string id) => new()
    {
        Id = id, ResultTypeId = "int64",
        Property = new BaseModuleRequestPropertyReference { StablePropertyPath = [stableId], DeclaredTypeId = "int64" },
    };

    private static BaseModuleConstantExpression Constant(string id, ReadOnlySpan<byte> bytes) => new()
    {
        Id = id, ResultTypeId = "json", CanonicalBaseJson = bytes.ToArray().ToImmutableArray(),
    };

    private static BaseCapturedAtomicMutationAuthority Captured() => new()
    {
        Kind = BaseAtomicMutationExecutionKind.ModuleMutation,
        IntentDigest = "intent", CaptureDigest = "capture", Authority = null!,
        Items = [],
        ModuleRecords =
        [
            new BaseCapturedModuleRecord
            {
                Ordinal = 0, CaptureId = "existing", CollectionId = "records", RecordId = new RecordId("one"), Exists = true,
                Current = new RecordEnvelope
                {
                    CollectionId = "records", Id = new RecordId("one"),
                    Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = new Dictionary<string, System.Text.Json.JsonElement>() },
                    Metadata = new RecordMetadata(),
                },
            },
        ],
        ModuleRelationTargets = [], Generations = [], ReadIntervals = [],
        Accounting = new BaseAtomicCaptureAccounting
        {
            Records = 0, RelationTargetReads = 0, GenerationReads = 0, SelectedBytes = 0,
            RelationTargetBytes = 0, GenerationBytes = 0, ReadIntervals = 0, EvidenceBytes = 0, TransientBytes = 0,
        },
    };
}

public sealed record EvaluatorRequest
{
    public required long Amount { get; init; }
    public required bool Enabled { get; init; }
}

public sealed record EvaluatorResult
{
    public required long Amount { get; init; }
}

public sealed record GenerationRequest;
public sealed record GenerationResult { public required string Generation { get; init; } }

[JsonSerializable(typeof(EvaluatorRequest))]
[JsonSerializable(typeof(EvaluatorResult))]
[JsonSerializable(typeof(GenerationRequest))]
[JsonSerializable(typeof(GenerationResult))]
internal sealed partial class EvaluatorJsonContext : JsonSerializerContext;
