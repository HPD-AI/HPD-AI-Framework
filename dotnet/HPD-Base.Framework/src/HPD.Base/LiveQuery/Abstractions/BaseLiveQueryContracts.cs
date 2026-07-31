
namespace HPD.Base;

/// <summary>Classifies one server-side live-query transition.</summary>
public enum BaseLiveQueryTransitionKind
{
    Snapshot,
    Failed
}

/// <summary>Contains one successful query evaluation and its complete dependency set.</summary>
public sealed record BaseLiveQueryEvaluation<T>
{
    public required T Value { get; init; }
    public required BaseDependencySet Dependencies { get; init; }
}

/// <summary>Defines one server-side live-query subscription.</summary>
public sealed record BaseLiveQueryRequest<T>
{
    public required string QueryId { get; init; }
    public required Func<CancellationToken, ValueTask<BaseLiveQueryEvaluation<T>>> ExecuteAsync { get; init; }
}

/// <summary>Contains a bounded safe live-query failure.</summary>
public sealed record BaseLiveQueryFailure
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}

/// <summary>Contains one complete replacement result or terminal failure.</summary>
public sealed record BaseLiveQueryTransition<T>
{
    public required BaseLiveQueryTransitionKind Kind { get; init; }
    public long Version { get; init; }
    public T? Value { get; init; }
    public BaseLiveQueryFailure? Failure { get; init; }
}

/// <summary>Represents one active in-process query subscription.</summary>
public interface IBaseLiveQuerySubscription<T> : IAsyncDisposable
{
    string SubscriptionId { get; }
    string QueryId { get; }
    IAsyncEnumerable<BaseLiveQueryTransition<T>> Transitions { get; }
}

/// <summary>Coordinates in-process server query subscriptions and L27 invalidations.</summary>
public interface IBaseLiveQueryCoordinator
{
    ValueTask<IBaseLiveQuerySubscription<T>> SubscribeAsync<T>(
        BaseLiveQueryRequest<T> request,
        CancellationToken cancellationToken = default);

    ValueTask InvalidateAsync(
        BaseDependencyInvalidation invalidation,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable server live-query error codes.</summary>
public static class BaseLiveQueryErrorCodes
{
    public const string ExecutionFailed = "base.liveQuery.executionFailed";
    public const string DependenciesInvalid = "base.liveQuery.dependenciesInvalid";
    public const string InvalidationFailed = "base.liveQuery.invalidationFailed";
    public const string CapacityExceeded = "base.liveQuery.capacityExceeded";
    public const string RequestInvalid = "base.liveQuery.requestInvalid";
}

/// <summary>Identifies a safe live-query open/evaluation failure.</summary>
public sealed class BaseLiveQueryException : Exception
{
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

    public string Code { get; }
    public string SafeMessage { get; }
}
