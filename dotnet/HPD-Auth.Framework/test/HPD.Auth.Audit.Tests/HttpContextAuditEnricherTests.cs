using FluentAssertions;
using HPD.Auth.Core.Events;
using Xunit;

namespace HPD.Auth.Audit.Tests;

/// <summary>
/// AuthExecutionContext — IP/UserAgent enrichment via event AuthContext.
/// (HttpContextAuditEnricher was removed; enrichment is now done by populating
/// AuthContext when emitting events from endpoints.)
/// </summary>
public class HttpContextAuditEnricherTests
{
    [Fact]
    public void AuthExecutionContext_IpAddress_CanBeSet()
    {
        var ctx = new AuthExecutionContext { IpAddress = "203.0.113.42" };
        ctx.IpAddress.Should().Be("203.0.113.42");
    }

    [Fact]
    public void AuthExecutionContext_UserAgent_CanBeSet()
    {
        var ctx = new AuthExecutionContext { UserAgent = "Mozilla/5.0 (Test)" };
        ctx.UserAgent.Should().Be("Mozilla/5.0 (Test)");
    }

    [Fact]
    public void AuthExecutionContext_NullIpAndUserAgent_AreAllowed()
    {
        var ctx = new AuthExecutionContext();
        ctx.IpAddress.Should().BeNull();
        ctx.UserAgent.Should().BeNull();
    }

    [Fact]
    public void UserLoggedInEvent_AuthContext_IpAndUserAgent_ArePreserved()
    {
        var evt = new UserLoggedInEvent
        {
            UserId = Guid.NewGuid(),
            Email = "user@example.com",
            AuthContext = new AuthExecutionContext { IpAddress = "1.2.3.4", UserAgent = "TestAgent" },
        };

        evt.AuthContext!.IpAddress.Should().Be("1.2.3.4");
        evt.AuthContext.UserAgent.Should().Be("TestAgent");
    }

    [Fact]
    public void LoginFailedEvent_AuthContext_CarriesIpAddress()
    {
        var evt = new LoginFailedEvent
        {
            Email = "test@example.com",
            Reason = "invalid_password",
            AuthContext = new AuthExecutionContext { IpAddress = "9.9.9.9" },
        };

        evt.AuthContext!.IpAddress.Should().Be("9.9.9.9");
    }

    [Fact]
    public void AuthExecutionContext_IsImmutableRecord()
    {
        var ctx = new AuthExecutionContext { IpAddress = "1.1.1.1" };
        var ctx2 = ctx with { UserAgent = "Agent2" };

        ctx.UserAgent.Should().BeNull(); // original unchanged
        ctx2.IpAddress.Should().Be("1.1.1.1");
        ctx2.UserAgent.Should().Be("Agent2");
    }
}
