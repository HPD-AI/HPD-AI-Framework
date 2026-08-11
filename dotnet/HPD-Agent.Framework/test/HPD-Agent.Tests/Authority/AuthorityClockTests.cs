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
        Assert.True(later.TrySubtract(earlier, out var duration));
        Assert.Equal(15, duration.Nanoseconds);
    }

    [Fact]
    public void DifferentClockDomain_IsIncomparable()
    {
        var boot = BootId.Create();
        var left = new MonotonicStampV1(ClockDomainId.Create(), boot, 10);
        var right = new MonotonicStampV1(ClockDomainId.Create(), boot, 20);

        Assert.Equal(ClockComparison.Incomparable, left.CompareTo(right));
        Assert.False(left.TrySubtract(right, out _));
    }

    [Fact]
    public void DifferentBoot_IsIncomparable()
    {
        var domain = ClockDomainId.Create();
        var left = new MonotonicStampV1(domain, BootId.Create(), 10);
        var right = new MonotonicStampV1(domain, BootId.Create(), 20);

        Assert.Equal(ClockComparison.Incomparable, left.CompareTo(right));
        Assert.False(left.TrySubtract(right, out _));
    }

    [Fact]
    public void ConstructorAndDefault_RejectInvalidBoundaryValues()
    {
        var domain = ClockDomainId.Create();
        var boot = BootId.Create();

        Assert.Throws<ArgumentException>(() => new MonotonicStampV1(default, boot, 0));
        Assert.Throws<ArgumentException>(() => new MonotonicStampV1(domain, default, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonotonicStampV1(domain, boot, -1));
        Assert.False(default(MonotonicStampV1).IsValid);
        Assert.Equal(ClockComparison.Incomparable, default(MonotonicStampV1).CompareTo(new(domain, boot, 0)));
    }
}
