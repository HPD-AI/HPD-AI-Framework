namespace HPD.Agent.Authority;

/// <summary>Describes the ordering relation between two monotonic stamps.</summary>
public enum ClockComparison
{
    /// <summary>The left stamp precedes the right stamp in the same clock domain and boot.</summary>
    Earlier = -1,
    /// <summary>The stamps identify the same monotonic instant.</summary>
    Equal = 0,
    /// <summary>The left stamp follows the right stamp in the same clock domain and boot.</summary>
    Later = 1,
    /// <summary>The stamps belong to different clock domains or boots and cannot be ordered.</summary>
    Incomparable = 2,
}

/// <summary>Identifies a monotonic instant within one clock domain and process boot.</summary>
/// <remarks>This value never establishes journal order and cannot be compared across a domain or boot boundary.</remarks>
public readonly record struct MonotonicStampV1
{
    /// <summary>Initializes a validated monotonic stamp.</summary>
    /// <param name="clockDomainId">The owner-defined monotonic clock domain.</param>
    /// <param name="bootId">The process or host boot that anchors the counter.</param>
    /// <param name="nanoseconds">A nonnegative nanosecond count within the clock and boot.</param>
    /// <exception cref="ArgumentException">A clock-domain or boot identifier is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The nanosecond count is negative.</exception>
    public MonotonicStampV1(ClockDomainId clockDomainId, BootId bootId, long nanoseconds)
    {
        if (!clockDomainId.IsValid)
            throw new ArgumentException("A clock domain is required.", nameof(clockDomainId));
        if (!bootId.IsValid)
            throw new ArgumentException("A boot identifier is required.", nameof(bootId));
        if (nanoseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(nanoseconds), "A monotonic counter cannot be negative.");
        ClockDomainId = clockDomainId;
        BootId = bootId;
        Nanoseconds = nanoseconds;
    }

    /// <summary>Gets the monotonic clock domain.</summary>
    public ClockDomainId ClockDomainId { get; }

    /// <summary>Gets the boot that anchors the counter.</summary>
    public BootId BootId { get; }

    /// <summary>Gets the nonnegative monotonic nanosecond count.</summary>
    public long Nanoseconds { get; }

    /// <summary>Gets whether the stamp contains valid IDs and a nonnegative counter.</summary>
    public bool IsValid => ClockDomainId.IsValid && BootId.IsValid && Nanoseconds >= 0;

    /// <summary>Compares two stamps without inventing order across clock or boot boundaries.</summary>
    public ClockComparison CompareTo(MonotonicStampV1 other)
    {
        if (!IsValid || !other.IsValid || ClockDomainId != other.ClockDomainId || BootId != other.BootId)
            return ClockComparison.Incomparable;
        return Nanoseconds < other.Nanoseconds
            ? ClockComparison.Earlier
            : Nanoseconds > other.Nanoseconds ? ClockComparison.Later : ClockComparison.Equal;
    }

    /// <summary>Subtracts another stamp only when both values share a clock domain and boot.</summary>
    /// <param name="other">The stamp to subtract.</param>
    /// <param name="duration">The signed difference when the clocks are comparable.</param>
    /// <returns><see langword="true"/> when the stamps are comparable; otherwise <see langword="false"/>.</returns>
    public bool TrySubtract(MonotonicStampV1 other, out DurationNs duration)
    {
        if (CompareTo(other) == ClockComparison.Incomparable)
        {
            duration = default;
            return false;
        }
        duration = new DurationNs(Nanoseconds - other.Nanoseconds);
        return true;
    }
}
