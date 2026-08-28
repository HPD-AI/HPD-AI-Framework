using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed record AuthUserCleanupInputV1
{
    [BaseField("auth.activation.cleanup.user.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.activation.cleanup.user.subjectContractId", MaximumUtf8Bytes = 128)] public required string SubjectContractId { get; init; }
    [BaseField("auth.activation.cleanup.user.subjectContractVersion", MinimumInt32 = 1, HasMinimumInt32 = true)] public required int SubjectContractVersion { get; init; }
    [BaseField("auth.activation.cleanup.user.subjectContractChecksum", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string SubjectContractChecksum { get; init; }
    [BaseField("auth.activation.cleanup.user.subjectId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid SubjectId { get; init; }
    [BaseField("auth.activation.cleanup.user.subject"), BaseSubjectReference(typeof(AuthUserSubject), Requirement = BaseSubjectReferenceRequirement.Exists, Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot)] public required BaseSubjectReference<AuthUserSubject> Subject { get; init; }
    [BaseField("auth.activation.cleanup.user.incarnation")] public required BaseSubjectIncarnation Incarnation { get; init; }
    [BaseField("auth.activation.cleanup.user.tombstoneSequence", MinimumInt64 = 1, HasMinimumInt64 = true)] public required long TombstoneSequence { get; init; }
    [BaseField("auth.activation.cleanup.user.tombstoneRevision", MaximumUtf8Bytes = 256)] public required string TombstoneRevision { get; init; }
    [BaseField("auth.activation.cleanup.user.barrierId", MaximumUtf8Bytes = 128)] public required string BarrierId { get; init; }
    [BaseField("auth.activation.cleanup.user.barrierChecksum", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string BarrierChecksum { get; init; }
    [BaseField("auth.activation.cleanup.user.workflowVersion", MinimumInt32 = 1, HasMinimumInt32 = true, MaximumInt32 = 1, HasMaximumInt32 = true)] public required int WorkflowVersion { get; init; }
}

internal sealed record AuthRoleCleanupInputV1
{
    [BaseField("auth.activation.cleanup.role.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.activation.cleanup.role.subjectContractId", MaximumUtf8Bytes = 128)] public required string SubjectContractId { get; init; }
    [BaseField("auth.activation.cleanup.role.subjectContractVersion", MinimumInt32 = 1, HasMinimumInt32 = true)] public required int SubjectContractVersion { get; init; }
    [BaseField("auth.activation.cleanup.role.subjectContractChecksum", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string SubjectContractChecksum { get; init; }
    [BaseField("auth.activation.cleanup.role.subjectId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid SubjectId { get; init; }
    [BaseField("auth.activation.cleanup.role.subject"), BaseSubjectReference(typeof(AuthRoleSubject), Requirement = BaseSubjectReferenceRequirement.Exists, Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot)] public required BaseSubjectReference<AuthRoleSubject> Subject { get; init; }
    [BaseField("auth.activation.cleanup.role.incarnation")] public required BaseSubjectIncarnation Incarnation { get; init; }
    [BaseField("auth.activation.cleanup.role.tombstoneSequence", MinimumInt64 = 1, HasMinimumInt64 = true)] public required long TombstoneSequence { get; init; }
    [BaseField("auth.activation.cleanup.role.tombstoneRevision", MaximumUtf8Bytes = 256)] public required string TombstoneRevision { get; init; }
    [BaseField("auth.activation.cleanup.role.barrierId", MaximumUtf8Bytes = 128)] public required string BarrierId { get; init; }
    [BaseField("auth.activation.cleanup.role.barrierChecksum", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string BarrierChecksum { get; init; }
    [BaseField("auth.activation.cleanup.role.workflowVersion", MinimumInt32 = 1, HasMinimumInt32 = true, MaximumInt32 = 1, HasMaximumInt32 = true)] public required int WorkflowVersion { get; init; }
}

internal sealed record AuthCleanupResultV1
{
    [BaseField("auth.activation.cleanup.result.completed")] public required bool Completed { get; init; }
    [BaseField("auth.activation.cleanup.result.state", AllowedEnumLiterals = ["awaitingSemanticRetirement", "complete", "draining", "readyToPurge", "waitingRetention"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupStateV1>))] public required AuthCleanupStateV1 State { get; init; }
    [BaseField("auth.activation.cleanup.result.step", AllowedEnumLiterals = ["deleteDeliveries", "deletePasskeys", "deleteRefreshTokens", "deleteRoleClaims", "deleteSessions", "deleteUserClaims", "deleteUserIdentities", "deleteUserLogins", "deleteUserRoles", "deleteUserTokens", "finalizeSubject", "proveEmpty", "revokeRefreshTokens", "revokeSessions", "waitSecurityRetention"]), JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupStepV1>))] public required AuthCleanupStepV1 Step { get; init; }
    [BaseField("auth.activation.cleanup.result.chunkOrdinal", MinimumInt64 = 0, HasMinimumInt64 = true)] public required long ChunkOrdinal { get; init; }
    [BaseField("auth.activation.cleanup.result.selectedCount", MinimumInt32 = 0, HasMinimumInt32 = true, MaximumInt32 = 200, HasMaximumInt32 = true)] public required int SelectedCount { get; init; }
    [BaseField("auth.activation.cleanup.result.retentionEligibleAt", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.NonNullable), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public DateTimeOffset? RetentionEligibleAt { get; init; }
}

internal sealed class AuthCleanupDeclarationHandler<TInput> : IBaseActivationHandler<TInput, AuthCleanupResultV1>
{
    public ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> ExecuteAsync(
        BaseActivationContext context, TInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new BaseActivationHandlerResult<AuthCleanupResultV1>
        {
            FailureCode = "auth.persistence.unavailable",
            Retryable = true,
        });
    }
}

[BaseActivationDtoAuthority(
    "hpd.auth.cleanup.user.dto.v1", 1, AuthBaseContract.ModuleId,
    "hpd.auth.type.auth-user-cleanup-input-v1.v1", "hpd.auth.type.auth-cleanup-result-v1.v1",
    typeof(AuthBaseJsonSerializerContext), typeof(AuthUserCleanupInputV1), typeof(AuthCleanupResultV1))]
internal static partial class AuthUserCleanupActivationDtos;

[BaseActivationDtoAuthority(
    "hpd.auth.cleanup.role.dto.v1", 1, AuthBaseContract.ModuleId,
    "hpd.auth.type.auth-role-cleanup-input-v1.v1", "hpd.auth.type.auth-cleanup-result-v1.v1",
    typeof(AuthBaseJsonSerializerContext), typeof(AuthRoleCleanupInputV1), typeof(AuthCleanupResultV1))]
internal static partial class AuthRoleCleanupActivationDtos;

internal static class AuthCleanupActivationDeclarations
{
    internal static BaseActivationHandlerRegistration<AuthUserCleanupInputV1, AuthCleanupResultV1> User { get; } =
        Create("hpd.auth.cleanup.user.v1", "user", AuthUserCleanupActivationDtos.HPDBaseActivationDtoAuthority,
            static _ => new AuthCleanupDeclarationHandler<AuthUserCleanupInputV1>());

    internal static BaseActivationHandlerRegistration<AuthRoleCleanupInputV1, AuthCleanupResultV1> Role { get; } =
        Create("hpd.auth.cleanup.role.v1", "role", AuthRoleCleanupActivationDtos.HPDBaseActivationDtoAuthority,
            static _ => new AuthCleanupDeclarationHandler<AuthRoleCleanupInputV1>());

    private static BaseActivationHandlerRegistration<TInput, AuthCleanupResultV1> Create<TInput>(
        string id, string suffix, BaseGeneratedActivationDtoAuthority<TInput, AuthCleanupResultV1> authority,
        Func<IServiceProvider, IBaseActivationHandler<TInput, AuthCleanupResultV1>> factory) =>
        AuthActivationDefinitionFactory.Create(
            id, $"hpd.auth.handler.cleanup.{suffix}", $"hpd.auth.factory.cleanup.{suffix}.v1",
            SourceGrants(suffix), authority, factory);

    private static string[] SourceGrants(string suffix) => suffix == "user"
        ? ["auth.cleanup.execute", "auth.operation.cleanup.advance", "auth.operation.cleanup.prepareRetirement", "auth.session.mutate", "auth.token.delivery", "auth.token.mutate", "base.subjectRetirement.barrier.inspect", "base.subjectRetirement.purge", "hpd.auth.cleanup.semantic-retire.user.v1.enqueue"]
        : ["auth.cleanup.execute", "auth.identity.mutate", "auth.operation.cleanup.advance", "auth.operation.cleanup.prepareRetirement", "base.subjectRetirement.barrier.inspect", "base.subjectRetirement.purge", "hpd.auth.cleanup.semantic-retire.role.v1.enqueue"];

}

internal static class AuthActivationDefinitionFactory
{
    internal static BaseActivationHandlerRegistration<TInput, TResult> Create<TInput, TResult>(
        string id,
        string handlerId,
        string factoryId,
        IEnumerable<string> sourceGrants,
        BaseGeneratedActivationDtoAuthority<TInput, TResult> authority,
        Func<IServiceProvider, IBaseActivationHandler<TInput, TResult>> factory,
        bool semanticRetirement = false,
        bool reconciliation = false) =>
        BaseActivationDefinitionBuilder.CreateGenerated(new BaseActivationDefinitionDraft
        {
            Id = id,
            Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId,
            ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
            Grants = Grants(id),
            SourceGrantIds = [.. sourceGrants.Order(StringComparer.Ordinal)],
            Retry = Retry(semanticRetirement),
            Limits = Limits(reconciliation),
            Handler = new BaseActivationHandlerDraft
            {
                Id = handlerId,
                Version = 1,
                FactoryId = factoryId,
                WorkerSubjectKind = AccessSubjectKind.System,
                SemanticAuthority = BaseActivationHandlerSemanticAuthority.Create(handlerId + ".semantics", 1),
            },
        }, authority, factory);

    private static BaseActivationGrantSet Grants(string id) => new()
    {
        Enqueue = id + ".enqueue", Observe = id + ".observe", Claim = id + ".claim", Execute = id + ".execute",
        Renew = id + ".renew", Complete = id + ".complete", Fail = id + ".fail", Cancel = id + ".cancel",
        Inspect = id + ".inspect", Replay = id + ".replay", Migrate = id + ".migrate", Reconcile = id + ".reconcile",
        Retry = id + ".retry", Dispose = id + ".dispose", Remove = id + ".remove", Repair = id + ".repair",
    };

    private static BaseActivationRetryProfile Retry(bool semanticRetirement) => new()
    {
        MaximumAttempts = 10, InitialDelayMilliseconds = 1_000, MaximumDelayMilliseconds = 300_000,
        MultiplierNumerator = 2, MultiplierDenominator = 1, JitterBasisPoints = 1_000,
        RetryableFailureCodes = semanticRetirement
            ? ["auth.cleanup.semanticRetirementPending", "auth.persistence.unavailable"]
            : ["auth.persistence.unavailable"],
    };

    private static BaseActivationLimits Limits(bool reconciliation) => new()
    {
        MaximumInputBytes = 65_536, MaximumResultBytes = 65_536, MaximumAttempts = 10,
        MaximumRenewalsPerAttempt = 3, MaximumChildrenPerAttempt = reconciliation ? 804 : 8, MaximumLineageDepth = 4,
        LeaseDuration = TimeSpan.FromSeconds(30), HandlerTimeout = TimeSpan.FromSeconds(20),
        Provider = new BaseActivationExecutionLimits
        {
            MaximumCandidates = reconciliation ? 200 : 64, MaximumInputBytes = 65_536, MaximumResultBytes = 65_536,
            MaximumEvidenceBytes = 1_048_576, MaximumTransientBytes = 16_777_216,
            MaximumReadIntervals = reconciliation ? 256 : 64, MaximumIndexOperations = reconciliation ? 2_048 : 256,
            AcquisitionTimeout = TimeSpan.FromSeconds(2), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(2), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
        AtomicCreation = AtomicLimits(),
    };

    private static BaseAtomicMutationExecutionLimits AtomicLimits() => new()
    {
        MaximumItems = 16, MaximumQueryNodes = 24, MaximumQueryDepth = 8, MaximumLiteralValues = 32,
        MaximumSelectedRecords = 200, MaximumProducedMutations = 200, MaximumQueryExecutions = 1,
        MaximumPreviousStateRequirements = 8, MaximumRecordCaptures = 16, MaximumRelationTargetCaptures = 16,
        MaximumGenerationReads = 4, MaximumGenerationComparisons = 4, MaximumGenerationIncrements = 4,
        MaximumGuardNodes = 64, MaximumGuardDepth = 8, MaximumStatements = 24, MaximumBranches = 8,
        MaximumExpressionNodes = 128, MaximumSelectedBytes = 1_048_576, MaximumEvidenceBytes = 2_097_152,
        MaximumTransientBytes = 8_388_608, MaximumReadIntervals = 64, MaximumSubjectValidations = 8,
        MaximumAuthorityReads = 64, MaximumRelationChecks = 400, MaximumUniqueConstraintChecks = 400,
        MaximumRetirementProjections = 4, MaximumRetirementBarrierReads = 4,
        MaximumRetirementAcknowledgementReads = 4, MaximumRetirementPublications = 4,
        MaximumRequestBytes = 262_144, MaximumGenerationBytes = 65_536, MaximumWrittenBytes = 1_048_576,
        MaximumFactBytes = 2_097_152, MaximumJournalBytes = 2_621_440, MaximumReceiptBytes = 2_621_440,
        MaximumResultBytes = 65_536, MaximumRetirementEvidenceBytes = 1_048_576,
        MaximumRetirementPublicationBytes = 1_048_576,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(2), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(2), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        },
    };

}
