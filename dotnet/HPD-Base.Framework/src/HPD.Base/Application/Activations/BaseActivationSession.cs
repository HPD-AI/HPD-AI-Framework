using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Defines semantic options for one durable activation creation.</summary>
public sealed record BaseActivationEnqueueOptions
{
    /// <summary>Gets the requested due instant; null means the trusted current instant.</summary>
    public DateTimeOffset? DueAt { get; init; }
}

/// <summary>Contains one durable activation creation result.</summary>
public sealed record BaseActivationEnqueueResult
{
    /// <summary>Gets the deterministic activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the initial durable state.</summary>
    public required BaseActivationState State { get; init; }
    /// <summary>Gets whether this call committed or resolved an exact duplicate.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Resolves graph-installed activation definitions for one principal-bound session.</summary>
public sealed class BaseActivationSession
{
    private readonly BaseSession _session;
    internal BaseActivationSession(BaseSession session) => _session = session;

    /// <summary>Resolves an inert generated identity to an executable session-bound handle.</summary>
    public BaseInstalledActivationHandle<TInput, TResult> Get<TInput, TResult>(
        BaseActivationRegistrationIdentity<TInput, TResult> identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        BaseActivationRegistry registry = _session.Services.GetService(typeof(BaseActivationRegistry)) as BaseActivationRegistry
            ?? throw new InvalidOperationException("base.activation.notInstalled");
        BaseActivationDefinition definition = registry.Find(identity.Id, identity.Version)
            ?? throw new InvalidOperationException("base.activation.notInstalled");
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            definition.Checksum.AsSpan(), identity.Checksum.Span))
            throw new InvalidOperationException("base.activation.schemaChanged");
        IBaseActivationRuntime runtime = _session.Services.GetService(typeof(IBaseActivationRuntime)) as IBaseActivationRuntime
            ?? throw new InvalidOperationException("base.activation.notInstalled");
        if (registry.Registration(identity.Id, identity.Version)?.Identity is not BaseActivationRegistrationIdentity<TInput, TResult> installed)
            throw new InvalidOperationException("base.activation.schemaChanged");
        return new BaseInstalledActivationHandle<TInput, TResult>(runtime, _session, definition, installed);
    }

    /// <summary>Resolves an inert generated identity to a Service/System worker handle.</summary>
    public BaseInstalledActivationWorkerHandle<TInput, TResult> GetWorker<TInput, TResult>(
        BaseActivationRegistrationIdentity<TInput, TResult> identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        BaseActivationRegistry registry = _session.Services.GetService(typeof(BaseActivationRegistry)) as BaseActivationRegistry
            ?? throw new InvalidOperationException("base.activation.notInstalled");
        BaseActivationDefinition definition = registry.Find(identity.Id, identity.Version)
            ?? throw new InvalidOperationException("base.activation.notInstalled");
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(definition.Checksum.AsSpan(), identity.Checksum.Span))
            throw new InvalidOperationException("base.activation.schemaChanged");
        IBaseActivationWorkerRuntime runtime = _session.Services.GetService(typeof(IBaseActivationWorkerRuntime)) as IBaseActivationWorkerRuntime
            ?? throw new InvalidOperationException("base.activation.notInstalled");
        if (registry.Registration(identity.Id, identity.Version)?.Identity is not BaseActivationRegistrationIdentity<TInput, TResult> installed)
            throw new InvalidOperationException("base.activation.schemaChanged");
        return new BaseInstalledActivationWorkerHandle<TInput, TResult>(runtime, _session, definition, installed);
    }

    /// <summary>Resolves an inert schedule identity to a principal-bound installed handle.</summary>
    public BaseInstalledScheduleHandle GetSchedule(BaseScheduleRegistrationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        BaseScheduleRegistry registry = _session.Services.GetService(typeof(BaseScheduleRegistry)) as BaseScheduleRegistry
            ?? throw new InvalidOperationException("base.activation.scheduleNotInstalled");
        BaseScheduleDefinition definition = registry.Find(identity.Id, identity.Version)
            ?? throw new InvalidOperationException("base.activation.scheduleNotInstalled");
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            definition.Checksum.AsSpan(), identity.Checksum.Span))
            throw new InvalidOperationException("base.activation.scheduleChanged");
        IBaseScheduleRuntime runtime = _session.Services.GetService(typeof(IBaseScheduleRuntime)) as IBaseScheduleRuntime
            ?? throw new InvalidOperationException("base.activation.scheduleNotInstalled");
        return new BaseInstalledScheduleHandle(runtime, _session, definition);
    }
}

