using FluentAssertions;
using HPD.Agent.Bots.Teams;

namespace HPD.Agent.Bots.Tests.Unit.TeamsBot;

public class TeamsBotConfigTests
{
    [Fact]
    public void Validate_AppPasswordAuth_Succeeds()
    {
        var config = new TeamsBotConfig
        {
            AppId = " app-id ",
            AppPassword = " secret "
        };

        config.Validate();

        config.AppId.Should().Be("app-id");
        config.AppPassword.Should().Be("secret");
    }

    [Fact]
    public void Validate_NoAuthMethod_Throws()
    {
        var config = new TeamsBotConfig { AppId = "app-id" };

        var act = config.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Exactly one Teams authentication method*");
    }

    [Fact]
    public void Validate_MultipleAuthMethods_Throws()
    {
        var config = new TeamsBotConfig
        {
            AppId = "app-id",
            AppPassword = "secret",
            Federated = new TeamsAuthFederated { ClientId = "client-id" }
        };

        var act = config.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Exactly one Teams authentication method*");
    }

    [Fact]
    public void Validate_SingleTenantWithoutTenantId_Throws()
    {
        var config = new TeamsBotConfig
        {
            AppId = "app-id",
            AppPassword = "secret",
            AppType = "SingleTenant"
        };

        var act = config.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AppTenantId is required*");
    }

    [Fact]
    public void Validate_CertificateRequiresPrivateKeyAndThumbprintOrX5c()
    {
        var config = new TeamsBotConfig
        {
            AppId = "app-id",
            Certificate = new TeamsAuthCertificate
            {
                CertificatePrivateKey = "private-key"
            }
        };

        var act = config.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CertificateThumbprint or X5c*");
    }
}
