using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using System.Text.Json;
using Xunit;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderOptionsRunConfigTests : AgentTestBase
{
    [Fact]
    public async Task RunConfigProviderOptions_IsPassedToRuntimeProviderConfig()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("ok");
        var provider = new CapturingChatClientProvider(fakeClient);
        var registry = new CapturingProviderRegistry(provider);
        var config = DefaultConfig();
        config.Clients!.Chat!.ProviderOptions = JsonDocument.Parse("""{"base":true}""").RootElement.Clone();

        var agent = await new AgentBuilder(config, registry)
            .WithCircuitBreaker(5)
            .WithErrorTracking(maxConsecutiveErrors: 3)
            .BuildAsync(TestCancellationToken);

        await agent.RunAsync(
            "hello",
            runConfig: new AgentRunConfig
            {
                ProviderKey = "test",
                ModelId = "run-model",
                ProviderOptions = JsonDocument.Parse("""{"run":true}""").RootElement.Clone()
            },
            cancellationToken: TestCancellationToken);

        Assert.Equal("""{"base":true,"run":true}""", provider.CreatedConfigs.Last().GetProviderOptionsRawJson());
        Assert.Equal("run-model", provider.CreatedConfigs.Last().ModelName);
    }

    [Fact]
    public async Task RunConfigProviderOptions_InheritsBaseOptionsForSameProvider()
    {
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("ok");
        var provider = new CapturingChatClientProvider(fakeClient);
        var registry = new CapturingProviderRegistry(provider);
        var config = DefaultConfig();
        config.Clients!.Chat!.ProviderOptions = JsonDocument.Parse("""{"base":true}""").RootElement.Clone();

        var agent = await new AgentBuilder(config, registry)
            .WithCircuitBreaker(5)
            .WithErrorTracking(maxConsecutiveErrors: 3)
            .BuildAsync(TestCancellationToken);

        await agent.RunAsync(
            "hello",
            runConfig: new AgentRunConfig
            {
                ProviderKey = "test",
                ModelId = "run-model"
            },
            cancellationToken: TestCancellationToken);

        Assert.Equal("""{"base":true}""", provider.CreatedConfigs.Last().GetProviderOptionsRawJson());
        Assert.Equal("run-model", provider.CreatedConfigs.Last().ModelName);
    }

    private sealed class CapturingProviderRegistry(CapturingChatClientProvider provider) : IProviderRegistry
    {
        public void Register(IProvider features)
        {
        }

        public IProvider? GetProvider(string providerKey)
            => string.Equals(providerKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase)
                ? provider
                : null;

        public TProvider? GetProvider<TProvider>(string providerKey)
            where TProvider : class, IProvider
            => GetProvider(providerKey) as TProvider;

        public bool IsRegistered(string providerKey)
            => string.Equals(providerKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> GetRegisteredProviders()
            => [provider.ProviderKey];

        public void Clear()
        {
        }
    }

    private sealed class CapturingChatClientProvider(IChatClient chatClient) : IChatClientProvider
    {
        public List<ClientProviderConfig> CreatedConfigs { get; } = [];

        public string ProviderKey => "test";

        public string DisplayName => "Test Provider";

        public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
        {
            CreatedConfigs.Add(config);
            return chatClient;
        }

        public IProviderErrorHandler CreateErrorHandler()
            => new PassthroughErrorHandler();

        public ProviderMetadata GetMetadata()
            => new()
            {
                ProviderKey = ProviderKey,
                DisplayName = DisplayName,
                Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
                {
                    [ProviderClientFamily.Chat] = new()
                    {
                        Family = ProviderClientFamily.Chat,
                        Capabilities = new Dictionary<string, object?>
                        {
                            ["SupportsStreaming"] = true,
                            ["SupportsFunctionCalling"] = true
                        }
                    }
                }
            };

        public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class PassthroughErrorHandler : IProviderErrorHandler
    {
        public ProviderErrorDetails? ParseError(Exception exception)
            => null;

        public TimeSpan? GetRetryDelay(
            ProviderErrorDetails details,
            int attempt,
            TimeSpan initialDelay,
            double multiplier,
            TimeSpan maxDelay)
            => null;

        public bool RequiresSpecialHandling(ProviderErrorDetails details)
            => false;
    }
}
