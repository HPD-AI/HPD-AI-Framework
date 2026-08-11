namespace HPD.Agent.Authority;

using System.Formats.Cbor;

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

internal static class MonotonicStampV1Codec
{
    internal static byte[] Encode(MonotonicStampV1 value)
    {
        if (!value.IsValid)
            throw new ArgumentException("The monotonic stamp is invalid.", nameof(value));

        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        Span<byte> clockDomain = stackalloc byte[16];
        Span<byte> boot = stackalloc byte[16];
        if (!value.ClockDomainId.TryWriteBytes(clockDomain) || !value.BootId.TryWriteBytes(boot))
            throw new ArgumentException("The monotonic stamp is invalid.", nameof(value));

        writer.WriteStartMap(3);
        writer.WriteUInt64(1);
        writer.WriteByteString(clockDomain);
        writer.WriteUInt64(2);
        writer.WriteByteString(boot);
        writer.WriteUInt64(3);
        writer.WriteUInt64(value.Nanoseconds);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out MonotonicStampV1 value)
    {
        value = default;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, allowMultipleRootLevelValues: false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1)
                return false;
            var clockDomain = reader.ReadByteString();
            if (clockDomain.Length != 16 || reader.ReadUInt64() != 2)
                return false;
            var boot = reader.ReadByteString();
            if (boot.Length != 16 || reader.ReadUInt64() != 3)
                return false;
            var nanoseconds = reader.ReadUInt64();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0)
                return false;

            value = new MonotonicStampV1(
                ClockDomainId.FromValue(StableId128.FromBytes(clockDomain)),
                BootId.FromValue(StableId128.FromBytes(boot)),
                nanoseconds);
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException)
        {
            value = default;
            return false;
        }
    }
}
