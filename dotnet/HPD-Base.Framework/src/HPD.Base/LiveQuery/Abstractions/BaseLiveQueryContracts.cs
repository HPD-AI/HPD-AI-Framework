
namespace HPD.Base;

/// <summary>Classifies one server-side live-query transition.</summary>
public enum BaseLiveQueryTransitionKind
{
    /// <summary>Identifies snapshot.</summary>
Snapshot,
    /// <summary>Identifies failed.</summary>
    Failed,
    /// <summary>Identifies an exported-subject authority change that makes the current snapshot stale.</summary>
    SubjectAuthorityChanged
}

/// <summary>
/// Contains one successful query evaluation and every dependency capable of changing its value.
/// </summary>
/// <remarks>
/// The host executor owns read consistency. When a result requires a consistent provider view,
/// the value and dependency set must be obtained from that same consistent evaluation.
/// </remarks>
public sealed record BaseLiveQueryEvaluation<T>
{
    /// <summary>Gets or sets the value.</summary>
    public required T Value { get; init; }
    /// <summary>Gets or sets the dependencies.</summary>
    public required BaseDependencySet Dependencies { get; init; }
}

/// <summary>Defines one server-side live-query subscription.</summary>
/// <remarks>
/// Every invocation must resolve current identity, authorization, and policy state. The executor
/// must not reuse an authorization decision captured by an earlier invocation.
/// </remarks>
public sealed record BaseLiveQueryRequest<T>
{
    /// <summary>Gets or sets the query ID.</summary>
    public required string QueryId { get; init; }
    /// <summary>Gets or sets the execute async.</summary>
    public required Func<CancellationToken, ValueTask<BaseLiveQueryEvaluation<T>>> ExecuteAsync { get; init; }
}

/// <summary>Contains a bounded safe live-query failure.</summary>
public sealed record BaseLiveQueryFailure
{
    /// <summary>Gets or sets the code.</summary>
    public required string Code { get; init; }
    /// <summary>Gets or sets the message.</summary>
    public required string Message { get; init; }
}

/// <summary>Contains one complete replacement result or terminal failure.</summary>
public sealed record BaseLiveQueryTransition<T>
{
    /// <summary>Gets or sets the kind.</summary>
    public required BaseLiveQueryTransitionKind Kind { get; init; }
    /// <summary>Gets or sets the version.</summary>
    public long Version { get; init; }
    /// <summary>Gets or sets the value.</summary>
    public T? Value { get; init; }
    /// <summary>Gets or sets the failure.</summary>
    public BaseLiveQueryFailure? Failure { get; init; }
    /// <summary>Gets the exported-subject contract identity for an authority-control transition.</summary>
    public string? SubjectContractId { get; init; }
    /// <summary>Gets the exported-subject contract version for an authority-control transition.</summary>
    public int? SubjectContractVersion { get; init; }
    /// <summary>Gets the positive publication generation for an authority-control transition.</summary>
    public long? SubjectStateGeneration { get; init; }
}

/// <summary>Represents one active in-process query subscription.</summary>
public interface IBaseLiveQuerySubscription<T> : IAsyncDisposable
{
    /// <summary>Gets the subscription ID.</summary>
    string SubscriptionId { get; }
    /// <summary>Gets the query ID.</summary>
    string QueryId { get; }

    /// <summary>
    /// Gets the non-replayable transition queue. Only one enumerator may read it at a time.
    /// </summary>
    IAsyncEnumerable<BaseLiveQueryTransition<T>> Transitions { get; }
}

/// <summary>Coordinates in-process server query subscriptions and L27 invalidations.</summary>
public interface IBaseLiveQueryCoordinator
{
    /// <summary>Executes the subscribe async operation.</summary>
    ValueTask<IBaseLiveQuerySubscription<T>> SubscribeAsync<T>(
        BaseLiveQueryRequest<T> request,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the invalidate async operation.</summary>
    ValueTask InvalidateAsync(
        BaseDependencyInvalidation invalidation,
        CancellationToken cancellationToken = default);

    /// <summary>Marks matching subscriptions stale and publishes one ordered subject-authority control before rerun snapshots.</summary>
    ValueTask InvalidateSubjectAuthorityAsync(
        BaseSubjectAuthorityPublicationFact publication,
        BaseDependencyInvalidation invalidation,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable server live-query error codes.</summary>
public static class BaseLiveQueryErrorCodes
{
    /// <summary>Provides the execution failed value.</summary>
    public const string ExecutionFailed = "base.liveQuery.executionFailed";
    /// <summary>Provides the dependencies invalid value.</summary>
    public const string DependenciesInvalid = "base.liveQuery.dependenciesInvalid";
    /// <summary>Provides the invalidation failed value.</summary>
    public const string InvalidationFailed = "base.liveQuery.invalidationFailed";
    /// <summary>Provides the capacity exceeded value.</summary>
    public const string CapacityExceeded = "base.liveQuery.capacityExceeded";
    /// <summary>Provides the request invalid value.</summary>
    public const string RequestInvalid = "base.liveQuery.requestInvalid";
}

/// <summary>Identifies a safe live-query open/evaluation failure.</summary>
public sealed class BaseLiveQueryException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public BaseLiveQueryException(string code, string safeMessage)
        : base(safeMessage)
    {
        if (string.IsNullOrWhiteSpace(code)
            || code.Length > 128
            || code.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw new ArgumentException("The live-query failure code is invalid.", nameof(code));
        if (string.IsNullOrWhiteSpace(safeMessage)
            || safeMessage.Length > 256
            || safeMessage.Any(char.IsControl))
            throw new ArgumentException("The live-query failure message is invalid.", nameof(safeMessage));
        Code = code;
        SafeMessage = safeMessage;
    }

    /// <summary>Gets the code.</summary>
    public string Code { get; }
    /// <summary>Gets the safe message.</summary>
    public string SafeMessage { get; }
}
