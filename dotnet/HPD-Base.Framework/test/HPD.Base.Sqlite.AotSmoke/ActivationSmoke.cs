namespace HPD.Base.Sqlite.AotSmoke;

internal static class ActivationSmoke
{
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
        "hpd.base.sqlite.aot.activation.target.enqueue", "hpd.base.sqlite.aot.activation.target.observe",
        "hpd.base.sqlite.aot.activation.target.claim", "hpd.base.sqlite.aot.activation.target.execute",
        "hpd.base.sqlite.aot.activation.target.renew", "hpd.base.sqlite.aot.activation.target.complete",
        "hpd.base.sqlite.aot.activation.target.fail", "hpd.base.sqlite.aot.activation.target.cancel",
        "hpd.base.sqlite.aot.activation.target.inspect", "hpd.base.sqlite.aot.activation.target.replay",
        "hpd.base.sqlite.aot.activation.target.migrate", "hpd.base.sqlite.aot.activation.target.reconcile",
        "hpd.base.sqlite.aot.activation.target.retry", "hpd.base.sqlite.aot.activation.target.dispose",
        "hpd.base.sqlite.aot.activation.target.remove", "hpd.base.sqlite.aot.activation.target.repair",
    ];

    internal static BaseActivationHandlerRegistration<ActivationSmokeInput, ActivationSmokeResult> Registration { get; } =
        BaseActivationDefinitionBuilder.CreateGenerated(new BaseActivationDefinitionDraft
        {
            Id = "hpd.base.sqlite.aot.activation", Version = 1, OwningModuleId = "hpd.base.sqlite.aot",
            ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
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
            Handler = new BaseActivationHandlerDraft
            {
                Id = "hpd.base.sqlite.aot.activation.handler", Version = 1,
                FactoryId = "hpd.base.sqlite.aot.activation.handler.factory",
                WorkerSubjectKind = AccessSubjectKind.ServicePrincipal,
                SemanticAuthority = BaseActivationHandlerSemanticAuthority.Create("hpd.base.sqlite.aot.activation.handler.semantics", 1),
            },
        }, ActivationSmokeDtos.HPDBaseActivationDtoAuthority, static _ => new ActivationSmokeHandler());

    internal static BaseActivationHandlerRegistration<ActivationMigrationTargetInput, ActivationSmokeResult> MigrationTargetRegistration { get; } =
        BaseActivationDefinitionBuilder.CreateGenerated(new BaseActivationDefinitionDraft
        {
            Id = "hpd.base.sqlite.aot.activation.target", Version = 1, OwningModuleId = "hpd.base.sqlite.aot",
            ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
            Grants = new BaseActivationGrantSet
            {
                Enqueue = GrantIds[16], Observe = GrantIds[17], Claim = GrantIds[18], Execute = GrantIds[19],
                Renew = GrantIds[20], Complete = GrantIds[21], Fail = GrantIds[22], Cancel = GrantIds[23],
                Inspect = GrantIds[24], Replay = GrantIds[25], Migrate = GrantIds[26], Reconcile = GrantIds[27],
                Retry = GrantIds[28], Dispose = GrantIds[29], Remove = GrantIds[30], Repair = GrantIds[31],
            },
            SourceGrantIds = [],
            Retry = new BaseActivationRetryProfile
            {
                MaximumAttempts = 1, InitialDelayMilliseconds = 1, MaximumDelayMilliseconds = 1,
                MultiplierNumerator = 1, MultiplierDenominator = 1, JitterBasisPoints = 0,
                RetryableFailureCodes = [],
            },
            Limits = new BaseActivationLimits
            {
                MaximumInputBytes = 4096, MaximumResultBytes = 4096, MaximumAttempts = 1,
                MaximumRenewalsPerAttempt = 1, MaximumChildrenPerAttempt = 1, MaximumLineageDepth = 1,
                LeaseDuration = TimeSpan.FromMinutes(1), HandlerTimeout = TimeSpan.FromSeconds(5),
                Provider = ProviderLimits(), AtomicCreation = AtomicLimits(),
            },
            Handler = new BaseActivationHandlerDraft
            {
                Id = "hpd.base.sqlite.aot.activation.target.handler", Version = 1,
                FactoryId = "hpd.base.sqlite.aot.activation.target.handler.factory",
                WorkerSubjectKind = AccessSubjectKind.ServicePrincipal,
                SemanticAuthority = BaseActivationHandlerSemanticAuthority.Create(
                    "hpd.base.sqlite.aot.activation.target.handler.semantics", 1),
            },
        }, ActivationMigrationTargetDtos.HPDBaseActivationDtoAuthority,
            static _ => new ActivationMigrationTargetHandler());

    internal static BaseActivationMigrationRegistration<ActivationSmokeInput, ActivationMigrationTargetInput> Migration { get; } =
        BaseActivationMigrationBuilder
            .From(Registration, ActivationSmokeDtos.HPDBaseActivationDtoAuthority)
            .To(MigrationTargetRegistration, ActivationMigrationTargetDtos.HPDBaseActivationDtoAuthority)
            .Map(ActivationMigrationTargetDtos.InputProperties.Value, ActivationSmokeDtos.InputProperties.Value)
            .Constant(ActivationMigrationTargetDtos.InputProperties.TenantId,
                Guid.Parse("9ca52180-5f5e-497f-81cc-99f4a606bcd4"))
            .Constant(ActivationMigrationTargetDtos.InputProperties.Nonce, BaseBinary.From([1, 2, 3, 4]))
            .Constant(ActivationMigrationTargetDtos.InputProperties.Mode, ActivationMigrationMode.Active)
            .Create(new BaseActivationMigrationDraft
            {
                Id = "hpd.base.sqlite.aot.activation.migration", Version = 1,
                OwningModuleId = "hpd.base.sqlite.aot", GrantId = GrantIds[10],
            });

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

