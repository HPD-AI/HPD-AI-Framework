namespace HPD.Base.Testing;

/// <summary>Thread-safe deterministic time source owned by one test host.</summary>
public sealed class BaseTestTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private long _utcTicks = initial.UtcTicks;

    /// <summary>Executes the get utc now operation.</summary>
    public override DateTimeOffset GetUtcNow() =>
        new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    /// <summary>Executes the set utc now operation.</summary>
    public void SetUtcNow(DateTimeOffset value) =>
        Interlocked.Exchange(ref _utcTicks, value.UtcTicks);

    /// <summary>Executes the advance operation.</summary>
    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        Interlocked.Add(ref _utcTicks, duration.Ticks);
    }
}
