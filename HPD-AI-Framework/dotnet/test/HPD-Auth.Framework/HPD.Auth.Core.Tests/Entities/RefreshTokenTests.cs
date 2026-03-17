using FluentAssertions;
using HPD.Auth.Core.Entities;
using Xunit;

namespace HPD.Auth.Core.Tests.Entities;

[Trait("Category", "Entity")]
public class RefreshTokenTests
{
    [Fact]
    public void RefreshToken_InstanceId_DefaultsToGuidEmpty()
    {
        new RefreshToken().InstanceId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void RefreshToken_IsUsed_DefaultsToFalse()
    {
        new RefreshToken().IsUsed.Should().BeFalse();
    }

    [Fact]
    public void RefreshToken_IsRevoked_DefaultsToFalse()
    {
        new RefreshToken().IsRevoked.Should().BeFalse();
    }

    [Fact]
    public void RefreshToken_RevokedAt_DefaultsToNull()
    {
        new RefreshToken().RevokedAt.Should().BeNull();
    }

    [Fact]
    public void RefreshToken_CreatedAt_IsSetToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var token = new RefreshToken();
        var after = DateTime.UtcNow.AddSeconds(5);

        token.CreatedAt.Should().BeAfter(before).And.BeBefore(after);
    }
}
