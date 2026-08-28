using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

internal static class ProofActivation
{
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Continuations = new();

    internal static readonly string[] GrantIds =
    [
        "proof.activation.enqueue", "proof.activation.observe", "proof.activation.claim",
        "proof.activation.execute", "proof.activation.renew", "proof.activation.complete",
        "proof.activation.fail", "proof.activation.cancel", "proof.activation.inspect",
        "proof.activation.replay", "proof.activation.migrate", "proof.activation.reconcile",
        "proof.activation.retry", "proof.activation.dispose", "proof.activation.remove",
        "proof.activation.repair",
    ];

    internal static BaseActivationHandlerRegistration<ProofActivationInput, ProofActivationResult> Registration { get; } =
        BaseActivationDefinitionBuilder.CreateGenerated(new BaseActivationDefinitionDraft
        {
            Id = "proof.activation.v1", Version = 1, OwningModuleId = "proof.module",
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
                RetryableFailureCodes = ["proof.activation.retryable"],
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
                Id = "proof.activation.handler", Version = 1, FactoryId = "proof.activation.handler.factory",
                WorkerSubjectKind = AccessSubjectKind.ServicePrincipal,
                SemanticAuthority = BaseActivationHandlerSemanticAuthority.Create("proof.activation.handler.semantics", 1),
            },
        }, ProofActivationDtos.HPDBaseActivationDtoAuthority, static _ => new ProofActivationHandler());

    internal const string ScheduleManageGrant = "proof.schedule.manage";
    internal const string ScheduleMaterializeGrant = "proof.schedule.materialize";

    internal static BaseGeneratedScheduleRegistration Schedule { get; } = CreateSchedule();

    internal static BaseScheduleRegistrationIdentity ScheduleIdentity => Schedule.Identity;

    internal static void ObserveContinuation(string value) => Continuations.Enqueue(value);

    internal static string[] DrainContinuations()
    {
        var values = new List<string>();
        while (Continuations.TryDequeue(out string? value)) values.Add(value);
        return [.. values];
    }

    private static BaseGeneratedScheduleRegistration CreateSchedule()
    {
        return BaseScheduleDefinitionBuilder.CreateGenerated(new BaseScheduleDefinitionDraft
        {
            Id = "proof.schedule.v1",
            Version = 1,
            OwningModuleId = "proof.module",
            ManageGrantId = ScheduleManageGrant,
            MaterializeGrantId = ScheduleMaterializeGrant,
            Expression = new BaseOnceSchedule(1),
            GapPolicy = BaseTimeGapPolicy.Skip,
            TimeOverlapPolicy = BaseTimeOverlapPolicy.EarlierOffset,
            MisfirePolicy = BaseScheduleMisfirePolicy.RunAll,
            ActivationOverlapPolicy = BaseScheduleOverlapPolicy.Allow,
            OverlapKeyKind = BaseScheduleOverlapKeyKind.Schedule,
            ConcurrencyKey = [],
            Priority = 0,
            MaximumSplayMilliseconds = 0,
        }, Registration, ProofActivationDtos.HPDBaseActivationDtoAuthority,
            new ProofActivationInput { Value = "scheduled-static" });
    }

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
        MaximumItems = 8, MaximumQueryNodes = 8, MaximumQueryDepth = 4, MaximumLiteralValues = 8,
        MaximumSelectedRecords = 8, MaximumProducedMutations = 8, MaximumQueryExecutions = 8,
        MaximumPreviousStateRequirements = 8, MaximumRecordCaptures = 8,
        MaximumRelationTargetCaptures = 8, MaximumSelectedBytes = 4096, MaximumEvidenceBytes = 8192,
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

[BaseActivationDtoAuthority(
    "proof.activation.dto.v1", 1, "proof.module", "proof.activation.input", "proof.activation.result",
    typeof(ConsumerJsonSerializerContext), typeof(ProofActivationInput), typeof(ProofActivationResult))]
internal static partial class ProofActivationDtos;

internal sealed record ProofActivationInput
{
    [BaseField("proof.activation.input.value", MaximumUtf8Bytes = 256)]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string Value { get; init; }
}

internal sealed record ProofActivationResult
{
    [BaseField("proof.activation.result.value", MaximumUtf8Bytes = 256)]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string Value { get; init; }
}

