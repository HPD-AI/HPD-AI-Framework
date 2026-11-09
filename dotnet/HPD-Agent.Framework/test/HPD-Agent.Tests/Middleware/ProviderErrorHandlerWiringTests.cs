using HPD.Agent.Middleware.Function;
using HPD.Agent.Providers;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Middleware;

public sealed class ProviderErrorHandlerWiringTests
{
    [Fact]
    public async Task WithRetry_UsesSelectedProviderErrorHandler()
    {
        var config = new AgentConfig { Name = "provider-error-handler" };
        config.Clients.Chat = new ChatClientConfig
        {
            Provider = new ProviderReference { Key = "test" }
        };
        var builder = new AgentBuilder(config, new TestProviderRegistry())
            .WithChatClient(new FakeChatClient())
            .WithRetry();

        await using var agent = await builder.BuildAsync();

        var retry = Assert.Single(builder.Middlewares.OfType<RetryMiddleware>());
        Assert.IsType<TestErrorHandler>(retry.ProviderErrorHandler);
    }
}