/// <summary>Executes one exact graph-installed activation through its owning session.</summary>
public sealed class BaseInstalledActivationHandle<TInput, TResult>
{
    private readonly IBaseActivationRuntime _runtime;
    private readonly BaseSession _session;
    private readonly BaseActivationDefinition _definition;
    private readonly BaseActivationRegistrationIdentity<TInput, TResult> _identity;

    internal BaseInstalledActivationHandle(
        IBaseActivationRuntime runtime,
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationRegistrationIdentity<TInput, TResult> identity)
    {
        _runtime = runtime;
        _session = session;
        _definition = definition;
        _identity = identity;
    }

    /// <summary>Creates or exactly resolves one identified durable activation.</summary>
    public ValueTask<OperationResult<BaseActivationEnqueueResult>> EnqueueAsync(
        TInput input,
        BaseMutationRequestIdentity identity,
        BaseActivationEnqueueOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _runtime.EnqueueAsync(_session, _definition, _identity, input, identity, options, cancellationToken);
}

/// <summary>Contains one typed, inert claimed activation delivery.</summary>
public sealed record BaseActivationDelivery<TInput>
{
    /// <summary>Gets the exact durable activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the graph-decoded immutable input.</summary>
    public required TInput Input { get; init; }
    /// <summary>Gets stable claim authority.</summary>
    public required BaseActivationClaimAuthority Claim { get; init; }
    /// <summary>Gets current renewable lease observation.</summary>
    public required BaseActivationLeaseObservation Lease { get; init; }
    /// <summary>Gets the exact attempt observation.</summary>
    public required BaseActivationAttemptEvidence Attempt { get; init; }
    /// <summary>Gets the protected semantic scope inherited by guarded child work.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the immutable schedule occurrence identity, when scheduled.</summary>
    public string? OccurrenceId { get; init; }
    /// <summary>Gets the requested due instant as Unix milliseconds.</summary>
    public required long RequestedDueAt { get; init; }
    /// <summary>Gets the effective due instant after deterministic scheduling policy.</summary>
    public required long EffectiveDueAt { get; init; }
}

/// <summary>Contains one bounded installed-worker dispatch outcome.</summary>
public sealed record BaseActivationDispatchResult
{
    /// <summary>Gets whether no due activation was available.</summary>
    public required bool Empty { get; init; }
    /// <summary>Gets the claimed activation identity when work was dispatched.</summary>
    public string? ActivationId { get; init; }
    /// <summary>Gets the resulting durable state when work was dispatched.</summary>
    public BaseActivationState? State { get; init; }
}

/// <summary>Contains one currently authorized historical activation result.</summary>
public sealed record BaseActivationResultReceipt<TResult>
{
    /// <summary>Gets the closed durable operation kind that committed the result.</summary>
    public required string OperationKind { get; init; }
    /// <summary>Gets the historical terminal activation state.</summary>
    public required BaseActivationState State { get; init; }
    /// <summary>Gets the historical terminal activation generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the graph-decoded historical result.</summary>
    public required TResult Result { get; init; }
}

/// <summary>Executes worker operations for one installed definition and principal-bound session.</summary>
public sealed class BaseInstalledActivationWorkerHandle<TInput, TResult>
{
    private readonly IBaseActivationWorkerRuntime _runtime;
    private readonly BaseSession _session;
    private readonly BaseActivationDefinition _definition;
    private readonly BaseActivationRegistrationIdentity<TInput, TResult> _identity;

    internal BaseInstalledActivationWorkerHandle(
        IBaseActivationWorkerRuntime runtime,
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationRegistrationIdentity<TInput, TResult> identity)
    {
        _runtime = runtime;
        _session = session;
        _definition = definition;
        _identity = identity;
    }

