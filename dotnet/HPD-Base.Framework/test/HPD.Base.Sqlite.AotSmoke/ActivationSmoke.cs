using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.Base.Sqlite.AotSmoke;

internal static class ActivationSmoke
{
    private static readonly SemanticSmokeJsonContext Serializer = new(
        BaseSerializerGeneratedContract.CreateOptions(JsonNamingPolicy.CamelCase));
    internal static readonly string[] GrantIds =
    [
        "hpd.base.sqlite.aot.activation.enqueue", "hpd.base.sqlite.aot.activation.observe",
        "hpd.base.sqlite.aot.activation.claim", "hpd.base.sqlite.aot.activation.execute",
        "hpd.base.sqlite.aot.activation.renew", "hpd.base.sqlite.aot.activation.complete",
        "hpd.base.sqlite.aot.activation.fail", "hpd.base.sqlite.aot.activation.cancel",
        "hpd.base.sqlite.aot.activation.inspect", "hpd.base.sqlite.aot.activation.replay",
        "hpd.base.sqlite.aot.activation.migrate", "hpd.base.sqlite.aot.activation.reconcile",
        "hpd.base.sqlite.aot.activation.retry", "hpd.base.sqlite.aot.activation.dispose",
        "hpd.base.sqlite.aot.activation.remove", "hpd.base.sqlite.aot.activation.repair",
    ];

    internal static BaseActivationHandlerRegistration<ActivationSmokeInput, ActivationSmokeResult> Registration { get; } =
        BaseActivationDefinitionBuilder.Create(new BaseActivationDefinition
        {
            Id = "hpd.base.sqlite.aot.activation", Version = 1, OwningModuleId = "hpd.base.sqlite.aot",
            ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
            InputTypeId = "hpd.base.sqlite.aot.activation.input", ResultTypeId = "hpd.base.sqlite.aot.activation.result",
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
                RetryableFailureCodes = ["hpd.base.sqlite.aot.activation.retryable"],
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
                Id = "hpd.base.sqlite.aot.activation.handler", Version = 1,
                FactoryId = "hpd.base.sqlite.aot.activation.handler.factory",
                InputTypeId = "hpd.base.sqlite.aot.activation.input", ResultTypeId = "hpd.base.sqlite.aot.activation.result",
                WorkerSubjectKind = AccessSubjectKind.ServicePrincipal,
                Checksum = ImmutableArray.Create(new byte[32]),
            },
            Checksum = [],
        }, Serializer.ActivationSmokeInput, Serializer.ActivationSmokeResult,
        [BaseModuleDtoPropertyBinding.Create<ActivationSmokeInput, string>("hpd.base.sqlite.aot.activation.input.value", "value", BaseGeneratedModuleScalarManifest.Primitive<string>())],
        [BaseModuleDtoPropertyBinding.Create<ActivationSmokeResult, string>("hpd.base.sqlite.aot.activation.result.value", "value", BaseGeneratedModuleScalarManifest.Primitive<string>())],
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
    [BaseField("hpd.base.sqlite.aot.activation.input.value")]
    public required string Value { get; init; }
}

internal sealed record ActivationSmokeResult
{
    [BaseField("hpd.base.sqlite.aot.activation.result.value")]
    public required string Value { get; init; }
}

internal sealed class ActivationSmokeHandler : IBaseActivationHandler<ActivationSmokeInput, ActivationSmokeResult>
{
    public async ValueTask<BaseActivationHandlerResult<ActivationSmokeResult>> ExecuteAsync(
        BaseActivationContext context, ActivationSmokeInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int separator = input.Value.IndexOf(':');
        if (separator > 0 && input.Value[..separator] is "ensure" or "retire")
        {
            string operation = input.Value[..separator];
            var request = new SemanticMutationSmokeRequest { Marker = input.Value[(separator + 1)..] };
            BaseMutationRequestFingerprint fingerprint = BaseMutationRequestFingerprint.Create(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input.Value)));
            BaseSemanticActivationKey<SemanticActivationSmokeMarker> key = context.CreateSemanticActivationKey(
                SemanticActivationSmoke.Identity, request);
            BaseModuleMutationExecutionOptions options = operation == "ensure"
                ? context.GuardModuleMutationAndEnsureActivation(
                    "semantic-ensure", 1, fingerprint, ActivationSmoke.Registration.Identity,
                    new ActivationSmokeInput { Value = "semantic-child:" + request.Marker }, null, key)
                : context.GuardModuleMutationAndRetireSemanticActivation("semantic-retire", 1, fingerprint, key);
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
                "sqlite-aot-semantic", operation, context.Claim.ActivationId + ":" + operation + ":" + request.Marker, fingerprint);
            BaseResult<BaseModuleMutationExecutionResult<SemanticEnsureSmokeResult>>? ensureResult = operation == "ensure"
                ? await context.ExecuteModuleMutationAsync(SemanticEnsureMutationSmoke.Identity, request, identity, options, cancellationToken)
                : null;
            BaseResult<BaseModuleMutationExecutionResult<SemanticRetireSmokeResult>>? retireResult = operation == "retire"
                ? await context.ExecuteModuleMutationAsync(SemanticRetirementMutationSmoke.Identity, request, identity, options, cancellationToken)
                : null;
            BaseError? error = ensureResult is BaseFailure<BaseModuleMutationExecutionResult<SemanticEnsureSmokeResult>> ensureFailure
                ? ensureFailure.Error
                : retireResult is BaseFailure<BaseModuleMutationExecutionResult<SemanticRetireSmokeResult>> retireFailure
                    ? retireFailure.Error : null;
            if (error is not null)
                return new BaseActivationHandlerResult<ActivationSmokeResult> { FailureCode = error.Code, Retryable = false };
        }
        return new BaseActivationHandlerResult<ActivationSmokeResult>
        { Result = new ActivationSmokeResult { Value = input.Value } };
    }
}
