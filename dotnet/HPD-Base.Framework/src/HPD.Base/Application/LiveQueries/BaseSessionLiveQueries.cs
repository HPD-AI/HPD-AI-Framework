using System.Runtime.CompilerServices;

namespace HPD.Base;

/// <summary>Opens valid typed server-side live-query subscriptions.</summary>
public sealed class BaseSessionLiveQueries(IBaseLiveQueryCoordinator coordinator)
{
    /// <summary>Executes the subscribe async operation.</summary>
    public async ValueTask<BaseLiveQuerySubscription<T>> SubscribeAsync<T>(
        string queryId,
        Func<CancellationToken, ValueTask<BaseLiveQueryResult<T>>> executeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentNullException.ThrowIfNull(executeAsync);

        IBaseLiveQuerySubscription<T> subscription =
            await coordinator.SubscribeAsync(
                new BaseLiveQueryRequest<T>
                {
                    QueryId = queryId,
                    ExecuteAsync = async token =>
                    {
                        BaseLiveQueryResult<T> result =
                            await executeAsync(token).ConfigureAwait(false);
                        return new BaseLiveQueryEvaluation<T>
                        {
                            Value = result.Value,
                            Dependencies = result.Dependencies,
                        };
                    },
                },
                cancellationToken).ConfigureAwait(false);

        return new BaseLiveQuerySubscription<T>(subscription);
    }
}

/// <summary>One complete execution result plus every dependency that can stale it.</summary>
public sealed record BaseLiveQueryResult<T>
{
    /// <summary>Gets or sets the value.</summary>
    public required T Value { get; init; }
    /// <summary>Gets or sets the dependencies.</summary>
    public required BaseDependencySet Dependencies { get; init; }
}

/// <summary>Factory for application live-query execution results.</summary>
public static class LiveQuery
{
    /// <summary>Executes the result operation.</summary>
    public static BaseLiveQueryResult<T> Result<T>(
        T value,
        BaseDependencySet dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        return new BaseLiveQueryResult<T>
        {
            Value = value,
            Dependencies = dependencies,
        };
    }
}

/// <summary>Closed valid application transition hierarchy.</summary>
public abstract record BaseLiveQueryUpdate<T>
{
    /// <summary>Initializes a new instance.</summary>
    private protected BaseLiveQueryUpdate(long version) => Version = version;
    /// <summary>Gets the version.</summary>
    public long Version { get; }
}

/// <summary>Represents a base live query snapshot.</summary>
public sealed record BaseLiveQuerySnapshot<T> : BaseLiveQueryUpdate<T>
{
    internal BaseLiveQuerySnapshot(long version, T value) : base(version) => Value = value;
    /// <summary>Gets the value.</summary>
    public T Value { get; }
}

/// <summary>Represents a base live query failed.</summary>
public sealed record BaseLiveQueryFailed<T> : BaseLiveQueryUpdate<T>
{
    internal BaseLiveQueryFailed(long version, string code, string safeMessage) : base(version)
    {
        Code = code;
        SafeMessage = safeMessage;
    }

    /// <summary>Gets the code.</summary>
    public string Code { get; }
    /// <summary>Gets the safe message.</summary>
    public string SafeMessage { get; }
}

/// <summary>Application wrapper that never exposes discriminator-plus-nullable transitions.</summary>
public sealed class BaseLiveQuerySubscription<T>(
    IBaseLiveQuerySubscription<T> inner) : IAsyncDisposable
{
    /// <summary>Gets the subscription ID.</summary>
    public string SubscriptionId => inner.SubscriptionId;
    /// <summary>Gets the query ID.</summary>
    public string QueryId => inner.QueryId;
    /// <summary>Gets the transitions.</summary>
    public IAsyncEnumerable<BaseLiveQueryUpdate<T>> Transitions =>
        Project(inner.Transitions);
    /// <summary>Executes the dispose async operation.</summary>
    public ValueTask DisposeAsync() => inner.DisposeAsync();

    private static async IAsyncEnumerable<BaseLiveQueryUpdate<T>> Project(
        IAsyncEnumerable<BaseLiveQueryTransition<T>> transitions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (BaseLiveQueryTransition<T> transition
            in transitions.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (transition.Kind == BaseLiveQueryTransitionKind.SubjectAuthorityChanged)
                continue;
            if (transition.Kind == BaseLiveQueryTransitionKind.Snapshot &&
                transition.Value is not null)
            {
                yield return new BaseLiveQuerySnapshot<T>(
                    transition.Version,
                    transition.Value);
                continue;
            }

            if (transition.Kind == BaseLiveQueryTransitionKind.Failed &&
                transition.Failure is not null)
            {
                yield return new BaseLiveQueryFailed<T>(
                    transition.Version,
                    transition.Failure.Code,
                    transition.Failure.Message);
                yield break;
            }

            yield return new BaseLiveQueryFailed<T>(
                transition.Version,
                "base.liveQuery.invalidTransition",
                "The live query produced an invalid transition.");
            yield break;
        }
    }
}
