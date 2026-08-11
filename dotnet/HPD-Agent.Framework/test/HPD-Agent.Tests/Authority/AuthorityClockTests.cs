using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityClockTests
{
    [Fact]
    public void SameClockAndBoot_CompareAndSubtractExactly()
    {
        var domain = ClockDomainId.Create();
        var boot = BootId.Create();
        var earlier = new MonotonicStampV1(domain, boot, 10);
        var later = new MonotonicStampV1(domain, boot, 25);

        Assert.Equal(ClockComparison.Earlier, earlier.CompareTo(later));
        Assert.Equal(ClockComparison.Later, later.CompareTo(earlier));
        Assert.Equal(ClockComparison.Equal, earlier.CompareTo(earlier));
        Assert.Equal(ClockSubtractionStatus.Success, later.Subtract(earlier, out var duration));
        Assert.Equal(15, duration.Nanoseconds);
    }

    [Fact]
    public void DifferentClockDomain_IsIncomparable()
    {
        var boot = BootId.Create();
        var left = new MonotonicStampV1(ClockDomainId.Create(), boot, 10);
        var right = new MonotonicStampV1(ClockDomainId.Create(), boot, 20);

        Assert.Equal(ClockComparison.Incomparable, left.CompareTo(right));
        Assert.Equal(ClockSubtractionStatus.Incomparable, left.Subtract(right, out _));
    }

    [Fact]
    public void DifferentBoot_IsIncomparable()
    {
        var domain = ClockDomainId.Create();
        var left = new MonotonicStampV1(domain, BootId.Create(), 10);
        var right = new MonotonicStampV1(domain, BootId.Create(), 20);

        Assert.Equal(ClockComparison.Incomparable, left.CompareTo(right));
        Assert.Equal(ClockSubtractionStatus.Incomparable, left.Subtract(right, out _));
    }

    [Fact]
    public void ConstructorAndDefault_RejectInvalidBoundaryValues()
    {
        var domain = ClockDomainId.Create();
        var boot = BootId.Create();

        Assert.Throws<ArgumentException>(() => new MonotonicStampV1(default, boot, 0));
        Assert.Throws<ArgumentException>(() => new MonotonicStampV1(domain, default, 0));
        Assert.False(default(MonotonicStampV1).IsValid);
        Assert.Equal(ClockComparison.Incomparable, default(MonotonicStampV1).CompareTo(new(domain, boot, 0)));
    }

    [Fact]
    public void FullUnsignedCounterRange_IsRepresented()
    {
        var stamp = new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), ulong.MaxValue);

        Assert.True(stamp.IsValid);
        Assert.Equal(ulong.MaxValue, stamp.Nanoseconds);
    }

    [Fact]
    public void SignedDurationBoundaries_AreExactInBothDirections()
    {
        var domain = ClockDomainId.Create();
        var boot = BootId.Create();
        var zero = new MonotonicStampV1(domain, boot, 0);
        var positiveMax = new MonotonicStampV1(domain, boot, (ulong)long.MaxValue);
        var negativeMin = new MonotonicStampV1(domain, boot, 1UL << 63);
        var beyond = new MonotonicStampV1(domain, boot, (1UL << 63) + 1);

        Assert.Equal(ClockSubtractionStatus.Success, positiveMax.Subtract(zero, out var max));
        Assert.Equal(long.MaxValue, max.Nanoseconds);
        Assert.Equal(ClockSubtractionStatus.Success, zero.Subtract(negativeMin, out var min));
        Assert.Equal(long.MinValue, min.Nanoseconds);
        Assert.Equal(ClockSubtractionStatus.OutOfRange, negativeMin.Subtract(zero, out _));
        Assert.Equal(ClockSubtractionStatus.OutOfRange, beyond.Subtract(zero, out _));
        Assert.Equal(ClockSubtractionStatus.OutOfRange, zero.Subtract(beyond, out _));
    }
}
