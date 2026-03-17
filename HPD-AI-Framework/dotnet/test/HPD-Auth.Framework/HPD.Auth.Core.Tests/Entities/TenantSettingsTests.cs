using FluentAssertions;
using HPD.Auth.Core.Entities;
using Xunit;

namespace HPD.Auth.Core.Tests.Entities;

[Trait("Category", "Entity")]
public class TenantSettingsTests
{
    [Fact]
    public void TenantSettings_InstanceId_DefaultsToGuidEmpty()
    {
        new TenantSettings().InstanceId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TenantSettings_Settings_DefaultsToEmptyJsonObject()
    {
        new TenantSettings().Settings.Should().Be("{}");
    }

    [Fact]
    public void TenantSettings_DisplayName_DefaultsToNull()
    {
        new TenantSettings().DisplayName.Should().BeNull();
    }
}