internal sealed class ProofActivationHandler : IBaseActivationHandler<ProofActivationInput, ProofActivationResult>
{
    public async ValueTask<BaseActivationHandlerResult<ProofActivationResult>> ExecuteAsync(
        BaseActivationContext context, ProofActivationInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Value.StartsWith("continuation-child:", StringComparison.Ordinal))
            ProofActivation.ObserveContinuation(input.Value);
        if (input.Value.StartsWith("continuation-root:", StringComparison.Ordinal))
        {
            string targetId = input.Value["continuation-root:".Length..];
            var request = new RequestControlRequest
            {
                Accepted = true,
                EnableHostile = false,
                HostileId = " ",
                Left = 1,
                Right = 2,
                Name = "continued",
                OptionalNote = null,
                OwnerId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
                TargetId = targetId,
            };
            BaseMutationRequestFingerprint fingerprint = BaseMutationRequestFingerprint.Create(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input.Value)));
            BaseMutationRequestIdentity identity = context.DeriveChildIdentity("continuation-state", 1, fingerprint);
            BaseModuleMutationExecutionOptions options = context.GuardModuleMutationAndCreateActivation(
                "continuation-state", 1, fingerprint, ProofActivation.Registration.Identity,
                new ProofActivationInput { Value = "continuation-child:" + targetId }, 1,
                "continuation-activation", 1);
            BaseResult<BaseModuleMutationExecutionResult<RequestControlResult>> first =
                await context.ExecuteModuleMutationAsync(
                    RequestControlProof.Identity, request, identity, options, cancellationToken);
            if (first is not BaseSuccess<BaseModuleMutationExecutionResult<RequestControlResult>>)
            {
                ProofActivation.ObserveContinuation("error:"
                    + (first as BaseFailure<BaseModuleMutationExecutionResult<RequestControlResult>>)?.Error.Code);
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = "proof.activation.continuationFailed", Retryable = false };
            }
        }
        if (input.Value.StartsWith("l50-child:", StringComparison.Ordinal))
        {
            string targetId = input.Value["l50-child:".Length..];
            Guid ownerId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            var request = new RequestControlRequest
            {
                Accepted = true, EnableHostile = false, HostileId = " ", Left = 1, Right = 2,
                Name = "activation-child", OptionalNote = null, OwnerId = ownerId, TargetId = targetId,
            };
            BaseMutationRequestFingerprint fingerprint = BaseMutationRequestFingerprint.Create(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input.Value)));
            BaseMutationRequestIdentity identity = context.DeriveChildIdentity("l50-child", 1, fingerprint);
            BaseModuleMutationExecutionOptions options = context.GuardModuleMutation("l50-child", 1, fingerprint);
            BaseResult<BaseModuleMutationExecutionResult<RequestControlResult>> result =
                await context.ExecuteModuleMutationAsync(
                    RequestControlProof.Identity, request, identity, options, cancellationToken);
            if (result is BaseFailure<BaseModuleMutationExecutionResult<RequestControlResult>> failure)
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = failure.Error.Code, Retryable = false };
            BaseMutationRequestFingerprint conflict = BaseMutationRequestFingerprint.Create(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input.Value + ":conflict")));
            try
            {
                _ = context.GuardModuleMutation("l50-child", 1, conflict);
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = "proof.activation.conflictMissing", Retryable = false };
            }
            catch (InvalidOperationException exception) when (
                string.Equals(exception.Message, "base.activation.childIdentityConflict", StringComparison.Ordinal))
            {
            }
        }
        if (input.Value.StartsWith("l43-delete:", StringComparison.Ordinal))
        {
            string cohort = input.Value["l43-delete:".Length..];
            BaseCollectionSession<ProofSelectionItem> work = context.Collection(ProofSelectionItem.Collection);
            BaseMutationRequestFingerprint fingerprint = BaseMutationRequestFingerprint.Create(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input.Value)));
            BaseMutationRequestIdentity identity = context.DeriveChildIdentity("l43-delete", 1, fingerprint);
            BaseSelectionMutationExecutionOptions options = context.GuardSelectionMutation(
                "l43-delete", 1, identity);
            BaseDeleteSelectionProfile<ProofSelectionItem> profile = work.GetDeleteSelectionProfile(SelectionProof.Identity);
            BaseResult<BaseSelectionMutationResult> result = await work.Query()
                .Where(ProofSelectionItem.Fields.Name.Equal(cohort))
                .OrderBy(ProofSelectionItem.Fields.Name).ThenByRecordId().Take(8)
                .DeleteSelectedAsync(profile, BasePreviousStateRequirement.None, identity, options, cancellationToken);
            if (result is BaseFailure<BaseSelectionMutationResult> failure)
            {
                ProofActivation.ObserveContinuation("l43-error:" + failure.Error.Code);
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = failure.Error.Code, Retryable = false };
            }
        }
        if (input.Value.StartsWith("l30-remove:", StringComparison.Ordinal))
        {
            string recordId = input.Value["l30-remove:".Length..];
            BaseMutationRequestFingerprint fingerprint = BaseMutationRequestFingerprint.Create(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input.Value)));
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
                "proof-l30", "guarded-removal", recordId, fingerprint);
            BaseBatchBuilder batch = context.GuardRecordMutations("l30-removal", 1, identity);
            batch.PatchRemoving(
                ProofOwner.Collection,
                RecordId.Create(recordId),
                new ProofOwnerPatch(),
                ProofOwnerJsonSerializerContext.Default.ProofOwnerPatch,
                [ProofOwner.Fields.Note.Removal()]);
            BaseResult<BaseBatchResult> result = await batch.CommitAsync(cancellationToken);
            if (result is BaseFailure<BaseBatchResult> failure)
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = failure.Error.Code, Retryable = false };
        }
        if (string.Equals(input.Value, "lifecycle", StringComparison.Ordinal))
        {
            BaseInstalledSubjectRetirementConsumer<ConsumerSubject> retirement =
                context.SubjectRetirements.Get(LifecycleProof.RetirementIdentity);
            await using IAsyncEnumerator<BaseSubjectRequiredLifecycleDelivery<ConsumerSubject>> deliveries =
                retirement.ReadRequiredAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            if (!await deliveries.MoveNextAsync())
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = "proof.lifecycle.deliveryMissing", Retryable = false };
            BaseSubjectRequiredLifecycleDelivery<ConsumerSubject> delivery = deliveries.Current;
            BaseActivationGuard acknowledgementGuard = context.GuardRetirementAcknowledgement(
                "retirement-acknowledgement", 1, delivery.AcknowledgementIdentity);
            BaseResult<BaseSubjectAcknowledgementResult> acknowledged = await retirement.AcknowledgeAsync(
                delivery.Acknowledgement,
                BaseSubjectAcknowledgementDisposition.Completed,
                delivery.AcknowledgementIdentity,
                acknowledgementGuard,
                cancellationToken);
            if (acknowledged is not BaseSuccess<BaseSubjectAcknowledgementResult> acknowledgement)
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = ((BaseFailure<BaseSubjectAcknowledgementResult>)acknowledged).Error.Code, Retryable = false };

            // Response-loss replay is resolved while the delivered lifecycle fact remains
            // unadvanced. Advancing the L47 checkpoint intentionally invalidates the old L48
            // delivery authority.
            BaseResult<BaseSubjectAcknowledgementResult> replayedAcknowledgement = await retirement.AcknowledgeAsync(
                delivery.Acknowledgement, BaseSubjectAcknowledgementDisposition.Completed,
                delivery.AcknowledgementIdentity, acknowledgementGuard, cancellationToken);
            if (replayedAcknowledgement is not BaseSuccess<BaseSubjectAcknowledgementResult> replayedAck)
            {
                LifecycleProof.ObserveError(
                    ((BaseFailure<BaseSubjectAcknowledgementResult>)replayedAcknowledgement).Error.Code);
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = "proof.lifecycle.acknowledgementReplayFailed", Retryable = false };
            }

            BaseInstalledSubjectLifecycleConsumer<ConsumerSubject> lifecycle =
                context.SubjectLifecycle.Get(LifecycleProof.LifecycleIdentity);
            BaseActivationGuard checkpointGuard = context.GuardLifecycleCheckpoint(
                "lifecycle-checkpoint", 2, delivery.Lifecycle.AdvanceIdentity);
            BaseResult<BaseSubjectLifecycleCheckpointResult> advanced = await lifecycle.AdvanceAsync(
                delivery.Lifecycle.Checkpoint,
                delivery.Lifecycle.AdvanceIdentity,
                checkpointGuard,
                cancellationToken);
            if (advanced is not BaseSuccess<BaseSubjectLifecycleCheckpointResult> checkpoint)
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = ((BaseFailure<BaseSubjectLifecycleCheckpointResult>)advanced).Error.Code, Retryable = false };
            LifecycleProof.Observe(acknowledgement.Value, checkpoint.Value);

            BaseResult<BaseSubjectLifecycleCheckpointResult> replayedCheckpoint = await lifecycle.AdvanceAsync(
                delivery.Lifecycle.Checkpoint, delivery.Lifecycle.AdvanceIdentity,
                checkpointGuard, cancellationToken);
            if (replayedCheckpoint is not BaseSuccess<BaseSubjectLifecycleCheckpointResult> replayedAdvance)
            {
                LifecycleProof.ObserveError(
                    ((BaseFailure<BaseSubjectLifecycleCheckpointResult>)replayedCheckpoint).Error.Code);
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = "proof.lifecycle.replayFailed", Retryable = false };
            }
            LifecycleProof.Observe(replayedAck.Value, replayedAdvance.Value);
        }
        if (string.Equals(input.Value, "lifecycle-checkpoint", StringComparison.Ordinal))
        {
            BaseInstalledSubjectLifecycleConsumer<ConsumerSubject> lifecycle =
                context.SubjectLifecycle.Get(LifecycleProof.LifecycleIdentity);
            await using IAsyncEnumerator<BaseSubjectLifecycleDelivery<ConsumerSubject>> deliveries =
                lifecycle.ReadAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            if (!await deliveries.MoveNextAsync())
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = "proof.lifecycle.deliveryMissing", Retryable = false };
            BaseSubjectLifecycleDelivery<ConsumerSubject> delivery = deliveries.Current;
            BaseResult<BaseSubjectLifecycleCheckpointResult> advanced = await lifecycle.AdvanceAsync(
                delivery.Checkpoint, delivery.AdvanceIdentity,
                context.GuardLifecycleCheckpoint("retired-lifecycle-checkpoint", 1, delivery.AdvanceIdentity),
                cancellationToken);
            if (advanced is BaseFailure<BaseSubjectLifecycleCheckpointResult> failure)
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = failure.Error.Code, Retryable = false };
        }
        int separator = input.Value.IndexOf(':');
        if (separator > 0 && input.Value[..separator] is "ensure" or "retire")
        {
            string operation = input.Value[..separator];
            string subjectId = input.Value[(separator + 1)..];
            BaseResult<ConsumerSubjectAcquire.Row[]> acquired = await context.Reads.ToArrayAsync(
                ConsumerSubjectAcquire.Handle,
                new ConsumerSubjectAcquire
                {
                    SubjectId = BaseRecordId<ConsumerPrivateSubject>.Create(subjectId),
                }, cancellationToken: cancellationToken);
            BaseSubjectReference<ConsumerSubject> subject;
            if (acquired is BaseSuccess<ConsumerSubjectAcquire.Row[]> success && success.Value.Length == 1)
                subject = success.Value[0].Reference;
            else
            {
                if (!SemanticProofRequests.TryGet(subjectId, out BaseSubjectReference<ConsumerSubject> retained))
                    return new BaseActivationHandlerResult<ProofActivationResult>
                    { FailureCode = "proof.semantic.authorityInvalid", Retryable = false };
                subject = retained;
            }
            var request = new SemanticProofRequest
            {
                Subject = subject,
                SubjectId = BaseRecordId<ConsumerPrivateSubject>.Create(subjectId),
                Incarnation = subject.Incarnation,
            };
            BaseMutationRequestFingerprint fingerprint = BaseMutationRequestFingerprint.Create(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input.Value)));
            BaseSemanticActivationKey<ProofSemanticMarker> key = context.CreateSemanticActivationKey(
                SemanticProof.Identity, request);
            BaseModuleMutationExecutionOptions options = operation == "ensure"
                ? context.GuardModuleMutationAndEnsureActivation("semantic-ensure", 1, fingerprint,
                    ProofActivation.Registration.Identity,
                new ProofActivationInput { Value = "semantic-child:" + subjectId }, null, key)
                : context.GuardModuleMutationAndRetireSemanticActivation("semantic-retire", 1, fingerprint, key);
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
                "proof-semantic", operation, context.Claim.ActivationId + ":" + operation + ":" + subjectId,
                fingerprint);
            BaseError? error;
            if (operation == "ensure")
            {
                BaseResult<BaseModuleMutationExecutionResult<SemanticEnsureProofResult>> result =
                    await context.ExecuteModuleMutationAsync(
                        SemanticEnsureProof.Identity, request, identity, options, cancellationToken);
                if (result is BaseSuccess<BaseModuleMutationExecutionResult<SemanticEnsureProofResult>> ensured)
                    SemanticProofObservations.Add(ensured.Value.Result);
                error = result is BaseFailure<BaseModuleMutationExecutionResult<SemanticEnsureProofResult>> failure
                    ? failure.Error : null;
            }
            else
            {
                BaseResult<BaseModuleMutationExecutionResult<SemanticRetireProofResult>> result =
                    await context.ExecuteModuleMutationAsync(
                        SemanticRetireProof.Identity, request, identity, options, cancellationToken);
                if (result is BaseSuccess<BaseModuleMutationExecutionResult<SemanticRetireProofResult>> retired)
                    SemanticProofObservations.Add(retired.Value.Result);
                error = result is BaseFailure<BaseModuleMutationExecutionResult<SemanticRetireProofResult>> failure
                    ? failure.Error : null;
            }
            if (error is not null)
            {
                SemanticProofObservations.Add(error);
                return new BaseActivationHandlerResult<ProofActivationResult>
                { FailureCode = error.Code, Retryable = false };
            }
        }
        return new BaseActivationHandlerResult<ProofActivationResult>
        {
            Result = new ProofActivationResult
            {
                Value = input.Value == "invalid-handler-result" ? new string('x', 257) : input.Value,
            },
        };
    }
}
