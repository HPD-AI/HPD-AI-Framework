using FluentAssertions;
using HPD.Auth.Core.Entities;
using Xunit;

namespace HPD.Auth.Core.Tests.Entities;

[Trait("Category", "Entity")]
public class UserIdentityTests
{
    [Fact]
    public void UserIdentity_InstanceId_DefaultsToGuidEmpty()
    {
        new UserIdentity().InstanceId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void UserIdentity_IdentityData_DefaultsToEmptyJsonObject()
    {
        new UserIdentity().IdentityData.Should().Be("{}");
    }

    [Fact]
    public void UserIdentity_LastSignInAt_IsSetToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var identity = new UserIdentity();
        var after = DateTime.UtcNow.AddSeconds(5);

        identity.LastSignInAt.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void UserIdentity_FederationSourceId_DefaultsToNull()
    {
        new UserIdentity().FederationSourceId.Should().BeNull();
    }

    [Fact]
    public void UserIdentity_ProviderTokens_DefaultsToNull()
    {
        new UserIdentity().ProviderTokens.Should().BeNull();
    }
}
