using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class DefaultBaseActivationWorkerRuntime(
    IRecordStoreRegistry stores,
    IBasePolicyOrchestrator policy,
    BaseActivationAcceptedTimeAuthority acceptedTime,
    BaseActivationRegistry definitions,
    BaseModuleMutationRegistry moduleMutations,
    BaseActivationProviderExecutionGate providerGate) : IBaseActivationWorkerRuntime
{
    private readonly SemaphoreSlim _executorGate = new(1, 1);
    private readonly string _hostId = Environment.MachineName.Normalize(NormalizationForm.FormC);
    private readonly string _processIncarnationId = Guid.NewGuid().ToString("N");
    private BaseExecutorRegistrationResult? _executor;

    internal DefaultBaseActivationWorkerRuntime(
        IRecordStoreRegistry stores,
        IBasePolicyOrchestrator policy,
        BaseActivationAcceptedTimeAuthority acceptedTime)
        : this(stores, policy, acceptedTime, new BaseActivationRegistry([]), new BaseModuleMutationRegistry([], []), new BaseActivationProviderExecutionGate()) { }

    internal DefaultBaseActivationWorkerRuntime(
        IRecordStoreRegistry stores,
        IBasePolicyOrchestrator policy,
        BaseActivationAcceptedTimeAuthority acceptedTime,
        BaseActivationRegistry definitions,
        BaseModuleMutationRegistry moduleMutations)
        : this(stores, policy, acceptedTime, definitions, moduleMutations, new BaseActivationProviderExecutionGate()) { }

    public async ValueTask<OperationResult<BaseActivationDispatchResult>> ExecuteTransactionalAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseDueObservationToken observation,
        CancellationToken cancellationToken)
    {
        if (definition.ExecutionClass != BaseActivationExecutionClass.TransactionalOperation
            || definition.TransactionalTarget is null)
            return Failure<BaseActivationDispatchResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        OperationResult<BaseActivationWorkerAuthority> authority = await AuthorizeAsync(
            session, definition, definition.Grants.Execute, BaseOperationKind.ActivationTransition, cancellationToken).ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null)
            return CopyFailure<BaseActivationDispatchResult, BaseActivationWorkerAuthority>(authority);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseActivationDispatchResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        BaseAcceptedTimeReceipt now = acceptedTime.Capture(session.ApplicationId);
        OperationResult<BaseTransactionalActivationCandidate> read = await CallAsync(
            token => provider.ReadTransactionalCandidateAsync(
            new BaseTransactionalActivationCandidateRequest
            {
                ApplicationId = session.ApplicationId,
                Definition = new BaseActivationDefinitionKey
                {
                    Id = definition.Id,
                    Version = definition.Version,
                    Checksum = definition.Checksum.ToArray().ToImmutableArray(),
                },
                Observation = observation,
                Scope = authority.Value.Scope,
                AcceptedTime = now,
                Limits = definition.Limits.Provider,
            }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess() || read.Value is null)
            return CopyFailure<BaseActivationDispatchResult, BaseTransactionalActivationCandidate>(read);
        BaseTransactionalActivationCandidate candidate = read.Value;
        if (!CandidateMatches(candidate, definition, now.CapturedUtc))
            return ProviderContractInvalid<BaseActivationDispatchResult>();
        string targetKind;
        string targetId;
        int targetVersion;
        string targetChecksum;
        switch (definition.TransactionalTarget)
        {
            case BaseModuleMutationActivationTarget module:
                targetKind = "moduleMutation";
                targetId = module.OperationId;
                targetVersion = module.OperationVersion;
                targetChecksum = module.OperationChecksum;
                break;
            case BaseSelectionMutationActivationTarget selection:
                targetKind = "selectionMutation";
                targetId = selection.ProfileId;
                targetVersion = selection.ProfileVersion;
                targetChecksum = selection.ProfileChecksum;
                break;
            default:
                return Failure<BaseActivationDispatchResult>(OperationStatus.Unsupported, "base.activation.handlerVersionUnavailable", ErrorCategory.Capability);
        }
        byte[] fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.transactional.v1\0{candidate.Payload.ActivationId}\0{targetKind}\0{targetId}\0{targetVersion}\0{targetChecksum}\0{Convert.ToHexString(candidate.Payload.InputChecksum.AsSpan())}"));
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            $"activation:{definition.Id}:{definition.Version}",
            "transactional-target",
            candidate.Payload.ActivationId,
            BaseMutationRequestFingerprint.Create(fingerprint));
        if (definition.TransactionalTarget is BaseModuleMutationActivationTarget moduleTarget)
        {
            IBaseModuleMutationRegistration? registration = moduleMutations.FindRegistration(moduleTarget.OperationId, moduleTarget.OperationVersion);
            if (registration is null || !string.Equals(
                    Convert.ToHexStringLower(moduleMutations.Find(moduleTarget.OperationId, moduleTarget.OperationVersion)?.Checksum.ToArray() ?? []),
                    moduleTarget.OperationChecksum, StringComparison.Ordinal))
                return Failure<BaseActivationDispatchResult>(OperationStatus.Unsupported, "base.activation.handlerVersionUnavailable", ErrorCategory.Capability);
            BaseResult<BaseUntypedModuleMutationExecutionResult> executed = await registration.ExecuteTransactionalAsync(
                session, candidate.Payload.CanonicalInput.AsMemory(), identity, candidate, cancellationToken).ConfigureAwait(false);
            if (executed is BaseFailure<BaseUntypedModuleMutationExecutionResult> failed)
                return new OperationResult<BaseActivationDispatchResult>
                { Status = failed.Status, Error = failed.Error, Warnings = failed.Warnings, Diagnostics = failed.Diagnostics };
        }
        else if (definition.TransactionalTarget is BaseSelectionMutationActivationTarget selectionTarget)
        {
            if (session.Services.GetService(typeof(BaseSelectionProfileRegistry)) is not BaseSelectionProfileRegistry profiles
                || session.Services.GetService(typeof(BaseCollectionRegistry)) is not BaseCollectionRegistry collections
                || session.Services.GetService(typeof(IBaseSelectionMutationRuntime)) is not DefaultBaseSelectionMutationRuntime selectionRuntime)
                return Failure<BaseActivationDispatchResult>(OperationStatus.Unsupported, "base.activation.handlerVersionUnavailable", ErrorCategory.Capability);
            BaseSelectionOperationProfile[] matches = profiles.All.Where(value => value.Id == selectionTarget.ProfileId
                && value.Version == selectionTarget.ProfileVersion
                && string.Equals(BaseSelectionProfileChecksum.Compute(value), selectionTarget.ProfileChecksum, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || !collections.Collections.TryGetValue(matches[0].CollectionId, out CollectionDefinition? collection))
                return Failure<BaseActivationDispatchResult>(OperationStatus.Unsupported, "base.activation.handlerVersionUnavailable", ErrorCategory.Capability);
            BaseSelectionActivationRequest? request;
            try
            {
                request = System.Text.Json.JsonSerializer.Deserialize(
                    candidate.Payload.CanonicalInput.AsSpan(), HPDBaseJsonSerializerContext.Default.BaseSelectionActivationRequest);
            }
            catch { request = null; }
            if (request is null)
                return Failure<BaseActivationDispatchResult>(OperationStatus.ValidationFailed, "base.activation.invalid", ErrorCategory.Validation);
            BaseResult<BaseSelectionMutationResult> executed = await selectionRuntime.ExecuteTransactionalAsync(
                session, collection, matches[0], request, identity, candidate, cancellationToken).ConfigureAwait(false);
            if (executed is BaseFailure<BaseSelectionMutationResult> failed)
                return new OperationResult<BaseActivationDispatchResult>
                { Status = failed.Status, Error = failed.Error, Warnings = failed.Warnings, Diagnostics = failed.Diagnostics };
        }
        return OperationResults.Ok(new BaseActivationDispatchResult
        {
            Empty = false,
            ActivationId = candidate.Payload.ActivationId,
            State = BaseActivationState.Succeeded,
        });
    }

    public async ValueTask<OperationResult> AuthorizeExecutionAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        CancellationToken cancellationToken)
    {
        bool authorized = await IsAuthorizedAsync(session, definition, definition.Grants.Execute,
            BaseOperationKind.ActivationTransition, cancellationToken).ConfigureAwait(false);
        return authorized
            ? new OperationResult { Status = OperationStatus.Ok }
            : new OperationResult
            {
                Status = OperationStatus.PolicyDenied,
                Error = new BaseError
                {
                    Code = "base.activation.unauthorized",
                    Message = "The activation operation is not authorized.",
                    Category = ErrorCategory.Authorization,
                },
            };
    }

    public async ValueTask<OperationResult<BaseActivationTransitionResult>> BeginEffectAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        if (definition.ExecutionClass != BaseActivationExecutionClass.AtMostOnceEffect)
            return Failure<BaseActivationTransitionResult>(OperationStatus.ValidationFailed, "base.activation.invalid", ErrorCategory.Validation);
        if (!await IsAuthorizedAsync(session, definition, definition.Grants.Execute,
            BaseOperationKind.ActivationTransition, cancellationToken).ConfigureAwait(false))
            return Failure<BaseActivationTransitionResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseActivationTransitionResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        OperationResult<BaseExecutorRegistrationResult> executor = await EnsureExecutorAsync(
            provider, session.ApplicationId, definition, cancellationToken).ConfigureAwait(false);
        if (!executor.IsSuccess() || executor.Value is null)
            return CopyFailure<BaseActivationTransitionResult, BaseExecutorRegistrationResult>(executor);
        long heartbeat = checked((long)Math.Max(
            definition.Limits.LeaseDuration.TotalMilliseconds,
            definition.Limits.HandlerTimeout.TotalMilliseconds + 10_000d));
        return await CallAsync(token => provider.TransitionAsync(new BaseActivationBeginEffectRequest
        {
            ActivationId = claim.ActivationId,
            Claim = claim,
            Executor = executor.Value.Executor,
            ExecutorHeartbeat = executor.Value.Heartbeat,
            HeartbeatMilliseconds = heartbeat,
            Identity = identity,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            Limits = definition.Limits.Provider,
        }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OperationResult<BaseActivationTransitionResult>> CompleteEffectAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseEffectExecutionAuthority effect,
        ImmutableArray<byte> result,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(session, definition, definition.Grants.Complete,
            BaseOperationKind.ActivationTransition, cancellationToken).ConfigureAwait(false))
            return Failure<BaseActivationTransitionResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        IBaseActivationProvider? provider = ResolveProvider();
        return provider is null
            ? Failure<BaseActivationTransitionResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported)
            : await CallAsync(token => provider.TransitionAsync(new BaseActivationCompleteEffectRequest
            {
                ActivationId = effect.Claim.ActivationId,
                Effect = effect,
                CanonicalResult = result,
                ResultChecksum = SHA256.HashData(result.AsSpan()).ToImmutableArray(),
                Identity = identity,
                AcceptedTime = acceptedTime.Capture(session.ApplicationId),
                Limits = definition.Limits.Provider,
            }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OperationResult<BaseExecutorRegistrationResult>> EnsureExecutorAsync(
        IBaseActivationProvider provider,
        string applicationId,
        BaseActivationDefinition definition,
        CancellationToken cancellationToken)
    {
        await _executorGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long now = acceptedTime.Capture(applicationId).CapturedUtc;
            if (_executor is not null && _executor.Heartbeat.HeartbeatExpiresAt > now + 5_000)
                return OperationResults.Ok(_executor);
            if (_executor is not null)
            {
                BaseMutationRequestIdentity heartbeatIdentity = ExecutorIdentity(
                    "executor-heartbeat", _executor.Executor.Checksum.AsSpan(), _executor.Heartbeat.HeartbeatRevision + 1);
                OperationResult<BaseExecutorHeartbeatResult> heartbeat = await CallAsync(token => provider.HeartbeatExecutorAsync(new BaseExecutorHeartbeatRequest
                {
                    Executor = _executor.Executor,
                    ExpectedHeartbeatRevision = _executor.Heartbeat.HeartbeatRevision,
                    ExtensionMilliseconds = checked((long)Math.Max(definition.Limits.HandlerTimeout.TotalMilliseconds + 30_000d, 60_000d)),
                    AcceptedTime = acceptedTime.Capture(applicationId),
                    Identity = heartbeatIdentity,
                    Limits = definition.Limits.Provider,
                }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
                if (heartbeat.IsSuccess() && heartbeat.Value is not null)
                {
                    _executor = new BaseExecutorRegistrationResult
                    {
                        Executor = heartbeat.Value.Executor,
                        Heartbeat = heartbeat.Value.Heartbeat,
                        Accounting = heartbeat.Value.Accounting,
                        Disposition = heartbeat.Value.Disposition,
                    };
                    return OperationResults.Ok(_executor);
                }
                return CopyFailure<BaseExecutorRegistrationResult, BaseExecutorHeartbeatResult>(heartbeat);
            }
            ImmutableArray<byte> workerSetChecksum = WorkerSetChecksum();
            OperationResult<BaseExecutorRegistrationResult> registered = await CallAsync(token => provider.RegisterExecutorAsync(new BaseExecutorRegistrationRequest
            {
                ApplicationId = applicationId,
                HostId = _hostId,
                ProcessIncarnationId = _processIncarnationId,
                WorkerDefinitionSetChecksum = workerSetChecksum,
                RequestedHeartbeatMilliseconds = checked((long)Math.Max(definition.Limits.HandlerTimeout.TotalMilliseconds + 30_000d, 60_000d)),
                AcceptedTime = acceptedTime.Capture(applicationId),
                Identity = ExecutorIdentity("executor-register", workerSetChecksum.AsSpan(), 1),
                Limits = definition.Limits.Provider,
            }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
            if (registered.IsSuccess() && registered.Value is not null) _executor = registered.Value;
            return registered;
        }
        finally { _executorGate.Release(); }
    }

    private ImmutableArray<byte> WorkerSetChecksum()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes("base.activation.workerSet.v1\0"));
        Span<byte> version = stackalloc byte[4];
        foreach (BaseActivationDefinition definition in definitions.Definitions)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(definition.Id));
            BinaryPrimitives.WriteInt32BigEndian(version, definition.Version);
            hash.AppendData(version);
            hash.AppendData(definition.Checksum.AsSpan());
        }
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static BaseMutationRequestIdentity ExecutorIdentity(string operation, ReadOnlySpan<byte> authority, long revision)
    {
        Span<byte> revisionBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(revisionBytes, revision);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(authority);
        hash.AppendData(revisionBytes);
        return BaseMutationRequestIdentity.Create("base.activation.executor", operation,
            $"{Convert.ToHexStringLower(SHA256.HashData(authority))}:{revision}",
            BaseMutationRequestFingerprint.Create(hash.GetHashAndReset()));
    }

    public async ValueTask<OperationResult<BaseActivationDueObservation>> ObserveAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        CancellationToken cancellationToken)
    {
        OperationResult<BaseActivationWorkerAuthority> authority = await AuthorizeAsync(
            session, definition, definition.Grants.Observe, BaseOperationKind.ActivationClaim, cancellationToken).ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null)
            return CopyFailure<BaseActivationDueObservation, BaseActivationWorkerAuthority>(authority);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseActivationDueObservation>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        OperationResult<BaseActivationDueObservation> result = await CallAsync(token => provider.ObserveDueAsync(new BaseActivationDueObservationRequest
        {
            ApplicationId = session.ApplicationId,
            WorkerModuleId = definition.OwningModuleId,
            Definitions = authority.Value.Definitions,
            Scope = authority.Value.Scope,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            MaximumCandidates = Math.Min(1, definition.Limits.Provider.MaximumCandidates),
            Limits = definition.Limits.Provider,
        }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
        return ValidateObservation(result, definition.Limits.Provider);
    }

    public async ValueTask<OperationResult<BaseActivationClaimResult>> ClaimAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseDueObservationToken observation,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(identity);
        OperationResult<BaseActivationWorkerAuthority> authority = await AuthorizeAsync(
            session, definition, definition.Grants.Claim, BaseOperationKind.ActivationClaim, cancellationToken).ConfigureAwait(false);
        if (!authority.IsSuccess() || authority.Value is null)
            return CopyFailure<BaseActivationClaimResult, BaseActivationWorkerAuthority>(authority);
        if (!await IsAuthorizedAsync(session, definition, definition.Grants.Execute,
            BaseOperationKind.ActivationClaim, cancellationToken).ConfigureAwait(false))
            return Failure<BaseActivationClaimResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseActivationClaimResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        OperationResult<BaseActivationClaimResult> result = await CallAsync(token => provider.TryClaimNextAsync(new BaseActivationClaimRequest
        {
            Observation = observation,
            Worker = authority.Value,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            LeaseMilliseconds = checked((long)definition.Limits.LeaseDuration.TotalMilliseconds),
            Identity = identity,
            Limits = definition.Limits.Provider,
        }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
        return ValidateClaim(result, definition, authority.Value);
    }

    public async ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        BaseActivationLeaseObservation lease,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(session, definition, definition.Grants.Renew,
            BaseOperationKind.ActivationTransition, cancellationToken).ConfigureAwait(false))
            return Failure<BaseActivationRenewResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseActivationRenewResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        OperationResult<BaseActivationRenewResult> result = await CallAsync(token => provider.RenewAsync(new BaseActivationRenewRequest
        {
            Claim = claim,
            ExpectedLeaseRevision = lease.LeaseRevision,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            ExtensionMilliseconds = checked((long)definition.Limits.LeaseDuration.TotalMilliseconds),
            Identity = identity,
            Limits = definition.Limits.Provider,
        }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null) return result;
        return ClaimsEqual(result.Value.Claim, claim) && result.Value.Lease.LeaseRevision == checked(lease.LeaseRevision + 1)
            ? result
            : ProviderContractInvalid<BaseActivationRenewResult>();
    }

    public ValueTask<OperationResult<BaseActivationTransitionResult>> CompleteAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        ImmutableArray<byte> result,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken) =>
        TransitionAsync(session, definition, new BaseActivationCompleteRequest
        {
            ActivationId = claim.ActivationId,
            Claim = claim,
            CanonicalResult = result,
            ResultChecksum = SHA256.HashData(result.AsSpan()).ToImmutableArray(),
            Identity = identity,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            Limits = definition.Limits.Provider,
        }, definition.Grants.Complete, cancellationToken);

    public ValueTask<OperationResult<BaseActivationTransitionResult>> FailAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        string failureCode,
        bool retry,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken) =>
        FailCoreAsync(session, definition, claim, failureCode, retry, identity, cancellationToken);

    public ValueTask<OperationResult<BaseActivationTransitionResult>> CancelAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        string activationId,
        long expectedGeneration,
        BaseCancellationPropagation propagation,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken) =>
        TransitionAsync(session, definition, new BaseActivationCancelRequest
        {
            ActivationId = activationId,
            ExpectedGeneration = expectedGeneration,
            Propagation = propagation,
            Identity = identity,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            Limits = definition.Limits.Provider,
        }, definition.Grants.Cancel, cancellationToken);

    public ValueTask<OperationResult<BaseActivationTransitionResult>> HeartbeatEffectAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseEffectExecutionAuthority effect,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken) =>
        TransitionAsync(session, definition, new BaseActivationEffectHeartbeatRequest
        {
            ActivationId = effect.Claim.ActivationId,
            Effect = effect,
            ExpectedHeartbeatRevision = effect.HeartbeatRevision,
            ExtensionMilliseconds = checked((long)Math.Max(definition.Limits.HandlerTimeout.TotalMilliseconds + 30_000d, 60_000d)),
            Identity = identity,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            Limits = definition.Limits.Provider,
        }, definition.Grants.Renew, cancellationToken);

    public async ValueTask<OperationResult<BaseExecutorRegistrationResult>> RegisterExecutorAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        string hostId,
        string processIncarnationId,
        long heartbeatMilliseconds,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(session, definition, definition.Grants.Execute,
            BaseOperationKind.ActivationTransition, cancellationToken).ConfigureAwait(false))
            return Failure<BaseExecutorRegistrationResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        if (string.IsNullOrWhiteSpace(hostId) || string.IsNullOrWhiteSpace(processIncarnationId)
            || heartbeatMilliseconds is <= 0 or > 86_400_000)
            return Failure<BaseExecutorRegistrationResult>(OperationStatus.ValidationFailed, "base.activation.invalid", ErrorCategory.Validation);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseExecutorRegistrationResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        return await CallAsync(token => provider.RegisterExecutorAsync(new BaseExecutorRegistrationRequest
        {
            ApplicationId = session.ApplicationId,
            HostId = hostId.Normalize(NormalizationForm.FormC),
            ProcessIncarnationId = processIncarnationId.Normalize(NormalizationForm.FormC),
            WorkerDefinitionSetChecksum = WorkerSetChecksum(),
            RequestedHeartbeatMilliseconds = heartbeatMilliseconds,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            Identity = identity,
            Limits = definition.Limits.Provider,
        }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OperationResult<BaseExecutorHeartbeatResult>> HeartbeatExecutorAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseExecutorIncarnationAuthority executor,
        BaseExecutorHeartbeatObservation heartbeat,
        long extensionMilliseconds,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(session, definition, definition.Grants.Renew,
            BaseOperationKind.ActivationTransition, cancellationToken).ConfigureAwait(false))
            return Failure<BaseExecutorHeartbeatResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        if (extensionMilliseconds is <= 0 or > 86_400_000)
            return Failure<BaseExecutorHeartbeatResult>(OperationStatus.ValidationFailed, "base.activation.invalid", ErrorCategory.Validation);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseExecutorHeartbeatResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        return await CallAsync(token => provider.HeartbeatExecutorAsync(new BaseExecutorHeartbeatRequest
        {
            Executor = executor,
            ExpectedHeartbeatRevision = heartbeat.HeartbeatRevision,
            ExtensionMilliseconds = extensionMilliseconds,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            Identity = identity,
            Limits = definition.Limits.Provider,
        }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OperationResult<BaseExecutorRetirementResult>> RetireExecutorAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseExecutorIncarnationAuthority executor,
        BaseExecutorHeartbeatObservation heartbeat,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(session, definition, definition.Grants.Execute,
            BaseOperationKind.ActivationTransition, cancellationToken).ConfigureAwait(false))
            return Failure<BaseExecutorRetirementResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseExecutorRetirementResult>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        return await CallAsync(token => provider.RetireExecutorAsync(new BaseExecutorRetirementRequest
        {
            Executor = executor,
            ExpectedHeartbeatRevision = heartbeat.HeartbeatRevision,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            Identity = identity,
            Limits = definition.Limits.Provider,
        }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OperationResult<BaseActivationReceiptResolution>> ResolveReceiptAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!await IsReplayAuthorizedAsync(session, definition, resultBindings, cancellationToken).ConfigureAwait(false))
            return Failure<BaseActivationReceiptResolution>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseActivationReceiptResolution>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        OperationResult<BaseActivationReceiptResolution> resolved = await CallAsync(
            token => provider.ResolveReceiptAsync(new BaseActivationReceiptResolutionRequest
            {
                Identity = identity,
                AcceptedTime = acceptedTime.Capture(session.ApplicationId),
                Limits = definition.Limits.Provider,
            }, token), definition.Limits.Provider, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess() || resolved.Value is null)
            return resolved;
        BaseActivationReceiptResolution value = resolved.Value;
        bool valid = value.OperationKind.Length is > 0 and <= 128
            && value.Fingerprint.Length == BaseMutationRequestFingerprint.Length
            && CryptographicOperations.FixedTimeEquals(value.Fingerprint.AsSpan(), identity.Fingerprint.ToArray())
            && value.CanonicalResult.Length <= definition.Limits.Provider.MaximumResultBytes
            && value.Accounting.Candidates == 1
            && value.Accounting.Comparisons >= 1
            && value.Accounting.EvidenceBytes == value.CanonicalResult.Length
            && value.Accounting.TransientBytes >= value.Accounting.EvidenceBytes
            && value.Accounting.TransientBytes <= definition.Limits.Provider.MaximumTransientBytes;
        return valid
            ? resolved
            : ProviderContractInvalid<BaseActivationReceiptResolution>();
    }

    public ValueTask<OperationResult<BaseActivationTransitionResult>> YieldAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        BaseActivationYield yield,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(yield);
        ArgumentNullException.ThrowIfNull(yield.ProgressFingerprint);
        if (definition.ExecutionClass != BaseActivationExecutionClass.AtLeastOnceWorker
            || definition.Limits.MaximumYields <= 0
            || claim.MaximumYields != definition.Limits.MaximumYields
            || claim.YieldCount < 0 || claim.YieldCount > claim.MaximumYields
            || claim.ExecutionSliceOrdinal <= 0)
            return ValueTask.FromResult(Failure<BaseActivationTransitionResult>(
                OperationStatus.Unsupported, "base.activation.yieldUnsupported", ErrorCategory.Unsupported));

        DateTimeOffset? requested = yield.ResumeAt;
        if (requested is { } present && (present.Offset != TimeSpan.Zero
                || present.Ticks % TimeSpan.TicksPerMillisecond != 0))
            return ValueTask.FromResult(Failure<BaseActivationTransitionResult>(
                OperationStatus.ValidationFailed, "base.activation.yieldInvalid", ErrorCategory.Validation));

        BaseAcceptedTimeReceipt now = acceptedTime.Capture(session.ApplicationId);
        long? requestedMilliseconds;
        try { requestedMilliseconds = requested?.ToUnixTimeMilliseconds(); }
        catch
        {
            return ValueTask.FromResult(Failure<BaseActivationTransitionResult>(
                OperationStatus.ValidationFailed, "base.activation.yieldInvalid", ErrorCategory.Validation));
        }
        long effectiveDueAt = requestedMilliseconds.HasValue
            ? Math.Max(requestedMilliseconds.Value, now.CapturedUtc)
            : now.CapturedUtc;
        ImmutableArray<byte> progress = yield.ProgressFingerprint.ToImmutableArray();
        byte[] fingerprint = YieldFingerprint(session.ApplicationId, definition, claim, requestedMilliseconds, progress.AsSpan());
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            $"activation:{claim.ActivationId}",
            "yield",
            $"{claim.ClaimEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{claim.ExecutionSliceOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            BaseMutationRequestFingerprint.Create(fingerprint));
        return TransitionAsync(session, definition, new BaseActivationYieldRequest
        {
            ActivationId = claim.ActivationId,
            Claim = claim,
            RequestedResumeAt = requested,
            EffectiveDueAt = effectiveDueAt,
            ProgressFingerprint = progress,
            ExpectedYieldCount = claim.YieldCount,
            MaximumYields = claim.MaximumYields,
            Identity = identity,
            AcceptedTime = now,
            Limits = definition.Limits.Provider,
        }, definition.Grants.Yield, cancellationToken);
    }

    private async ValueTask<bool> IsReplayAuthorizedAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings,
        CancellationToken cancellationToken)
    {
        if (!BaseSystemCollectionGate.Allows(session.Principal)
            || resultBindings.Any(static binding => binding.RecordDisclosure != BaseRecordDisclosure.Include))
            return false;
        OperationContext operation = session.Operation(BaseOperationKind.ActivationTransition, definition.Id);
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = session.Principal,
            Operation = operation,
            Collection = PolicyResource(definition),
            ResourceKind = PolicyResourceKind.ActivationDefinition,
        }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactActivationGrant(
                authorization, definition.Grants.Replay, definition.OwningModuleId, session.Principal, operation)
            || authorization.Value is null)
            return false;
        string[] fields = resultBindings.Select(static binding => binding.StablePropertyId).ToArray();
        return authorization.Value.EffectiveReadMask?.Mode switch
        {
            null or FieldMaskMode.Unspecified or FieldMaskMode.AllowAll => true,
            FieldMaskMode.DenyAll => fields.Length == 0,
            FieldMaskMode.IncludeOnly => fields.All(value =>
                (authorization.Value.EffectiveReadMask.Include ?? []).Contains(value, StringComparer.Ordinal)),
            FieldMaskMode.Exclude => fields.All(value =>
                !(authorization.Value.EffectiveReadMask.Exclude ?? []).Contains(value, StringComparer.Ordinal)),
            _ => false,
        };
    }

    private ValueTask<OperationResult<BaseActivationTransitionResult>> FailCoreAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        string failureCode,
        bool retry,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        BaseAcceptedTimeReceipt now = acceptedTime.Capture(session.ApplicationId);
        bool retryAllowed = retry && claim.AttemptNumber < definition.Retry.MaximumAttempts &&
            definition.Retry.RetryableFailureCodes.Contains(failureCode, StringComparer.Ordinal);
        long? retryDueAt = retryAllowed
            ? checked(now.CapturedUtc + RetryDelay(definition.Retry, claim.ActivationId, claim.AttemptNumber))
            : null;
        return TransitionAsync(session, definition, new BaseActivationFailRequest
        {
            ActivationId = claim.ActivationId,
            Claim = claim,
            StableFailureCode = failureCode,
            Disposition = retryAllowed ? BaseActivationFailureDisposition.Retry : BaseActivationFailureDisposition.Exhaust,
            RetryDueAt = retryDueAt,
            Identity = identity,
            AcceptedTime = now,
            Limits = definition.Limits.Provider,
        }, definition.Grants.Fail, cancellationToken);
    }

    private static byte[] YieldFingerprint(
        string applicationId,
        BaseActivationDefinition definition,
        BaseActivationClaimAuthority claim,
        long? requestedResumeAt,
        ReadOnlySpan<byte> progress)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendYield(hash, "base.activation.yield.v1");
        AppendYield(hash, applicationId);
        AppendYield(hash, definition.Id);
        Span<byte> i32 = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(i32, definition.Version); hash.AppendData(i32);
        AppendYield(hash, definition.Checksum.AsSpan());
        AppendYield(hash, claim.ActivationId);
        AppendYield(hash, claim.AttemptNumber);
        AppendYield(hash, claim.ClaimEpoch);
        AppendYield(hash, claim.ExecutionSliceOrdinal);
        AppendYield(hash, claim.FencingToken.AsSpan());
        AppendYield(hash, claim.YieldCount);
        AppendYield(hash, claim.MaximumYields);
        hash.AppendData(requestedResumeAt.HasValue ? [1] : [0]);
        if (requestedResumeAt.HasValue) AppendYield(hash, requestedResumeAt.Value);
        AppendYield(hash, progress);
        return hash.GetHashAndReset();
    }

    private static void AppendYield(IncrementalHash hash, string value) =>
        AppendYield(hash, Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC)));

    private static void AppendYield(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)value.Length));
        hash.AppendData(length); hash.AppendData(value);
    }

    private static void AppendYield(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes);
    }

    private async ValueTask<OperationResult<BaseActivationTransitionResult>> TransitionAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationTransitionRequest request,
        string requiredGrant,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(session, definition, requiredGrant,
            BaseOperationKind.ActivationTransition, cancellationToken).ConfigureAwait(false))
            return Failure<BaseActivationTransitionResult>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        IBaseActivationProvider? provider = ResolveProvider();
        if (provider is null)
            return Failure<BaseActivationTransitionResult>(OperationStatus.Unsupported,
                "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
        OperationResult<BaseActivationTransitionResult> result = await CallAsync(
            token => provider.TransitionAsync(request, token), definition.Limits.Provider,
            cancellationToken).ConfigureAwait(false);
        if (request is BaseActivationYieldRequest yielded && !YieldTransitionResultValid(result, yielded, definition))
            return ProviderContractInvalid<BaseActivationTransitionResult>();
        return result;
    }

    private static bool YieldTransitionResultValid(
        OperationResult<BaseActivationTransitionResult> result,
        BaseActivationYieldRequest request,
        BaseActivationDefinition definition)
    {
        if (!result.IsSuccess() || result.Value is null) return true;
        BaseActivationTransitionResult value = result.Value;
        bool exhausted = request.ExpectedYieldCount == request.MaximumYields;
        BaseActivationState expectedState = exhausted ? BaseActivationState.Exhausted : BaseActivationState.YieldPending;
        long expectedCount = exhausted ? request.ExpectedYieldCount : checked(request.ExpectedYieldCount + 1);
        BaseActivationYieldDisposition expectedDisposition = exhausted
            ? BaseActivationYieldDisposition.LimitExceeded : BaseActivationYieldDisposition.Yielded;
        string? expectedFailure = exhausted ? "base.activation.yieldLimitExceeded" : null;
        return request.MaximumYields == definition.Limits.MaximumYields
            && value.State == expectedState
            && value.Generation == checked(request.Claim.ActivationGeneration + 1)
            && value.YieldCount == expectedCount
            && value.ExecutionSliceOrdinal == request.Claim.ExecutionSliceOrdinal
            && value.EffectiveDueAt == request.EffectiveDueAt
            && value.YieldDisposition == expectedDisposition
            && value.YieldTerminalFailureCode == expectedFailure
            && value.Disposition is BaseMutationRequestDisposition.Committed or BaseMutationRequestDisposition.Duplicate
            && value.CanonicalResult.IsDefaultOrEmpty
            && value.Effect is null
            && value.Accounting.Candidates == 1
            && value.Accounting.Comparisons >= 1
            && value.Accounting.IndexOperations >= 1
            && value.Accounting.ReadIntervals >= 0
            && value.Accounting.EvidenceBytes >= 0
            && value.Accounting.TransientBytes >= value.Accounting.EvidenceBytes
            && value.Accounting.EvidenceBytes <= definition.Limits.Provider.MaximumEvidenceBytes
            && value.Accounting.TransientBytes <= definition.Limits.Provider.MaximumTransientBytes
            && BaseActivationControlChecksumContract.Matches(value.ControlChecksum.AsSpan(),
                request.ActivationId, value.Generation, expectedState, request.EffectiveDueAt,
                expectedCount, request.MaximumYields, request.Claim.ExecutionSliceOrdinal,
                request.Claim.AttemptStartedAt, request.Claim.SliceStartedAt,
                exhausted ? BaseActivationYieldDisposition.LimitExceeded : null, expectedFailure);
    }

    private async ValueTask<OperationResult<T>> CallAsync<T>(
        Func<CancellationToken, ValueTask<OperationResult<T>>> call,
        BaseActivationExecutionLimits limits,
        CancellationToken cancellationToken)
    {
        BaseActivationProviderCallResult<OperationResult<T>> result = await providerGate.ExecuteAsync(
            call, limits.AcquisitionTimeout, limits.TransactionTimeout, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            BaseActivationProviderCallOutcome.Completed when result.Value is not null => result.Value,
            BaseActivationProviderCallOutcome.Cancelled => Failure<T>(OperationStatus.StoreError, "base.activation.cancelled", ErrorCategory.Store),
            BaseActivationProviderCallOutcome.TimedOut => Failure<T>(OperationStatus.StoreError, "base.activation.timeout", ErrorCategory.Store),
            BaseActivationProviderCallOutcome.Capacity => Failure<T>(OperationStatus.StoreError, "base.activation.quarantined", ErrorCategory.Store),
            _ => Failure<T>(OperationStatus.StoreError, "base.activation.storeError", ErrorCategory.Store),
        };
    }

    private async ValueTask<OperationResult<BaseActivationWorkerAuthority>> AuthorizeAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        string requiredGrant,
        BaseOperationKind operationKind,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(session, definition, requiredGrant, operationKind, cancellationToken).ConfigureAwait(false))
            return Failure<BaseActivationWorkerAuthority>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        BaseOwnedScopeSeekAuthority scope = Scope(session.ActivationScope);
        BaseActivationDefinitionKey key = new()
        { Id = definition.Id, Version = definition.Version, Checksum = definition.Checksum.ToArray().ToImmutableArray() };
        string worker = session.Principal.SubjectId ?? string.Empty;
        byte[] checksum = Hash($"base.activation.worker.v2\0{session.ApplicationId}\n{definition.OwningModuleId}\n{worker}\n{definition.Id}\n{definition.Version}\n{Convert.ToHexString(definition.Checksum.AsSpan())}\n{Convert.ToHexString(scope.ProtectedIndexDigest.AsSpan())}");
        return OperationResults.Ok(new BaseActivationWorkerAuthority
        {
            ApplicationId = session.ApplicationId,
            ModuleId = definition.OwningModuleId,
            WorkerIdentity = worker,
            Definitions = [key],
            Scope = scope,
            Checksum = checksum.ToImmutableArray(),
        });
    }

    private async ValueTask<bool> IsAuthorizedAsync(
        BaseSession session,
        BaseActivationDefinition definition,
        string requiredGrant,
        BaseOperationKind operationKind,
        CancellationToken cancellationToken)
    {
        if (!BaseSystemCollectionGate.Allows(session.Principal))
            return false;
        OperationContext operation = session.Operation(operationKind, definition.Id);
        OperationResult<BasePolicyEvaluation> authorization = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = session.Principal,
            Operation = operation,
            Collection = PolicyResource(definition),
            ResourceKind = PolicyResourceKind.ActivationDefinition,
        }, cancellationToken).ConfigureAwait(false);
        return BaseSystemCollectionGate.HasExactActivationGrant(
            authorization, requiredGrant, definition.OwningModuleId, session.Principal, operation);
    }

    private IBaseActivationProvider? ResolveProvider()
    {
        IBaseActivationProvider[] values = stores.GetRegistrations().Select(static item => item.Store)
            .OfType<IBaseActivationProvider>()
            .Where(static item => BaseActivationCertificationReceiptContract.Validate(item.Descriptor))
            .Distinct().ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private OperationResult<BaseActivationDueObservation> ValidateObservation(
        OperationResult<BaseActivationDueObservation> result,
        BaseActivationExecutionLimits limits)
    {
        if (!result.IsSuccess() || result.Value is null) return result;
        BaseActivationDueObservation value = result.Value;
        bool valid = value.Token.Value is { Length: > 0 } && value.Intervals.Length is > 0 &&
            value.Intervals.Length <= limits.MaximumReadIntervals && value.Accounting.Candidates <= limits.MaximumCandidates &&
            value.Accounting.EvidenceBytes <= limits.MaximumEvidenceBytes && value.Accounting.TransientBytes <= limits.MaximumTransientBytes;
        return valid ? result : ProviderContractInvalid<BaseActivationDueObservation>();
    }

    private OperationResult<BaseActivationClaimResult> ValidateClaim(
        OperationResult<BaseActivationClaimResult> result,
        BaseActivationDefinition definition,
        BaseActivationWorkerAuthority worker)
    {
        if (!result.IsSuccess() || result.Value is null) return result;
        if (result.Value is not BaseActivationClaimedResult claimed) return result;
        bool valid = claimed.Payload.Definition.Id == definition.Id && claimed.Payload.Definition.Version == definition.Version &&
            CryptographicOperations.FixedTimeEquals(claimed.Payload.Definition.Checksum.AsSpan(), definition.Checksum.AsSpan()) &&
            claimed.Payload.CanonicalInput.Length <= definition.Limits.MaximumInputBytes &&
            claimed.Claim.ActivationId == claimed.Payload.ActivationId && claimed.Claim.AttemptNumber == claimed.Attempt.AttemptNumber &&
            claimed.Claim.ActivationGeneration > 0 &&
            claimed.Claim.ExecutionSliceOrdinal > 0 &&
            claimed.Claim.AttemptStartedAt <= claimed.Claim.SliceStartedAt &&
            claimed.Claim.SliceStartedAt == claimed.Attempt.StartedAt &&
            claimed.Claim.YieldCount >= 0 && claimed.Claim.YieldCount <= claimed.Claim.MaximumYields &&
            claimed.Claim.MaximumYields == definition.Limits.MaximumYields &&
            claimed.Claim.FencingToken.Length == 32 && claimed.Lease.LeaseRevision > 0 && claimed.Lease.LeaseExpiresAt > claimed.Attempt.StartedAt &&
            claimed.Intervals.Length > 0 && claimed.Accounting.EvidenceBytes <= definition.Limits.Provider.MaximumEvidenceBytes &&
            worker.Definitions.Length == 1;
        return valid ? result : ProviderContractInvalid<BaseActivationClaimResult>();
    }

    private static bool CandidateMatches(
        BaseTransactionalActivationCandidate candidate,
        BaseActivationDefinition definition,
        long acceptedAt) =>
        candidate.ActivationGeneration > 0 && candidate.AcceptedAt == acceptedAt
        && candidate.Payload.Definition.Id == definition.Id
        && candidate.Payload.Definition.Version == definition.Version
        && CryptographicOperations.FixedTimeEquals(
            candidate.Payload.Definition.Checksum.AsSpan(), definition.Checksum.AsSpan())
        && candidate.Payload.InputChecksum.Length == SHA256.HashSizeInBytes
        && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(candidate.Payload.CanonicalInput.AsSpan()), candidate.Payload.InputChecksum.AsSpan())
        && candidate.ControlChecksum.Length == SHA256.HashSizeInBytes
        && candidate.ReadIntervals.Length is > 0
        && candidate.ReadIntervals.Length <= definition.Limits.Provider.MaximumReadIntervals
        && candidate.Accounting.Candidates == 1
        && candidate.Accounting.EvidenceBytes <= definition.Limits.Provider.MaximumEvidenceBytes
        && candidate.Accounting.TransientBytes <= definition.Limits.Provider.MaximumTransientBytes;

    private static BaseOwnedScopeSeekAuthority Scope(BaseOwnedSubjectScopeEvidence scope) => new()
    {
        Kind = scope.Kind,
        ProtectedIndexDigest = Hash($"base.activation.scope.v2\0{(int)scope.Kind}\n{scope.Value ?? string.Empty}").ToImmutableArray(),
    };

    private static CollectionDefinition PolicyResource(BaseActivationDefinition definition) => new()
    {
        Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
        SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
        System = true, SystemOwnerModuleId = definition.OwningModuleId,
    };

    private static bool ClaimsEqual(BaseActivationClaimAuthority left, BaseActivationClaimAuthority right) =>
        left.ActivationId == right.ActivationId &&
        left.AttemptNumber == right.AttemptNumber && left.ActivationGeneration == right.ActivationGeneration &&
        left.ExecutionSliceOrdinal == right.ExecutionSliceOrdinal &&
        left.AttemptStartedAt == right.AttemptStartedAt && left.SliceStartedAt == right.SliceStartedAt &&
        left.YieldCount == right.YieldCount && left.MaximumYields == right.MaximumYields &&
        left.ClaimEpoch == right.ClaimEpoch && left.WorkerIdentity == right.WorkerIdentity &&
        left.CancellationGeneration == right.CancellationGeneration && left.StoreInstanceId == right.StoreInstanceId &&
        left.RestoreEpoch == right.RestoreEpoch &&
        CryptographicOperations.FixedTimeEquals(left.DefinitionChecksum.AsSpan(), right.DefinitionChecksum.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.FencingToken.AsSpan(), right.FencingToken.AsSpan());

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static long RetryDelay(BaseActivationRetryProfile profile, string activationId, int attemptNumber)
    {
        long delay = profile.InitialDelayMilliseconds;
        for (int index = 1; index < attemptNumber; index++)
        {
            long numerator = checked(delay * profile.MultiplierNumerator);
            delay = checked((numerator + profile.MultiplierDenominator - 1) / profile.MultiplierDenominator);
            delay = Math.Min(delay, profile.MaximumDelayMilliseconds);
        }
        long maximumJitter = checked((delay * profile.JitterBasisPoints + 9_999) / 10_000);
        if (maximumJitter == 0) return delay;
        byte[] digest = Hash($"base.activation.retry.jitter.v2\0{activationId}\n{attemptNumber}");
        ulong sample = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(digest);
        return checked(Math.Min(profile.MaximumDelayMilliseconds, delay + (long)(sample % checked((ulong)maximumJitter + 1))));
    }
    private OperationResult<T> ProviderContractInvalid<T>()
    {
        providerGate.QuarantineContractViolation();
        return BaseActivationFailureContract.ProviderContractInvalid<T>();
    }

    private static OperationResult<T> Failure<T>(OperationStatus status, string code, ErrorCategory category) => new()
    { Status = status, Error = new BaseError { Code = code, Message = "The activation operation could not be completed.", Category = category } };
    private static OperationResult<T> CopyFailure<T, TSource>(OperationResult<TSource> source) => new()
    { Status = source.Status, Error = source.Error, Warnings = source.Warnings, Diagnostics = source.Diagnostics };
}
