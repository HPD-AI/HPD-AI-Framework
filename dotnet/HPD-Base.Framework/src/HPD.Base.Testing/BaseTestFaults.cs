using HPD.Base;

namespace HPD.Base.Testing;

/// <summary>
/// Provides deterministic, one-shot failures for application test hosts.
/// </summary>
public sealed class BaseTestFaults
{
    private int _atomicCommitFailures;
    private int _indeterminateAtomicCommits;
    private int _observerFailures;

    /// <summary>
    /// Fails the next atomic batch at the provider commit boundary after provisional
    /// transactional work, requiring the provider to confirm rollback.
    /// </summary>
    public void FailNextAtomicCommit() =>
        Interlocked.Increment(ref _atomicCommitFailures);

    /// <summary>
    /// Makes the next successfully committed atomic batch return an indeterminate
    /// acknowledgement without exposing provisional item results.
    /// </summary>
    public void MakeNextAtomicCommitIndeterminate() =>
        Interlocked.Increment(ref _indeterminateAtomicCommits);

    /// <summary>Fails the test observer after the next committed mutation.</summary>
    public void FailNextPostCommitObserver() =>
        Interlocked.Increment(ref _observerFailures);

    internal bool TakeAtomicCommitFailure() => Take(ref _atomicCommitFailures);
    internal bool TakeIndeterminateAtomicCommit() =>
        Take(ref _indeterminateAtomicCommits);
    internal bool TakeObserverFailure() => Take(ref _observerFailures);

    internal static BaseError AtomicCommitError() => new()
    {
        Code = "base.testing.atomicCommitFailed",
        Message = "The test host injected an atomic commit failure.",
        Category = ErrorCategory.Store,
        Store = new StoreErrorInfo
        {
            StoreId = "base.testing",
            Retryable = true,
        },
    };

    private static bool Take(ref int count)
    {
        while (true)
        {
            int current = Volatile.Read(ref count);
            if (current == 0)
                return false;
            if (Interlocked.CompareExchange(ref count, current - 1, current) == current)
                return true;
        }
    }
}
