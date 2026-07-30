using System.Runtime.CompilerServices;
using HPD.Base.Dependencies;
using HPD.Base.LiveQuery;

namespace HPD.Base.Application.LiveQueries;

/// <summary>Opens valid typed server-side live-query subscriptions.</summary>
public sealed class BaseSessionLiveQueries(IBaseLiveQueryCoordinator coordinator)
{
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
    public required T Value { get; init; }
    public required BaseDependencySet Dependencies { get; init; }
}

/// <summary>Factory for application live-query execution results.</summary>
public static class LiveQuery
{
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
public abstract record BaseLiveQueryTransition<T>
{
    private protected BaseLiveQueryTransition(long version) => Version = version;
    public long Version { get; }
}

public sealed record BaseLiveQuerySnapshot<T> : BaseLiveQueryTransition<T>
{
    internal BaseLiveQuerySnapshot(long version, T value) : base(version) => Value = value;
    public T Value { get; }
}

public sealed record BaseLiveQueryFailed<T> : BaseLiveQueryTransition<T>
{
    internal BaseLiveQueryFailed(long version, string code, string safeMessage) : base(version)
    {
        Code = code;
        SafeMessage = safeMessage;
    }

    public string Code { get; }
    public string SafeMessage { get; }
}

/// <summary>Application wrapper that never exposes discriminator-plus-nullable transitions.</summary>
public sealed class BaseLiveQuerySubscription<T>(
    IBaseLiveQuerySubscription<T> inner) : IAsyncDisposable
{
    public string SubscriptionId => inner.SubscriptionId;
    public string QueryId => inner.QueryId;
    public IAsyncEnumerable<BaseLiveQueryTransition<T>> Transitions =>
        Project(inner.Transitions);
    public ValueTask DisposeAsync() => inner.DisposeAsync();

    private static async IAsyncEnumerable<BaseLiveQueryTransition<T>> Project(
        IAsyncEnumerable<HPD.Base.LiveQuery.BaseLiveQueryTransition<T>> transitions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (HPD.Base.LiveQuery.BaseLiveQueryTransition<T> transition
            in transitions.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
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
