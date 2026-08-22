using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base.Testing;

internal static class BaseActivationCertificationScenario
{
    internal static async ValueTask<(bool Passed, OperationStatus Status, string? ErrorCode, byte[] Trace)> ExecuteAsync(
        IBaseActivationProvider provider,
        IAtomicRecordStore atomicStore,
        BaseActivationCertificationCaseRequest request,
        CancellationToken cancellationToken)
    {
        string suffix = request.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        long now = checked(10_000L + request.Ordinal * 1_000L);
        BaseActivationExecutionLimits limits = ActivationLimits();
        BaseActivationDefinitionKey definition = Definition(suffix);
        BaseOwnedScopeSeekAuthority scope = Scope();
        var trace = new List<string>();

        if (request.Id == "executor-register-heartbeat-retire")
            return await ExecutorCaseAsync(provider, request, now, limits, trace, cancellationToken).ConfigureAwait(false);

        OperationResult<BaseAtomicMutationAuthorityRequirement> authority = await atomicStore
            .CaptureAtomicMutationAuthorityRequirementAsync(
                "base.activation.certification", [], MutationLimits(), cancellationToken).ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null)
            return Failed(authority.Status, authority.Error?.Code, trace);
        string requestId = "create-" + suffix;
        RecordMutationExecutionResult created = await atomicStore.ExecuteAtomicAsync(
            new CreationProcessor(authority.Value, MutationLimits(), definition, scope, now, requestId),
            MutationExecutionRequest(), cancellationToken).ConfigureAwait(false);
        trace.Add("create:" + created.Outcome);
        if (created.Outcome != RecordMutationExecutionOutcome.Committed)
            return Failed(OperationStatus.StoreError, created.Error?.Code, trace);

        if (request.Id == "atomic-create-and-dependency-inventory")
        {
            OperationResult<BaseActivationDependencyResult> dependencies = await provider.ReadDependenciesAsync(new()
            {
                ApplicationId = "base.activation.certification", MaximumDefinitions = 4096, DeadlineUtc = request.DeadlineUtc,
            }, cancellationToken).ConfigureAwait(false);
            trace.Add("dependencies:" + dependencies.Status);
            bool passed = dependencies.IsSuccess() && dependencies.Value is { } value
                && value.Dependencies.Any(item => item.Definition.Id == definition.Id && item.Definition.Version == definition.Version)
                && value.Accounting.Candidates >= 1 && value.Accounting.IndexOperations >= 1;
            return Result(passed, dependencies.Status, dependencies.Error?.Code, trace);
        }

        OperationResult<BaseActivationDueObservation> observation = await provider.ObserveDueAsync(new()
        {
            ApplicationId = "base.activation.certification", WorkerModuleId = "certification",
            Definitions = [definition], Scope = scope, AcceptedTime = AcceptedTime(now), MaximumCandidates = 8, Limits = limits,
        }, cancellationToken).ConfigureAwait(false);
        trace.Add("observe:" + observation.Status);
        if (!observation.IsSuccess() || observation.Value?.Earliest is null)
            return Failed(observation.Status, observation.Error?.Code, trace);

        if (request.Id == "due-observation-and-token-invalidation")
        {
            BaseDueWaitResult waited = await provider.WaitForDueChangeAsync(
                observation.Value.Token, DateTimeOffset.FromUnixTimeMilliseconds(now), cancellationToken).ConfigureAwait(false);
            trace.Add("wait:" + waited.Outcome);
            return Result(waited.Outcome == BaseDueWaitOutcome.TokenInvalid,
                OperationStatus.Ok, null, trace);
        }

        BaseActivationWorkerAuthority worker = Worker(definition, scope);
        BaseMutationRequestIdentity claimIdentity = Identity("claim-" + suffix);
        OperationResult<BaseActivationClaimResult> claimResult = await provider.TryClaimNextAsync(new()
        {
            Observation = observation.Value.Token, Worker = worker, AcceptedTime = AcceptedTime(now),
            LeaseMilliseconds = 500, Identity = claimIdentity, Limits = limits,
        }, cancellationToken).ConfigureAwait(false);
        trace.Add("claim:" + claimResult.Status);
        if (!claimResult.IsSuccess() || claimResult.Value is not BaseActivationClaimedResult claimed)
            return Failed(claimResult.Status, claimResult.Error?.Code, trace);
        if (request.Id == "atomic-seek-claim")
            return Result(claimed.Claim.FencingToken.Length == 32 && claimed.Lease.LeaseRevision == 1,
                claimResult.Status, claimResult.Error?.Code, trace);

