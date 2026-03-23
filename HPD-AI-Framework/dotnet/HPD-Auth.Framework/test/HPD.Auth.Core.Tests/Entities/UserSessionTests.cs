using FluentAssertions;
using HPD.Auth.Core.Entities;
using Xunit;

namespace HPD.Auth.Core.Tests.Entities;

[Trait("Category", "Entity")]
public class UserSessionTests
{
    [Fact]
    public void UserSession_InstanceId_DefaultsToGuidEmpty()
    {
        new UserSession().InstanceId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void UserSession_AAL_DefaultsToAal1()
    {
        new UserSession().AAL.Should().Be("aal1");
    }

    [Fact]
    public void UserSession_IsRevoked_DefaultsToFalse()
    {
        new UserSession().IsRevoked.Should().BeFalse();
    }

    [Fact]
    public void UserSession_SessionState_DefaultsToActive()
    {
        new UserSession().SessionState.Should().Be("active");
    }

    [Fact]
    public void UserSession_CreatedAt_IsSetToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var session = new UserSession();
        var after = DateTime.UtcNow.AddSeconds(5);

        session.CreatedAt.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void UserSession_LastActiveAt_IsSetToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var session = new UserSession();
        var after = DateTime.UtcNow.AddSeconds(5);

        session.LastActiveAt.Should().BeAfter(before).And.BeBefore(after);
    }
}
