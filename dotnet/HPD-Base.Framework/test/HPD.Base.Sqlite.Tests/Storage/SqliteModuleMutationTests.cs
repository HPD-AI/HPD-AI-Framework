using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed partial class SqliteModuleMutationTests
{
    [Fact]
    public async Task Generation_operation_commits_replays_and_survives_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l50-{Guid.NewGuid():N}.db");
        try
        {
            BaseMutationRequestIdentity requestIdentity = BaseMutationRequestIdentity.Create(
                "module", "increment", "one", BaseMutationRequestFingerprint.Create(new byte[32]));
            await using (SqliteRecordStore store = Store(path))
            {
                DefaultBaseModuleMutationRuntime runtime = Runtime(store);
                BaseResult<BaseModuleMutationExecutionResult<Result>> first = await runtime.ExecuteAsync(
                    Session(), Definition(), Identity(), new Request(), requestIdentity, null, default);
                BaseResult<BaseModuleMutationExecutionResult<Result>> duplicate = await runtime.ExecuteAsync(
                    Session(), Definition(), Identity(), new Request(), requestIdentity, null, default);

                first.RequireValue().Result.Generation.Should().Be("1");
                first.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Committed);
                duplicate.RequireValue().Result.Generation.Should().Be("1");
                duplicate.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            }

            await using (SqliteRecordStore reopened = Store(path))
            {
                BaseResult<BaseModuleMutationExecutionResult<Result>> resolved = await Runtime(reopened).ResolveAsync(
                    Session(), Definition(), Identity(), requestIdentity, default);
                resolved.RequireValue().Result.Generation.Should().Be("1");
                resolved.RequireValue().Disposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static SqliteRecordStore Store(string path)
    {
        var store = new SqliteRecordStore(new HPDBaseSqliteOptions
        {
            StoreId = "module-store", DataSource = path, Collections = [],
        }, NullLoggerFactory.Instance);
        store.InitializeUnacceptedSchemaForTestsAsync().AsTask().GetAwaiter().GetResult();
        return store;
    }

    private static DefaultBaseModuleMutationRuntime Runtime(SqliteRecordStore store)
    {
        var stores = new DefaultRecordStoreRegistry();
        stores.Add(new RecordStoreRegistration { StoreId = "module-store", Store = store });
        BaseRegisteredModuleMutationDefinition definition = Definition();
        var cell = new BaseModuleGenerationCellDefinition
        {
            Id = "module.generation", Version = 1, OwningModuleId = "module",
            Scope = BaseModuleGenerationScope.Application, MaximumKeyUtf8Bytes = 32, MaximumCellsPerOperation = 1,
        };
        return new DefaultBaseModuleMutationRuntime(stores, new BaseCollectionRegistry(new Dictionary<string, CollectionDefinition>()),
            new BaseModuleMutationRegistry([definition], [cell]), null!, Policy(), null!, new BaseSubjectContractRegistry([]), TimeProvider.System);
    }

    private static BaseSession Session() => new(null!, TimeProvider.System,
        new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System, SubjectId = "system" },
        new BaseSessionOptions { Audience = HPDBaseEndpointAudience.ControlPlane }, applicationId: "module.application");

    private static DefaultBasePolicyOrchestrator Policy()
    {
        var builder = new BasePolicyAuthorityBuilder();
        builder.AddPolicy(new BasePolicyAuthorityDefinition
        {
            Id = "module.policy", Version = 1, OwningModuleId = "module",
            EvaluatorContractId = "module.policy.evaluator", EvaluatorContractVersion = 1, CompositionOrder = 0,
        }, new AllowPolicyEvaluator());
        return new DefaultBasePolicyOrchestrator(builder.Freeze("module.application"));
    }

    private static BaseGeneratedModuleMutationIdentity<Request, Result> Identity() => new(
        "module.increment", 1, new byte[32], Json.Default.Request, Json.Default.Result, [],
        [BaseModuleDtoPropertyBinding.Create<Result>("result.generation", nameof(Result.Generation))]);

    private static BaseRegisteredModuleMutationDefinition Definition() => new()
    {
        Id = "module.increment", Version = 1, OwningModuleId = "module", GrantId = "module.increment",
        Audience = BaseModuleMutationAudience.System, RequestTypeId = "request", ResultTypeId = "result",
        SystemCollectionIds = [], GenerationCellIds = ["module.generation"], ImportedSubjectContractIds = [],
        Template = new BaseModuleMutationTemplate
        {
            Captures = [new BaseModuleGenerationCapture { Id = "generation", CellId = "module.generation", Absence = BaseModuleGenerationAbsenceBehavior.AllowEither }],
            Guards = [],
            Body = new BaseModuleMutationBlock { Statements = [new BaseModuleIncrementGenerationStatement { Id = "increment", CaptureId = "generation", CreateIfAbsent = true }] },
            Result = new BaseModuleResultProjection
            {
                Value = new BaseModuleObjectExpression
                {
                    Id = "result", ResultTypeId = "result",
                    Properties = [new BaseModuleObjectPropertyExpression
                    {
                        StablePropertyId = "result.generation",
                        Value = new BaseModuleResultingGenerationExpression { Id = "result-generation", ResultTypeId = "string", CaptureId = "generation" },
                    }],
                },
            },
        },
        Limits = Limits(), ReceiptPolicy = new BaseModuleMutationReceiptPolicy { FormatVersion = 1, Lifetime = TimeSpan.FromDays(1) },
        Checksum = BaseModuleMutationChecksum.Create(new byte[32]),
    };

    private static BaseModuleMutationLimits Limits() => new()
    {
        MaximumCaptures = 8, MaximumRecordCaptures = 8, MaximumRelationTargetCaptures = 8, MaximumGenerationCaptures = 8,
        MaximumRecordMutations = 8, MaximumGenerationReads = 8, MaximumGenerationComparisons = 8, MaximumGenerationIncrements = 8,
        MaximumGuardNodes = 8, MaximumGuardDepth = 8, MaximumStatements = 8, MaximumBranches = 8, MaximumExpressionNodes = 32,
        MaximumReadIntervals = 16, MaximumSubjectValidations = 8, MaximumAuthorityReads = 16, MaximumRelationChecks = 8,
        MaximumUniqueConstraintChecks = 8, MaximumRequestBytes = 4096, MaximumSelectedBytes = 4096, MaximumGenerationBytes = 4096,
        MaximumEvidenceBytes = 4096, MaximumWrittenBytes = 4096, MaximumFactBytes = 4096, MaximumJournalBytes = 4096,
        MaximumReceiptBytes = 4096, MaximumResultBytes = 4096, MaximumTransientBytes = 65536,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };

    public sealed record Request;
    public sealed record Result { public required string Generation { get; init; } }
    [JsonSerializable(typeof(Request))]
    [JsonSerializable(typeof(Result))]
    internal sealed partial class Json : JsonSerializerContext;

    private sealed class AllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
    }
}