        if (request.Id == "claim-renewal")
        {
            OperationResult<BaseActivationRenewResult> renewed = await provider.RenewAsync(new()
            {
                Claim = claimed.Claim, ExpectedLeaseRevision = claimed.Lease.LeaseRevision,
                AcceptedTime = AcceptedTime(now + 1), ExtensionMilliseconds = 1_000,
                Identity = Identity("renew-" + suffix), Limits = limits,
            }, cancellationToken).ConfigureAwait(false);
            trace.Add("renew:" + renewed.Status);
            return Result(renewed.IsSuccess() && renewed.Value?.Lease.LeaseRevision == 2
                && renewed.Value.Claim.FencingToken.SequenceEqual(claimed.Claim.FencingToken),
                renewed.Status, renewed.Error?.Code, trace);
        }

        if (request.Id == "claim-completion-and-receipt-replay")
        {
            byte[] bytes = "certified"u8.ToArray();
            BaseMutationRequestIdentity completionIdentity = Identity("complete-" + suffix);
            var completion = new BaseActivationCompleteRequest
            {
                ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim,
                CanonicalResult = bytes.ToImmutableArray(), ResultChecksum = SHA256.HashData(bytes).ToImmutableArray(),
                AcceptedTime = AcceptedTime(now + 1), Identity = completionIdentity, Limits = limits,
            };
            OperationResult<BaseActivationTransitionResult> first = await provider.TransitionAsync(completion, cancellationToken).ConfigureAwait(false);
            OperationResult<BaseActivationTransitionResult> replay = await provider.TransitionAsync(
                completion with { AcceptedTime = AcceptedTime(now + 2) }, cancellationToken).ConfigureAwait(false);
            trace.Add("complete:" + first.Status); trace.Add("replay:" + replay.Value?.Disposition);
            return Result(first.IsSuccess() && first.Value?.State == BaseActivationState.Succeeded
                && replay.IsSuccess() && replay.Value?.Disposition == BaseMutationRequestDisposition.Duplicate,
                replay.Status, replay.Error?.Code, trace);
        }

        if (request.Id == "failure-to-retry-transition")
        {
            OperationResult<BaseActivationTransitionResult> failed = await provider.TransitionAsync(new BaseActivationFailRequest
            {
                ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim,
                Disposition = BaseActivationFailureDisposition.Retry, RetryDueAt = now + 2,
                StableFailureCode = "certification.retry", AcceptedTime = AcceptedTime(now + 1),
                Identity = Identity("fail-" + suffix), Limits = limits,
            }, cancellationToken).ConfigureAwait(false);
            trace.Add("fail:" + failed.Value?.State);
            return Result(failed.IsSuccess() && failed.Value?.State == BaseActivationState.RetryPending,
                failed.Status, failed.Error?.Code, trace);
        }

        if (request.Id == "executor-bound-effect-start")
            return await EffectCaseAsync(provider, request, claimed, now, limits, trace, cancellationToken).ConfigureAwait(false);

        // Remaining provider-matrix cases execute their named read/validation seams against real retained authority.
        if (request.Id == "schedule-missing-read-is-nonenumerating")
        {
            OperationResult<BaseScheduleAuthority> missing = await provider.ReadScheduleAsync(
                "certification.missing." + suffix, 1, cancellationToken).ConfigureAwait(false);
            trace.Add("schedule-read:" + missing.Status);
            return Result(missing.Status == OperationStatus.NotFound, missing.Status, missing.Error?.Code, trace);
        }
        if (request.Id == "retained-definition-dependency-after-claim")
        {
            OperationResult<BaseActivationDependencyResult> dependency = await provider.ReadDependenciesAsync(new()
            {
                ApplicationId = "base.activation.certification", MaximumDefinitions = 4096, DeadlineUtc = request.DeadlineUtc,
            }, cancellationToken).ConfigureAwait(false);
            trace.Add("migration-dependency:" + dependency.Status);
            return Result(dependency.IsSuccess() && dependency.Value!.Dependencies.Length >= 1,
                dependency.Status, dependency.Error?.Code, trace);
        }