    /// <summary>Observes finite due authority without claiming work.</summary>
    public ValueTask<OperationResult<BaseActivationDueObservation>> ObserveDueAsync(
        CancellationToken cancellationToken = default) =>
        _runtime.ObserveAsync(_session, _definition, cancellationToken);

    /// <summary>Atomically claims the earliest activation under one finite observation.</summary>
    public async ValueTask<OperationResult<BaseActivationDelivery<TInput>?>> TryClaimAsync(
        BaseDueObservationToken observation,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken = default)
    {
        OperationResult<BaseActivationClaimResult> claimed = await _runtime.ClaimAsync(
            _session, _definition, observation, identity, cancellationToken).ConfigureAwait(false);
        if (!claimed.IsSuccess())
            return new OperationResult<BaseActivationDelivery<TInput>?>
            { Status = claimed.Status, Error = claimed.Error, Warnings = claimed.Warnings, Diagnostics = claimed.Diagnostics };
        if (claimed.Value is not BaseActivationClaimedResult value)
            return OperationResults.Ok<BaseActivationDelivery<TInput>?>(null);
        TInput? input;
        try { input = System.Text.Json.JsonSerializer.Deserialize(value.Payload.CanonicalInput.AsSpan(), _identity.Input); }
        catch
        {
            return InvalidDelivery();
        }
        if (input is null) return InvalidDelivery();
        return OperationResults.Ok<BaseActivationDelivery<TInput>?>(new BaseActivationDelivery<TInput>
        {
            ActivationId = value.Payload.ActivationId,
            Input = input,
            Claim = value.Claim,
            Lease = value.Lease,
            Attempt = value.Attempt,
            Scope = value.Payload.Scope with { },
            OccurrenceId = value.Payload.OccurrenceId,
            RequestedDueAt = value.Payload.RequestedDueAt,
            EffectiveDueAt = value.Payload.EffectiveDueAt,
        });
    }

    /// <summary>Renews the exact current lease without replacing stable claim authority.</summary>
    public ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(
        BaseActivationDelivery<TInput> delivery,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken = default) =>
        _runtime.RenewAsync(_session, _definition, delivery.Claim, delivery.Lease, identity, cancellationToken);

    /// <summary>Commits the graph-encoded terminal result under the current fence.</summary>
    public ValueTask<OperationResult<BaseActivationTransitionResult>> CompleteAsync(
        BaseActivationDelivery<TInput> delivery,
        TResult result,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, _identity.Result);
        if (bytes.LongLength > _definition.Limits.MaximumResultBytes)
            return ValueTask.FromResult(new OperationResult<BaseActivationTransitionResult>
            { Status = OperationStatus.ValidationFailed, Error = new BaseError { Code = "base.activation.budgetExceeded", Message = "The activation result exceeds its configured bound.", Category = ErrorCategory.Validation } });
        return _runtime.CompleteAsync(_session, _definition, delivery.Claim, bytes.ToImmutableArray(), identity, cancellationToken);
    }

    /// <summary>Commits a stable failed-attempt outcome under the current fence.</summary>
    public ValueTask<OperationResult<BaseActivationTransitionResult>> FailAsync(
        BaseActivationDelivery<TInput> delivery,
        string stableFailureCode,
        bool retry,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken = default) =>
        _runtime.FailAsync(_session, _definition, delivery.Claim, stableFailureCode, retry, identity, cancellationToken);

