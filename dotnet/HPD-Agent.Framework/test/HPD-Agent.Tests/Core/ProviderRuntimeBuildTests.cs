using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderRuntimeBuildTests
{
    [Fact]
    public async Task BuildAsync_WithConfiguredChatDefault_DoesNotCreateClientOrResolveSecret()
    {
        var provider = new BuildTrackingProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        var secrets = new CountingSecretResolver();
        var config = new AgentConfig
        {
            Clients = new AgentClientsConfig
            {
                Chat = new ProviderClientConfig { ProviderKey = "build-tracking", ModelName = "model" }
            }
        };

        var agent = await new AgentBuilder(config, registry)
            .WithSecretResolver(secrets)
            .BuildAsync();

        Assert.Equal(0, provider.CreateCount);
        Assert.Equal(0, secrets.ResolveCount);
        agent.Dispose();
    }

    [Fact]
    public async Task BuildAsync_WithoutProviderOrModel_Succeeds()
    {
        var agent = await new AgentBuilder(new AgentConfig(), new ProviderRegistry()).BuildAsync();
        Assert.NotNull(agent);
        agent.Dispose();
    }

    private sealed class CountingSecretResolver : ISecretResolver
    {
        public int ResolveCount { get; private set; }

        public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            return ValueTask.FromResult<ResolvedSecret?>(null);
        }
    }

    private sealed class BuildTrackingProvider : IChatClientProvider
    {
        public string ProviderKey => "build-tracking";
        public string DisplayName => "Build Tracking";
        public int CreateCount { get; private set; }

        public ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return ValueTask.FromResult<IChatClient>(new FakeChatClient());
        }

        public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();
        public ProviderMetadata GetMetadata() => new() { ProviderKey = ProviderKey, DisplayName = DisplayName };
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }
}
