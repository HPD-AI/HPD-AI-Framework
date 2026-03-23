using FluentAssertions;
using HPD.Auth.Core.Events;
using Xunit;

namespace HPD.Auth.Core.Tests.Events;

[Trait("Category", "Events")]
public class AuthEventBaseTests
{
    private static UserRegisteredEvent MakeRegisteredEvent() =>
        new() { UserId = Guid.NewGuid(), Email = "test@example.com" };

    [Fact]
    public void AuthEvent_Timestamp_IsAutoPopulatedOnConstruction()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var evt = MakeRegisteredEvent();
        var after = DateTimeOffset.UtcNow.AddSeconds(5);

        evt.Timestamp.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void AuthEvent_TwoInstances_AreDistinct()
    {
        var a = MakeRegisteredEvent();
        var b = MakeRegisteredEvent();
        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void AuthEvent_AuthContext_DefaultsToNull()
    {
        MakeRegisteredEvent().AuthContext.Should().BeNull();
    }

    [Fact]
    public void AuthEvent_ExtendsHPDEventsEvent()
    {
        typeof(AuthEvent).IsAssignableTo(typeof(HPD.Events.Event)).Should().BeTrue();
    }

    [Fact]
    public void AuthExecutionContext_InstanceId_DefaultsToGuidEmpty()
    {
        new AuthExecutionContext().InstanceId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AuthExecutionContext_CanSetIpAndUserAgent()
    {
        var ctx = new AuthExecutionContext { IpAddress = "1.2.3.4", UserAgent = "TestAgent" };
        ctx.IpAddress.Should().Be("1.2.3.4");
        ctx.UserAgent.Should().Be("TestAgent");
    }
}
