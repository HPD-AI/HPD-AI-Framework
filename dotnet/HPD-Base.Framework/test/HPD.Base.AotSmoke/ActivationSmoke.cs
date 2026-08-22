using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.Base.AotSmoke;

internal static class ActivationSmoke
{
    private static readonly AotApplicationJsonContext Serializer = new(
        BaseSerializerGeneratedContract.CreateOptions(JsonNamingPolicy.CamelCase));
    internal static readonly string[] GrantIds =
    [
        "hpd.base.aot.activation.enqueue", "hpd.base.aot.activation.observe",
        "hpd.base.aot.activation.claim", "hpd.base.aot.activation.execute",
        "hpd.base.aot.activation.renew", "hpd.base.aot.activation.complete",
        "hpd.base.aot.activation.fail", "hpd.base.aot.activation.cancel",
        "hpd.base.aot.activation.inspect", "hpd.base.aot.activation.replay",
        "hpd.base.aot.activation.migrate", "hpd.base.aot.activation.reconcile",
        "hpd.base.aot.activation.retry", "hpd.base.aot.activation.dispose",
        "hpd.base.aot.activation.remove", "hpd.base.aot.activation.repair",
    ];

    internal static BaseActivationHandlerRegistration<ActivationSmokeInput, ActivationSmokeResult> Registration { get; } =
        BaseActivationDefinitionBuilder.Create(new BaseActivationDefinition
        {
            Id = "hpd.base.aot.activation", Version = 1, OwningModuleId = "hpd.base.aot",
            ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
            InputTypeId = "hpd.base.aot.activation.input", ResultTypeId = "hpd.base.aot.activation.result",
            Grants = new BaseActivationGrantSet
            {
                Enqueue = GrantIds[0], Observe = GrantIds[1], Claim = GrantIds[2], Execute = GrantIds[3],
                Renew = GrantIds[4], Complete = GrantIds[5], Fail = GrantIds[6], Cancel = GrantIds[7],
                Inspect = GrantIds[8], Replay = GrantIds[9], Migrate = GrantIds[10], Reconcile = GrantIds[11],
                Retry = GrantIds[12], Dispose = GrantIds[13], Remove = GrantIds[14], Repair = GrantIds[15],
            },
            SourceGrantIds = [],
            Retry = new BaseActivationRetryProfile
            {
                MaximumAttempts = 3, InitialDelayMilliseconds = 100, MaximumDelayMilliseconds = 1000,
                MultiplierNumerator = 2, MultiplierDenominator = 1, JitterBasisPoints = 0,
                RetryableFailureCodes = ["hpd.base.aot.activation.retryable"],
            },
            Limits = new BaseActivationLimits
            {
                MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 3,
                MaximumRenewalsPerAttempt = 4, MaximumChildrenPerAttempt = 4, MaximumLineageDepth = 4,
                LeaseDuration = TimeSpan.FromMinutes(1), HandlerTimeout = TimeSpan.FromSeconds(5),
                Provider = ProviderLimits(), AtomicCreation = AtomicLimits(),
            },
            Handler = new BaseActivationHandlerBinding
            {
                Id = "hpd.base.aot.activation.handler", Version = 1,
                FactoryId = "hpd.base.aot.activation.handler.factory",
                InputTypeId = "hpd.base.aot.activation.input", ResultTypeId = "hpd.base.aot.activation.result",
                WorkerSubjectKind = AccessSubjectKind.ServicePrincipal,
                Checksum = ImmutableArray.Create(new byte[32]),
            },
            Checksum = [],
        }, Serializer.ActivationSmokeInput, Serializer.ActivationSmokeResult,
        [BaseModuleDtoPropertyBinding.Create<ActivationSmokeInput, string>("hpd.base.aot.activation.input.value", "value")],
        [BaseModuleDtoPropertyBinding.Create<ActivationSmokeResult, string>("hpd.base.aot.activation.result.value", "value")],
        static _ => new ActivationSmokeHandler());

    private static BaseActivationExecutionLimits ProviderLimits() => new()
    {
        MaximumCandidates = 8, MaximumInputBytes = 4096, MaximumResultBytes = 4096,
        MaximumEvidenceBytes = 8192, MaximumTransientBytes = 16384,
        MaximumReadIntervals = 8, MaximumIndexOperations = 16,
        AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
    };

    private static BaseAtomicMutationExecutionLimits AtomicLimits() => new()
    {
        MaximumItems = 8, MaximumQueryNodes = 8, MaximumQueryDepth = 4,
        MaximumLiteralValues = 8, MaximumSelectedRecords = 8, MaximumProducedMutations = 8,
        MaximumQueryExecutions = 8, MaximumPreviousStateRequirements = 8,
        MaximumRecordCaptures = 8, MaximumRelationTargetCaptures = 8,
        MaximumSelectedBytes = 4096, MaximumEvidenceBytes = 8192,
        MaximumTransientBytes = 16384, MaximumReadIntervals = 8, MaximumSubjectValidations = 8,
        MaximumAuthorityReads = 16, MaximumRelationChecks = 8, MaximumUniqueConstraintChecks = 8,
        MaximumRequestBytes = 4096, MaximumGenerationBytes = 4096, MaximumWrittenBytes = 4096,
        MaximumFactBytes = 4096, MaximumJournalBytes = 4096, MaximumReceiptBytes = 8192,
        MaximumResultBytes = 4096, MaximumGenerationReads = 8, MaximumGenerationComparisons = 8,
        MaximumGenerationIncrements = 8, MaximumGuardNodes = 8, MaximumExpressionNodes = 32,
        MaximumStatements = 8, MaximumBranches = 8, MaximumGuardDepth = 4,
        MaximumRetirementProjections = 8, MaximumRetirementBarrierReads = 8,
        MaximumRetirementAcknowledgementReads = 8, MaximumRetirementPublications = 8,
        MaximumRetirementEvidenceBytes = 4096, MaximumRetirementPublicationBytes = 4096,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };
}

internal sealed record ActivationSmokeInput
{
    [BaseField("hpd.base.aot.activation.input.value")]
    public required string Value { get; init; }
}

internal sealed record ActivationSmokeResult
{
    [BaseField("hpd.base.aot.activation.result.value")]
    public required string Value { get; init; }
}

internal sealed class ActivationSmokeHandler : IBaseActivationHandler<ActivationSmokeInput, ActivationSmokeResult>
{
    public ValueTask<BaseActivationHandlerResult<ActivationSmokeResult>> ExecuteAsync(
        BaseActivationContext context, ActivationSmokeInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new BaseActivationHandlerResult<ActivationSmokeResult>
        { Result = new ActivationSmokeResult { Value = input.Value } });
    }
}
