using FluentAssertions;
using HPD.Auth.Core.Entities;
using Xunit;

namespace HPD.Auth.Core.Tests.Entities;

[Trait("Category", "Entity")]
public class SSOProviderTests
{
    [Fact]
    public void SSOProvider_InstanceId_DefaultsToGuidEmpty()
    {
        new SSOProvider().InstanceId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void SSOProvider_IsEnabled_DefaultsToTrue()
    {
        new SSOProvider().IsEnabled.Should().BeTrue();
    }
}
