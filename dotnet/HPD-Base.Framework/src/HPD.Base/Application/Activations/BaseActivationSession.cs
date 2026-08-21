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
        return new BaseInstalledActivationHandle<TInput, TResult>(runtime, _session, definition, identity);
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
        return new BaseInstalledActivationWorkerHandle<TInput, TResult>(runtime, _session, definition, identity);
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

    private static OperationResult<BaseActivationDelivery<TInput>?> InvalidDelivery() => new()
    {
        Status = OperationStatus.StoreError,
        Error = new BaseError { Code = "base.activation.providerContractInvalid", Message = "The activation payload is invalid.", Category = ErrorCategory.Store },
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
}

internal sealed record BaseActivationCreationProcessingResult(
    ImmutableArray<string> ActivationIds);
