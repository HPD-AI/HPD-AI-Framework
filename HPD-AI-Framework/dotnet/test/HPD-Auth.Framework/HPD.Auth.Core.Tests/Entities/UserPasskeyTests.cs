using FluentAssertions;
using HPD.Auth.Core.Entities;
using Xunit;

namespace HPD.Auth.Core.Tests.Entities;

[Trait("Category", "Entity")]
public class UserPasskeyTests
{
    [Fact]
    public void UserPasskey_InstanceId_DefaultsToGuidEmpty()
    {
        new UserPasskey().InstanceId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void UserPasskey_SignatureCounter_DefaultsToZero()
    {
        new UserPasskey().SignatureCounter.Should().Be(0u);
    }

    [Fact]
    public void UserPasskey_UserVerified_DefaultsToFalse()
    {
        new UserPasskey().UserVerified.Should().BeFalse();
    }

    [Fact]
    public void UserPasskey_IsDiscoverable_DefaultsToFalse()
    {
        new UserPasskey().IsDiscoverable.Should().BeFalse();
    }

    [Fact]
    public void UserPasskey_LastUsedAt_DefaultsToNull()
    {
        new UserPasskey().LastUsedAt.Should().BeNull();
    }
}
