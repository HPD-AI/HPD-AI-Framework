using System.Runtime.CompilerServices;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

public sealed class AgentChatClientResolverTests
{
    [Fact]
    public async Task ResolveAsync_AgentDefault_BeatsInheritedFallback()
    {
        var agentClient = new FakeChatClient();
        var inheritedClient = new FakeChatClient();
        var resolver = new AgentChatClientResolver(null, null);

        await using var lease = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            AgentDefault = AgentChatClientHandle.Borrowed(agentClient, AgentChatClientSource.AgentDefault),
            InheritedFallback = AgentChatClientHandle.Borrowed(inheritedClient, AgentChatClientSource.InheritedFallback)
        });

        Assert.Same(agentClient, lease.Client);
    }

    [Fact]
    public async Task ResolveAsync_Override_BeatsAgentDefault()
    {
        var overrideClient = new FakeChatClient();
        var resolver = new AgentChatClientResolver(null, null);

        await using var lease = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = new AgentRunConfig { OverrideChatClient = overrideClient },
            AgentDefault = AgentChatClientHandle.Borrowed(new FakeChatClient(), AgentChatClientSource.AgentDefault)
        });

        Assert.Same(overrideClient, lease.Client);
        Assert.Equal(AgentChatClientSource.InjectedOverride, lease.Handle.Source);
    }

    [Fact]
    public async Task ResolveAsync_RuntimeProvider_DisposesAfterFinalLease()
    {
        var client = new TrackingChatClient();
        var registry = new ProviderRegistry();
        registry.Register(new TrackingProvider(client));
        var resolver = new AgentChatClientResolver(registry, null);

        var lease = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = new AgentRunConfig { ProviderKey = "tracking", ModelId = "model" }
        });
        var childLease = lease.Handle.AcquireLease();

        await lease.DisposeAsync();
        Assert.Equal(0, client.DisposeCount);

        await childLease.DisposeAsync();
        Assert.Equal(1, client.DisposeCount);
    }

    private sealed class TrackingChatClient : IChatClient
    {
        public int DisposeCount { get; private set; }
        public ChatClientMetadata Metadata { get; } = new("tracking", null, "model");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => DisposeCount++;
    }

    private sealed class TrackingProvider(TrackingChatClient client) : IChatClientProvider
    {
        public string ProviderKey => "tracking";
        public string DisplayName => "Tracking";
        public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null) => client;
        public IProviderErrorHandler CreateErrorHandler() => new NoopErrorHandler();
        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>()
        };
        public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class NoopErrorHandler : IProviderErrorHandler
    {
        public ProviderErrorDetails? ParseError(Exception exception) => null;
        public TimeSpan? GetRetryDelay(ProviderErrorDetails details, int attempt, TimeSpan initialDelay, double multiplier, TimeSpan maxDelay) => null;
        public bool RequiresSpecialHandling(ProviderErrorDetails details) => false;
    }
}
