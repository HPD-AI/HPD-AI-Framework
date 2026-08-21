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

internal sealed record BaseActivationCreationProcessingResult(
    ImmutableArray<string> ActivationIds);
