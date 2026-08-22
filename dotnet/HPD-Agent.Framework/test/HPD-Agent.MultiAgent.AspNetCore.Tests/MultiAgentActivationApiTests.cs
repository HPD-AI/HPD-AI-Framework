using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.MultiAgent.AspNetCore.Tests;

public sealed class MultiAgentActivationApiTests
{
    [Fact]
    public void Registration_requires_an_installed_graph_activation()
    {
        var services = new ServiceCollection();

        Action register = () => services.AddHPDMultiAgentAspNetCore();

        register.Should().Throw<ArgumentException>()
            .WithParameterName("graphs");
    }
}
