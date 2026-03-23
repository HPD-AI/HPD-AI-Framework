using FluentAssertions;
using HPD.Auth.Core.Options;
using Xunit;

namespace HPD.Auth.Core.Tests.Options;

[Trait("Category", "Options")]
public class OAuthOptionsTests
{
    [Fact]
    public void OAuthOptions_AutoProvisionUsers_DefaultsToTrue()
    {
        new OAuthOptions().AutoProvisionUsers.Should().BeTrue();
    }

    [Fact]
    public void OAuthOptions_AutoLinkAccounts_DefaultsToTrue()
    {
        new OAuthOptions().AutoLinkAccounts.Should().BeTrue();
    }

    [Fact]
    public void OAuthOptions_StoreRawProfileData_DefaultsToTrue()
    {
        new OAuthOptions().StoreRawProfileData.Should().BeTrue();
    }

    [Fact]
    public void OAuthOptions_Providers_DefaultsToEmptyDictionary()
    {
        var opts = new OAuthOptions();
        opts.Providers.Should().NotBeNull();
        opts.Providers.Should().BeEmpty();
    }
}

[Trait("Category", "Options")]
public class OAuthProviderOptionsTests
{
    [Fact]
    public void OAuthProviderOptions_Enabled_DefaultsToTrue()
    {
        new OAuthProviderOptions().Enabled.Should().BeTrue();
    }

    [Fact]
    public void OAuthProviderOptions_AdditionalScopes_DefaultsToEmptyList()
    {
        var opts = new OAuthProviderOptions();
        opts.AdditionalScopes.Should().NotBeNull();
        opts.AdditionalScopes.Should().BeEmpty();
    }

    [Fact]
    public void OAuthProviderOptions_CallbackPath_DefaultsToNull()
    {
        new OAuthProviderOptions().CallbackPath.Should().BeNull();
    }

    [Fact]
    public void OAuthProviderOptions_ClientId_DefaultsToEmpty()
    {
        new OAuthProviderOptions().ClientId.Should().Be(string.Empty);
    }

    [Fact]
    public void OAuthProviderOptions_ClientSecret_DefaultsToEmpty()
    {
        new OAuthProviderOptions().ClientSecret.Should().Be(string.Empty);
    }
}