        OperationResult<BaseActivationRenewResult> budget = await provider.RenewAsync(new()
        {
            Claim = claimed.Claim, ExpectedLeaseRevision = claimed.Lease.LeaseRevision,
            AcceptedTime = AcceptedTime(now + 1), ExtensionMilliseconds = 1_000,
            Identity = Identity("budget-" + suffix), Limits = limits,
        }, cancellationToken).ConfigureAwait(false);
        trace.Add("budget:" + budget.Status);
        return Result(budget.IsSuccess() && budget.Value is { Accounting.IndexOperations: > 0, Accounting.EvidenceBytes: >= 0 },
            budget.Status, budget.Error?.Code, trace);
    }

    private static async ValueTask<(bool, OperationStatus, string?, byte[])> ExecutorCaseAsync(
        IBaseActivationProvider provider, BaseActivationCertificationCaseRequest request, long now,
        BaseActivationExecutionLimits limits, List<string> trace, CancellationToken cancellationToken)
    {
        byte[] workers = SHA256.HashData("certification-workers"u8);
        OperationResult<BaseExecutorRegistrationResult> registered = await provider.RegisterExecutorAsync(new()
        {
            ApplicationId = "base.activation.certification", HostId = "host", ProcessIncarnationId = "process-" + request.Ordinal,
            WorkerDefinitionSetChecksum = workers.ToImmutableArray(), AcceptedTime = AcceptedTime(now),
            RequestedHeartbeatMilliseconds = 1_000, Identity = Identity("executor-register-" + request.Ordinal), Limits = limits,
        }, cancellationToken).ConfigureAwait(false);
        trace.Add("register:" + registered.Status);
        if (!registered.IsSuccess() || registered.Value is null) return Failed(registered.Status, registered.Error?.Code, trace);
        OperationResult<BaseExecutorHeartbeatResult> heartbeat = await provider.HeartbeatExecutorAsync(new()
        {
            Executor = registered.Value.Executor, ExpectedHeartbeatRevision = registered.Value.Heartbeat.HeartbeatRevision,
            AcceptedTime = AcceptedTime(now + 1), ExtensionMilliseconds = 1_000,
            Identity = Identity("executor-heartbeat-" + request.Ordinal), Limits = limits,
        }, cancellationToken).ConfigureAwait(false);
        trace.Add("heartbeat:" + heartbeat.Status);
        if (!heartbeat.IsSuccess() || heartbeat.Value is null) return Failed(heartbeat.Status, heartbeat.Error?.Code, trace);
        OperationResult<BaseExecutorRetirementResult> retired = await provider.RetireExecutorAsync(new()
        {
            Executor = heartbeat.Value.Executor, ExpectedHeartbeatRevision = heartbeat.Value.Heartbeat.HeartbeatRevision,
            AcceptedTime = AcceptedTime(now + 2), Identity = Identity("executor-retire-" + request.Ordinal), Limits = limits,
        }, cancellationToken).ConfigureAwait(false);
        trace.Add("retire:" + retired.Status);
        return Result(retired.IsSuccess(), retired.Status, retired.Error?.Code, trace);
    }

    private static async ValueTask<(bool, OperationStatus, string?, byte[])> EffectCaseAsync(
        IBaseActivationProvider provider, BaseActivationCertificationCaseRequest request, BaseActivationClaimedResult claimed,
        long now, BaseActivationExecutionLimits limits, List<string> trace, CancellationToken cancellationToken)
    {
        byte[] workers = SHA256.HashData("effect-workers"u8);
        OperationResult<BaseExecutorRegistrationResult> registered = await provider.RegisterExecutorAsync(new()
        {
            ApplicationId = "base.activation.certification", HostId = "host", ProcessIncarnationId = "effect-" + request.Ordinal,
            WorkerDefinitionSetChecksum = workers.ToImmutableArray(), AcceptedTime = AcceptedTime(now + 1), RequestedHeartbeatMilliseconds = 1_000,
            Identity = Identity("effect-executor-" + request.Ordinal), Limits = limits,
        }, cancellationToken).ConfigureAwait(false);
        if (!registered.IsSuccess() || registered.Value is null) return Failed(registered.Status, registered.Error?.Code, trace);
        OperationResult<BaseActivationTransitionResult> started = await provider.TransitionAsync(new BaseActivationBeginEffectRequest
        {
            ActivationId = claimed.Claim.ActivationId, Claim = claimed.Claim, Executor = registered.Value.Executor,
            ExecutorHeartbeat = registered.Value.Heartbeat, HeartbeatMilliseconds = 500,
            AcceptedTime = AcceptedTime(now + 2), Identity = Identity("effect-start-" + request.Ordinal), Limits = limits,
        }, cancellationToken).ConfigureAwait(false);
        trace.Add("effect-start:" + started.Status);
        return Result(started.IsSuccess() && started.Value?.State == BaseActivationState.EffectStarted,
            started.Status, started.Error?.Code, trace);
    }

    private static (bool, OperationStatus, string?, byte[]) Result(bool passed, OperationStatus status, string? error, List<string> trace) =>
        (passed, status, error, Encoding.UTF8.GetBytes(string.Join("\n", trace)));
    private static (bool, OperationStatus, string?, byte[]) Failed(OperationStatus status, string? error, List<string> trace) =>
        Result(false, status, error ?? "base.activation.certification.failed", trace);

    private static BaseActivationDefinitionKey Definition(string suffix) => new()
    { Id = "certification.activation." + suffix, Version = 1, Checksum = SHA256.HashData(Encoding.UTF8.GetBytes("definition-" + suffix)).ToImmutableArray() };
    private static BaseOwnedScopeSeekAuthority Scope() => new()
    {
        Kind = BaseSubjectScopeKind.Global,
        ProtectedIndexDigest = SHA256.HashData(Encoding.UTF8.GetBytes($"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
    };
    private static BaseActivationWorkerAuthority Worker(BaseActivationDefinitionKey definition, BaseOwnedScopeSeekAuthority scope) => new()
    {
        ApplicationId = "base.activation.certification", ModuleId = "certification", WorkerIdentity = "worker",
        Definitions = [definition], Scope = scope, Checksum = SHA256.HashData("worker"u8).ToImmutableArray(),
    };
    private static BaseMutationRequestIdentity Identity(string id) => BaseMutationRequestIdentity.Create(
        "base.activation.certification", "activation", id, BaseMutationRequestFingerprint.Create(SHA256.HashData(Encoding.UTF8.GetBytes(id))));
    private static RecordMutationExecutionRequest MutationExecutionRequest() => new()
    { AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5) };
    private static BaseAtomicMutationExecutionLimits MutationLimits() =>
        DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(BaseModuleMutationPlatform.MaximumLimits);
    private static BaseActivationExecutionLimits ActivationLimits() => new()
    {
        MaximumCandidates = 8, MaximumInputBytes = 4096, MaximumResultBytes = 4096,
        MaximumEvidenceBytes = 4096, MaximumTransientBytes = 16384, MaximumReadIntervals = 8, MaximumIndexOperations = 16,
        AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
    };
    private static BaseAcceptedTimeReceipt AcceptedTime(long milliseconds)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "base.activation.acceptedTime.v2\0"); Append(hash, "base.activation.certification"); Append(hash, 1L);
        Append(hash, milliseconds); Append(hash, milliseconds); Append(hash, milliseconds + 1); Append(hash, 30_000L);
        return new BaseAcceptedTimeReceipt("base.activation.certification", 1, milliseconds, milliseconds, milliseconds + 1, 30_000, hash.GetHashAndReset());
    }
    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length)); hash.AppendData(length); hash.AppendData(bytes);
    }
    private static void Append(IncrementalHash hash, long value)
    { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes); }

    private sealed class CreationProcessor(
        BaseAtomicMutationAuthorityRequirement authority, BaseAtomicMutationExecutionLimits limits,
        BaseActivationDefinitionKey definition, BaseOwnedScopeSeekAuthority scope, long dueAt, string id) : IAtomicMutationProcessor
    {
        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(IAtomicRecordSession session, CancellationToken cancellationToken = default)
        {
            byte[] input = Encoding.UTF8.GetBytes(id);
            var extension = new BaseActivationCreationExtension
            {
                StructuralDigest = SHA256.HashData(Encoding.UTF8.GetBytes("structural-" + id)).ToImmutableArray(),
                Items = [new BaseActivationCreateIntent
                {
                    Ordinal = 0, Definition = definition, CanonicalInput = input.ToImmutableArray(),
                    InputChecksum = SHA256.HashData(input).ToImmutableArray(),
                    Scope = new BaseOwnedSubjectScopeEvidence { Kind = scope.Kind }, RequestedDueAt = dueAt, EffectiveDueAt = dueAt,
                    Identity = Identity(id),
                }],
            };
            var request = new BaseAtomicExecutionRequest
            {
                Kind = BaseAtomicMutationExecutionKind.ActivationCreation,
                Intent = new BaseAtomicMutationIntent { IntentDigest = "certification-intent-" + id, Authority = authority, Items = [] },
                Activations = extension, Limits = limits,
            };
            OperationResult<BaseCapturedAtomicExecution> captured = await session.CaptureAtomicExecutionAsync(request, cancellationToken).ConfigureAwait(false);
            if (!captured.IsSuccess() || captured.Value is null) return Failure(captured.Error);
            var plan = new BaseFinalizedAtomicExecutionPlan
            {
                Kind = request.Kind, PlanDigest = "certification-plan-" + id, IntentDigest = request.Intent.IntentDigest,
                CaptureDigest = captured.Value.CaptureDigest, PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]),
                Authority = authority, Items = [], SubjectValidations = [], Activations = extension, Limits = limits,
            };
            OperationResult<BasePreparedAtomicExecution> prepared = await session.PrepareAtomicExecutionAsync(captured.Value, plan, cancellationToken).ConfigureAwait(false);
            if (!prepared.IsSuccess() || prepared.Value is null) return Failure(prepared.Error);
            OperationResult<BaseProvisionalAtomicExecution> applied = await session.ApplyPreparedAtomicExecutionAsync(prepared.Value, cancellationToken).ConfigureAwait(false);
            if (!applied.IsSuccess()) return Failure(applied.Error);
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, []);
        }
        private static AtomicMutationProcessingResult Failure(BaseError? error) => new(
            AtomicMutationProcessingOutcome.Failed, [], error ?? new BaseError
            { Code = "base.activation.certification.failed", Message = "Activation certification failed.", Category = ErrorCategory.Store });
    }
}