    /// <summary>Resolves one successful historical result under current replay authority.</summary>
    public async ValueTask<OperationResult<BaseActivationResultReceipt<TResult>>> ResolveResultAsync(
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken = default)
    {
        OperationResult<BaseActivationReceiptResolution> resolved = await _runtime.ResolveReceiptAsync(
            _session, _definition, _identity.ResultBindings, identity, cancellationToken).ConfigureAwait(false);
        if (!resolved.IsSuccess() || resolved.Value is null)
            return CopyFailure<BaseActivationResultReceipt<TResult>, BaseActivationReceiptResolution>(resolved);
        if (resolved.Value.OperationKind is not ("activation-completed" or "effect-completed" or "effect-reconciled"))
            return ReceiptFailure("base.activation.receiptKindInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        BaseActivationTransitionResult? transition;
        TResult? result;
        try
        {
            transition = System.Text.Json.JsonSerializer.Deserialize(
                resolved.Value.CanonicalResult.AsSpan(), HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult);
            if (transition is null || transition.State != BaseActivationState.Succeeded || transition.CanonicalResult.IsDefaultOrEmpty)
                return ReceiptFailure("base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
            result = System.Text.Json.JsonSerializer.Deserialize(transition.CanonicalResult.AsSpan(), _identity.Result);
        }
        catch
        {
            return ReceiptFailure("base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
        }
        if (result is null)
            return ReceiptFailure("base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
        return OperationResults.Ok(new BaseActivationResultReceipt<TResult>
        {
            OperationKind = resolved.Value.OperationKind,
            State = transition.State,
            Generation = transition.Generation,
            Result = result,
        });
    }

    /// <summary>Observes, claims, executes, and durably resolves at most one due activation.</summary>
    public async ValueTask<OperationResult<BaseActivationDispatchResult>> RunOneAsync(
        CancellationToken cancellationToken = default)
    {
        OperationResult<BaseActivationDueObservation> observed = await ObserveDueAsync(cancellationToken).ConfigureAwait(false);
        if (!observed.IsSuccess() || observed.Value is null)
            return CopyFailure<BaseActivationDispatchResult, BaseActivationDueObservation>(observed);
        if (observed.Value.Earliest is null)
            return OperationResults.Ok(new BaseActivationDispatchResult { Empty = true });

        if (_definition.ExecutionClass == BaseActivationExecutionClass.TransactionalOperation)
            return await _runtime.ExecuteTransactionalAsync(
                _session, _definition, observed.Value.Token, cancellationToken).ConfigureAwait(false);

        BaseMutationRequestIdentity claimIdentity = Identity(
            "claim", observed.Value.Token.Value.AsSpan(), observed.Value.Earliest.ActivationId);
        OperationResult<BaseActivationDelivery<TInput>?> claimed = await TryClaimAsync(
            observed.Value.Token, claimIdentity, cancellationToken).ConfigureAwait(false);
        if (!claimed.IsSuccess())
            return CopyFailure<BaseActivationDispatchResult, BaseActivationDelivery<TInput>?>(claimed);
        if (claimed.Value is null)
            return OperationResults.Ok(new BaseActivationDispatchResult { Empty = true });

        OperationResult executionAuthority = await _runtime.AuthorizeExecutionAsync(
            _session, _definition, cancellationToken).ConfigureAwait(false);
        if (!executionAuthority.IsSuccess())
            return new OperationResult<BaseActivationDispatchResult>
            {
                Status = executionAuthority.Status,
                Error = executionAuthority.Error,
                Warnings = executionAuthority.Warnings,
                Diagnostics = executionAuthority.Diagnostics,
            };

        IBaseActivationRegistration? registration = (_session.Services.GetService(typeof(BaseActivationRegistry)) as BaseActivationRegistry)
            ?.Registration(_definition.Id, _definition.Version);
        if (registration?.CreateHandler(_session.Services) is not IBaseActivationHandler<TInput, TResult> handler)
            return Failure("base.activation.handlerUnavailable", ErrorCategory.Capability);

        BaseActivationDelivery<TInput> delivery = claimed.Value;
        BaseEffectExecutionAuthority? effect = null;
        if (_definition.ExecutionClass == BaseActivationExecutionClass.AtMostOnceEffect)
        {
            OperationResult<BaseActivationTransitionResult> begun = await _runtime.BeginEffectAsync(
                _session, _definition, delivery.Claim,
                Identity("effect-start", delivery.Claim.FencingToken.AsSpan(), delivery.ActivationId),
                cancellationToken).ConfigureAwait(false);
            if (!begun.IsSuccess() || begun.Value?.Effect is null)
                return CopyFailure<BaseActivationDispatchResult, BaseActivationTransitionResult>(begun);
            effect = begun.Value.Effect;
        }
        BaseActivationHandlerExecutionGate? gate = _session.Services.GetService(typeof(BaseActivationHandlerExecutionGate)) as BaseActivationHandlerExecutionGate;
        if (gate is null) return Failure("base.activation.handlerUnavailable", ErrorCategory.Capability);
        BaseActivationHandlerExecutionResult<BaseActivationHandlerResult<TResult>> execution = await gate.ExecuteAsync(
            token => handler.ExecuteAsync(new BaseActivationContext(
                new BaseActivationDefinitionKey { Id = _definition.Id, Version = _definition.Version, Checksum = _definition.Checksum },
                delivery.Claim,
                delivery.Lease,
                delivery.Scope,
                delivery.OccurrenceId,
                delivery.RequestedDueAt,
                delivery.EffectiveDueAt,
                _definition.Limits.MaximumRenewalsPerAttempt,
                (lease, renewCancellation) => _runtime.RenewAsync(
                    _session,
                    _definition,
                    delivery.Claim,
                    lease,
                    Identity(
                        "renew",
                        delivery.Claim.FencingToken.AsSpan(),
                        lease.LeaseRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    renewCancellation),
                token,
                _definition.Limits.MaximumChildrenPerAttempt), delivery.Input, token).AsTask(),
            _definition.Limits.HandlerTimeout,
            cancellationToken).ConfigureAwait(false);
        if (effect is not null && execution.Outcome != BaseActivationHandlerExecutionOutcome.Completed)
            return OperationResults.Ok(new BaseActivationDispatchResult
            { Empty = false, ActivationId = delivery.ActivationId, State = BaseActivationState.EffectStarted });
        if (execution.Outcome == BaseActivationHandlerExecutionOutcome.TimedOut)
            return await ResolveFailureAsync(delivery, "base.activation.handlerTimeout", cancellationToken).ConfigureAwait(false);
        if (execution.Outcome == BaseActivationHandlerExecutionOutcome.Cancelled)
            return Failure("base.activation.cancelled", ErrorCategory.Store);
        if (execution.Outcome is BaseActivationHandlerExecutionOutcome.Failed or BaseActivationHandlerExecutionOutcome.Capacity || execution.Value is null)
            return await ResolveFailureAsync(delivery, "base.activation.handlerFailed", cancellationToken).ConfigureAwait(false);
        BaseActivationHandlerResult<TResult> handlerResult = execution.Value;

        OperationResult<BaseActivationTransitionResult> transition;
        if (effect is not null)
        {
            if (handlerResult.Result is null)
                return OperationResults.Ok(new BaseActivationDispatchResult
                { Empty = false, ActivationId = delivery.ActivationId, State = BaseActivationState.EffectStarted });
            byte[] resultBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(handlerResult.Result, _identity.Result);
            transition = await _runtime.CompleteEffectAsync(
                _session, _definition, effect, resultBytes.ToImmutableArray(),
                Identity("effect-complete", effect.Checksum.AsSpan(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(resultBytes))),
                cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(handlerResult.FailureCode))
        {
            transition = await FailAsync(delivery, handlerResult.FailureCode, handlerResult.Retryable,
                Identity("fail", delivery.Claim.FencingToken.AsSpan(), handlerResult.FailureCode), cancellationToken).ConfigureAwait(false);
        }
        else if (handlerResult.Result is not null)
        {
            byte[] resultBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(handlerResult.Result, _identity.Result);
            transition = await CompleteAsync(delivery, handlerResult.Result,
                Identity("complete", delivery.Claim.FencingToken.AsSpan(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(resultBytes))), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            transition = await FailAsync(delivery, "base.activation.handlerContractInvalid", false,
                Identity("fail", delivery.Claim.FencingToken.AsSpan(), "contract"), cancellationToken).ConfigureAwait(false);
        }
        return transition.IsSuccess() && transition.Value is not null
            ? OperationResults.Ok(new BaseActivationDispatchResult { Empty = false, ActivationId = delivery.ActivationId, State = transition.Value.State })
            : CopyFailure<BaseActivationDispatchResult, BaseActivationTransitionResult>(transition);
    }

    private async ValueTask<OperationResult<BaseActivationDispatchResult>> ResolveFailureAsync(
        BaseActivationDelivery<TInput> delivery,
        string failureCode,
        CancellationToken cancellationToken)
    {
        OperationResult<BaseActivationTransitionResult> transition = await FailAsync(
            delivery, failureCode, true, Identity("fail", delivery.Claim.FencingToken.AsSpan(), failureCode), cancellationToken).ConfigureAwait(false);
        return transition.IsSuccess() && transition.Value is not null
            ? OperationResults.Ok(new BaseActivationDispatchResult { Empty = false, ActivationId = delivery.ActivationId, State = transition.Value.State })
            : CopyFailure<BaseActivationDispatchResult, BaseActivationTransitionResult>(transition);
    }

    private BaseMutationRequestIdentity Identity(string operation, ReadOnlySpan<byte> authority, string discriminator)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(authority);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(discriminator));
        return BaseMutationRequestIdentity.Create(
            $"activation:{_definition.Id}:{_definition.Version}",
            operation,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(authority)),
            BaseMutationRequestFingerprint.Create(hash.GetHashAndReset()));
    }

    private static OperationResult<BaseActivationDispatchResult> Failure(string code, ErrorCategory category) => new()
    {
        Status = OperationStatus.StoreError,
        Error = new BaseError { Code = code, Message = "The activation worker could not complete the operation.", Category = category },
    };

    private static OperationResult<T> CopyFailure<T, TSource>(OperationResult<TSource> source) => new()
    { Status = source.Status, Error = source.Error, Warnings = source.Warnings, Diagnostics = source.Diagnostics };

    private static OperationResult<BaseActivationDelivery<TInput>?> InvalidDelivery() => new()
    {
        Status = OperationStatus.StoreError,
        Error = new BaseError { Code = "base.activation.providerContractInvalid", Message = "The activation payload is invalid.", Category = ErrorCategory.Store },
    };

    private static OperationResult<BaseActivationResultReceipt<TResult>> ReceiptFailure(
        string code, OperationStatus status, ErrorCategory category) => new()
    {
        Status = status,
        Error = new BaseError { Code = code, Message = "The activation receipt could not be resolved.", Category = category },
    };
}

internal interface IBaseActivationRuntime
{
    ValueTask<OperationResult<BaseActivationEnqueueResult>> EnqueueAsync<TInput, TResult>(
        BaseSession session,
        BaseActivationDefinition definition,
        BaseActivationRegistrationIdentity<TInput, TResult> identity,
        TInput input,
        BaseMutationRequestIdentity requestIdentity,
        BaseActivationEnqueueOptions? options,
        CancellationToken cancellationToken);
}

internal interface IBaseActivationWorkerRuntime
{
    ValueTask<OperationResult<BaseActivationDispatchResult>> ExecuteTransactionalAsync(
        BaseSession session, BaseActivationDefinition definition, BaseDueObservationToken observation,
        CancellationToken cancellationToken);
    ValueTask<OperationResult> AuthorizeExecutionAsync(
        BaseSession session, BaseActivationDefinition definition, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseActivationTransitionResult>> BeginEffectAsync(
        BaseSession session, BaseActivationDefinition definition, BaseActivationClaimAuthority claim,
        BaseMutationRequestIdentity identity, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseActivationTransitionResult>> CompleteEffectAsync(
        BaseSession session, BaseActivationDefinition definition, BaseEffectExecutionAuthority effect,
        ImmutableArray<byte> result, BaseMutationRequestIdentity identity, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseActivationDueObservation>> ObserveAsync(
        BaseSession session, BaseActivationDefinition definition, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseActivationClaimResult>> ClaimAsync(
        BaseSession session, BaseActivationDefinition definition, BaseDueObservationToken observation,
        BaseMutationRequestIdentity identity, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(
        BaseSession session, BaseActivationDefinition definition, BaseActivationClaimAuthority claim,
        BaseActivationLeaseObservation lease, BaseMutationRequestIdentity identity, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseActivationTransitionResult>> CompleteAsync(
        BaseSession session, BaseActivationDefinition definition, BaseActivationClaimAuthority claim,
        ImmutableArray<byte> result, BaseMutationRequestIdentity identity, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseActivationTransitionResult>> FailAsync(
        BaseSession session, BaseActivationDefinition definition, BaseActivationClaimAuthority claim,
        string failureCode, bool retry, BaseMutationRequestIdentity identity, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseActivationReceiptResolution>> ResolveReceiptAsync(
        BaseSession session, BaseActivationDefinition definition,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings, BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken);
}

internal interface IBaseScheduleRuntime
{
    ValueTask<OperationResult<BaseScheduleAuthority>> ReadAsync(
        BaseSession session, BaseScheduleDefinition definition, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseScheduleMutationResult>> MutateAsync(
        BaseSession session, BaseScheduleDefinition definition, BaseScheduleMutationKind kind,
        long? expectedGeneration, BaseMutationRequestIdentity identity, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceAsync(
        BaseSession session, BaseScheduleDefinition definition, BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken);
}

/// <summary>Operates one graph-installed durable schedule through a principal-bound session.</summary>
public sealed class BaseInstalledScheduleHandle
{
    private readonly IBaseScheduleRuntime _runtime;
    private readonly BaseSession _session;
    private readonly BaseScheduleDefinition _definition;

    internal BaseInstalledScheduleHandle(IBaseScheduleRuntime runtime, BaseSession session, BaseScheduleDefinition definition)
        => (_runtime, _session, _definition) = (runtime, session, definition);

    /// <summary>Reads current durable schedule authority.</summary>
    public ValueTask<OperationResult<BaseScheduleAuthority>> ReadAsync(CancellationToken cancellationToken = default) =>
        _runtime.ReadAsync(_session, _definition, cancellationToken);

    /// <summary>Creates current schedule authority.</summary>
    public ValueTask<OperationResult<BaseScheduleMutationResult>> CreateAsync(
        BaseMutationRequestIdentity identity, CancellationToken cancellationToken = default) =>
        _runtime.MutateAsync(_session, _definition, BaseScheduleMutationKind.Create, null, identity, cancellationToken);

    /// <summary>Replaces current schedule semantics under an exact generation.</summary>
    public ValueTask<OperationResult<BaseScheduleMutationResult>> UpdateAsync(
        long expectedGeneration, BaseMutationRequestIdentity identity, CancellationToken cancellationToken = default) =>
        _runtime.MutateAsync(_session, _definition, BaseScheduleMutationKind.Update, expectedGeneration, identity, cancellationToken);

    /// <summary>Enables future occurrence materialization.</summary>
    public ValueTask<OperationResult<BaseScheduleMutationResult>> EnableAsync(
        long expectedGeneration, BaseMutationRequestIdentity identity, CancellationToken cancellationToken = default) =>
        _runtime.MutateAsync(_session, _definition, BaseScheduleMutationKind.Enable, expectedGeneration, identity, cancellationToken);

    /// <summary>Disables future occurrence materialization.</summary>
    public ValueTask<OperationResult<BaseScheduleMutationResult>> DisableAsync(
        long expectedGeneration, BaseMutationRequestIdentity identity, CancellationToken cancellationToken = default) =>
        _runtime.MutateAsync(_session, _definition, BaseScheduleMutationKind.Disable, expectedGeneration, identity, cancellationToken);

    /// <summary>Removes current authority while retaining immutable occurrence history.</summary>
    public ValueTask<OperationResult<BaseScheduleMutationResult>> RemoveAsync(
        long expectedGeneration, BaseMutationRequestIdentity identity, CancellationToken cancellationToken = default) =>
        _runtime.MutateAsync(_session, _definition, BaseScheduleMutationKind.Remove, expectedGeneration, identity, cancellationToken);

    /// <summary>Materializes one bounded deterministic page through trusted current time.</summary>
    public ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceAsync(
        BaseMutationRequestIdentity identity, CancellationToken cancellationToken = default) =>
        _runtime.AdvanceAsync(_session, _definition, identity, cancellationToken);
}

internal sealed record BaseActivationCreationProcessingResult(
    ImmutableArray<string> ActivationIds);
