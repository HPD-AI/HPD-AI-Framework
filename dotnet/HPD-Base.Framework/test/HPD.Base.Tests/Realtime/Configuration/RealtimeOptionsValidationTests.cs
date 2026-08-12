using HPD.Base;

namespace HPD.Base.Tests.Realtime.Configuration;

public sealed class RealtimeOptionsValidationTests
{
    public static TheoryData<HPD.Events.AsyncStreamBackpressureMode> UnsupportedBackpressureModes => new()
    {
        HPD.Events.AsyncStreamBackpressureMode.Unspecified,
        HPD.Events.AsyncStreamBackpressureMode.Wait,
        HPD.Events.AsyncStreamBackpressureMode.LatestOnly
    };

    public static TheoryData<Func<BaseRealtimeLimits, BaseRealtimeLimits>> NonPositiveLimits => new()
    {
        limits => limits with { MaxConnections = 0 },
        limits => limits with { MaxChannelsPerConnection = 0 },
        limits => limits with { StreamCapacity = 0 },
        limits => limits with { OutboundCapacity = 0 },
        limits => limits with { MaxMessageBytes = 0 },
        limits => limits with { ReceiveIdleTimeoutSeconds = 0 },
        limits => limits with { SendTimeoutSeconds = 0 },
        limits => limits with { MaxJoinsPerSecond = 0 }
    };

    [Theory]
    [MemberData(nameof(NonPositiveLimits))]
    public void RegistrationRejectsNonPositiveLimits(
        Func<BaseRealtimeLimits, BaseRealtimeLimits> mutate)
    {
        var services = new ServiceCollection();

        var register = () => services.AddHPDBaseRealtime(
            options => options.Limits = mutate(options.Limits));

        register.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RegistrationRejectsPayloadLimitTooSmallForProtocolResponses()
    {
        var services = new ServiceCollection();

        var register = () => services.AddHPDBaseRealtime(
            options => options.Limits = options.Limits with { MaxPayloadBytes = 255 });

        register.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("MaxPayloadBytes");
    }

    [Theory]
    [MemberData(nameof(UnsupportedBackpressureModes))]
    public void RegistrationRejectsBlockingOrMisleadingBackpressureModes(
        HPD.Events.AsyncStreamBackpressureMode mode)
    {
        var services = new ServiceCollection();

        var register = () => services.AddHPDBaseRealtime(
            options => options.Backpressure = mode);

        register.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("Backpressure");
    }

    [Theory]
    [InlineData(HPD.Events.AsyncStreamBackpressureMode.DropOldest)]
    [InlineData(HPD.Events.AsyncStreamBackpressureMode.DropNewest)]
    [InlineData(HPD.Events.AsyncStreamBackpressureMode.DropWrite)]
    public void RegistrationAcceptsNonBlockingBackpressureModes(
        HPD.Events.AsyncStreamBackpressureMode mode)
    {
        var services = new ServiceCollection();

        services.AddHPDBaseRealtime(options => options.Backpressure = mode);
    }

    [Fact]
    public void DefaultLimitsDescribeTheEnforcedL24Controls()
    {
        var limits = new BaseRealtimeLimits();

        limits.OutboundCapacity.Should().Be(32);
        limits.ReceiveIdleTimeoutSeconds.Should().Be(90);
        limits.SendTimeoutSeconds.Should().Be(10);
        new BaseRealtimeOptions().Backpressure.Should()
            .Be(HPD.Events.AsyncStreamBackpressureMode.DropOldest);
        typeof(BaseRealtimeLimits).GetProperty("HeartbeatIntervalSeconds").Should().BeNull();
        typeof(BaseRealtimeLimits).GetProperty("HeartbeatTimeoutSeconds").Should().BeNull();
        typeof(BaseRealtimeLimits).GetProperty("MaxEventsPerSecond").Should().BeNull();
    }
}
