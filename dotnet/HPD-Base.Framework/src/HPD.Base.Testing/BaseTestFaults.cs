using HPD.Base.Results;

namespace HPD.Base.Testing;

/// <summary>
/// Provides deterministic, one-shot failures for application test hosts.
/// </summary>
public sealed class BaseTestFaults
{
    private int _atomicCommitFailures;
    private int _observerFailures;

    /// <summary>Fails the next atomic batch before it reaches the provider.</summary>
    public void FailNextAtomicCommit() =>
        Interlocked.Increment(ref _atomicCommitFailures);

    /// <summary>Fails the test observer after the next committed mutation.</summary>
    public void FailNextPostCommitObserver() =>
        Interlocked.Increment(ref _observerFailures);

    internal bool TakeAtomicCommitFailure() => Take(ref _atomicCommitFailures);
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
