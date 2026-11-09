using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Middleware;

public sealed class AgentBuilderConfiguredMiddlewareTests
{
    [Fact]
    public async Task ConfiguredMiddlewares_ResolveAndPreserveOrder()
    {
        var config = new AgentConfig
        {
            Name = "configured-middleware",
            Middlewares =
            [
                "TestConfiguredFirst",
                "TestConfiguredSecond"
            ]
        };

        await using var agent = await new AgentBuilder(config)
            .WithChatClient(new FakeChatClient())
            .BuildAsync();

        var configured = agent.Middlewares
            .Where(static middleware => middleware is TestConfiguredFirst or TestConfiguredSecond)
            .Select(static middleware => middleware.GetType())
            .ToArray();

        Assert.Equal(
            [typeof(TestConfiguredFirst), typeof(TestConfiguredSecond)],
            configured);
    }

    [Fact]
    public async Task UnknownConfiguredMiddleware_FailsTheBuild()
    {
        var config = new AgentConfig
        {
            Name = "unknown-middleware",
            Middlewares = ["TestMiddlewareThatDoesNotExist"]
        };

        var exception = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            new AgentBuilder(config)
                .WithChatClient(new FakeChatClient())
                .BuildAsync());

        Assert.Equal("middleware_configuration_invalid", exception.Code);
        Assert.Equal("middlewares", exception.Path);
        var aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        var failure = Assert.IsType<AgentRunConfigurationException>(Assert.Single(aggregate.InnerExceptions));
        Assert.Equal("middleware_not_found", failure.Code);
        Assert.Equal("middlewares[0]", failure.Path);
    }

    [Fact]
    public async Task UnresolvableConfiguredMiddleware_FailsTheBuild()
    {
        var config = new AgentConfig
        {
            Name = "unresolved-middleware",
            Middlewares = ["TestDependencyOnly"]
        };

        var exception = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            new AgentBuilder(config)
                .WithChatClient(new FakeChatClient())
                .BuildAsync());

        Assert.Equal("middleware_configuration_invalid", exception.Code);
        var aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        var failure = Assert.IsType<AgentRunConfigurationException>(Assert.Single(aggregate.InnerExceptions));
        Assert.Equal("middleware_unresolved", failure.Code);
        Assert.Contains(nameof(TestDependencyOnly), failure.Message);
    }
}

[Middleware("TestConfiguredFirst")]
public sealed class TestConfiguredFirst : IAgentMiddleware;

[Middleware("TestConfiguredSecond")]
public sealed class TestConfiguredSecond : IAgentMiddleware;

[Middleware("TestDependencyOnly")]
public sealed class TestDependencyOnly : IAgentMiddleware
{
    public TestDependencyOnly(IServiceProvider services, string value)
    {
    }
}
