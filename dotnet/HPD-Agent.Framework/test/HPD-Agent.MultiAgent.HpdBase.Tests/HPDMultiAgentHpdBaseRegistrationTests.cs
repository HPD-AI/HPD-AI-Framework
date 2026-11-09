using HPD.Agent.MultiAgent.HpdBase;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.MultiAgent.HpdBase.Tests;

public sealed class HPDMultiAgentHpdBaseRegistrationTests
{
    [Fact]
    public void Registration_requires_an_installed_graph_activation()
    {
        var services = new ServiceCollection();

        Action register = () => services.AddHPDMultiAgentHpdBase();

        register.Should().Throw<ArgumentException>()
            .WithParameterName("graphs");
    }
}
