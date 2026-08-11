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

/// <summary>Describes whether subtraction produced a signed duration.</summary>
public enum ClockSubtractionStatus
{
    /// <summary>The stamps were comparable and the signed duration was representable.</summary>
    Success = 0,
    /// <summary>The stamps belong to different clock domains or boots.</summary>
    Incomparable = 1,
    /// <summary>The stamps were comparable but their difference exceeded the signed duration range.</summary>
    OutOfRange = 2,
}

/// <summary>Identifies a monotonic instant within one clock domain and process boot.</summary>
/// <remarks>This value never establishes journal order and cannot be compared across a domain or boot boundary.</remarks>
public readonly record struct MonotonicStampV1
{
    /// <summary>Initializes a validated monotonic stamp.</summary>
    /// <param name="clockDomainId">The owner-defined monotonic clock domain.</param>
    /// <param name="bootId">The process or host boot that anchors the counter.</param>
    /// <param name="nanoseconds">An unsigned nanosecond count within the clock and boot.</param>
    /// <exception cref="ArgumentException">A clock-domain or boot identifier is invalid.</exception>
    public MonotonicStampV1(ClockDomainId clockDomainId, BootId bootId, ulong nanoseconds)
    {
        if (!clockDomainId.IsValid)
            throw new ArgumentException("A clock domain is required.", nameof(clockDomainId));
        if (!bootId.IsValid)
            throw new ArgumentException("A boot identifier is required.", nameof(bootId));
        ClockDomainId = clockDomainId;
        BootId = bootId;
        Nanoseconds = nanoseconds;
    }

    /// <summary>Gets the monotonic clock domain.</summary>
    public ClockDomainId ClockDomainId { get; }

    /// <summary>Gets the boot that anchors the counter.</summary>
    public BootId BootId { get; }

    /// <summary>Gets the unsigned monotonic nanosecond count.</summary>
    public ulong Nanoseconds { get; }

    /// <summary>Gets whether the stamp contains valid IDs and a nonnegative counter.</summary>
    public bool IsValid => ClockDomainId.IsValid && BootId.IsValid;

    /// <summary>Compares two stamps without inventing order across clock or boot boundaries.</summary>
    public ClockComparison CompareTo(MonotonicStampV1 other)
    {
        if (!IsValid || !other.IsValid || ClockDomainId != other.ClockDomainId || BootId != other.BootId)
            return ClockComparison.Incomparable;
        return Nanoseconds < other.Nanoseconds
            ? ClockComparison.Earlier
            : Nanoseconds > other.Nanoseconds ? ClockComparison.Later : ClockComparison.Equal;
    }

    /// <summary>Subtracts another stamp without conflating clock mismatch with signed-duration overflow.</summary>
    /// <param name="other">The stamp to subtract.</param>
    /// <param name="duration">The signed difference when the clocks are comparable.</param>
    /// <returns>The exact subtraction disposition.</returns>
    public ClockSubtractionStatus Subtract(MonotonicStampV1 other, out DurationNs duration)
    {
        if (CompareTo(other) == ClockComparison.Incomparable)
        {
            duration = default;
            return ClockSubtractionStatus.Incomparable;
        }

        if (Nanoseconds >= other.Nanoseconds)
        {
            var magnitude = Nanoseconds - other.Nanoseconds;
            if (magnitude > long.MaxValue)
            {
                duration = default;
                return ClockSubtractionStatus.OutOfRange;
            }
            duration = new DurationNs((long)magnitude);
            return ClockSubtractionStatus.Success;
        }

        var negativeMagnitude = other.Nanoseconds - Nanoseconds;
        if (negativeMagnitude > 1UL << 63)
        {
            duration = default;
            return ClockSubtractionStatus.OutOfRange;
        }
        duration = new DurationNs(negativeMagnitude == 1UL << 63 ? long.MinValue : -(long)negativeMagnitude);
        return ClockSubtractionStatus.Success;
    }
}