[BaseActivationDtoAuthority("hpd.base.sqlite.aot.activation.dto", 1, "hpd.base.sqlite.aot",
    "hpd.base.sqlite.aot.activation.input", "hpd.base.sqlite.aot.activation.result",
    typeof(SemanticSmokeJsonContext), typeof(ActivationSmokeInput), typeof(ActivationSmokeResult))]
internal static partial class ActivationSmokeDtos;

[BaseActivationDtoAuthority("hpd.base.sqlite.aot.activation.target.dto", 1, "hpd.base.sqlite.aot",
    "hpd.base.sqlite.aot.activation.target.input", "hpd.base.sqlite.aot.activation.result",
    typeof(SemanticSmokeJsonContext), typeof(ActivationMigrationTargetInput), typeof(ActivationSmokeResult))]
internal static partial class ActivationMigrationTargetDtos;

internal sealed record ActivationSmokeInput
{
    [BaseField("hpd.base.sqlite.aot.activation.input.value", MaximumUtf8Bytes = 256)]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string Value { get; init; }
}

internal sealed record ActivationSmokeResult
{
    [BaseField("hpd.base.sqlite.aot.activation.result.value", MaximumUtf8Bytes = 256)]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string Value { get; init; }
}

internal enum ActivationMigrationMode
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("active")] Active,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("passive")] Passive,
}

internal sealed record ActivationMigrationTargetInput
{
    [BaseField("hpd.base.sqlite.aot.activation.input.value", MaximumUtf8Bytes = 256)]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string Value { get; init; }

    [BaseField("hpd.base.sqlite.aot.activation.target.tenant")]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    [System.Text.Json.Serialization.JsonConverter(typeof(BaseCanonicalGuidJsonConverter))]
    public required Guid TenantId { get; init; }

    [BaseField("hpd.base.sqlite.aot.activation.target.nonce", MinimumBytes = 4, MaximumBytes = 4)]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required BaseBinary Nonce { get; init; }

    [BaseField("hpd.base.sqlite.aot.activation.target.mode", AllowedEnumLiterals = ["active", "passive"])]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    [System.Text.Json.Serialization.JsonConverter(typeof(BaseClosedEnumJsonConverter<ActivationMigrationMode>))]
    public required ActivationMigrationMode Mode { get; init; }
}

internal sealed class ActivationMigrationTargetHandler : IBaseActivationHandler<ActivationMigrationTargetInput, ActivationSmokeResult>
{
    public ValueTask<BaseActivationHandlerResult<ActivationSmokeResult>> ExecuteAsync(
        BaseActivationContext context, ActivationMigrationTargetInput input, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new BaseActivationHandlerResult<ActivationSmokeResult>
        {
            Result = new ActivationSmokeResult { Value = input.Value },
        });
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
