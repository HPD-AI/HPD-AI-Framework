using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable MEAI001

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
                Chat = new ChatClientConfig { ProviderKey = "build-tracking", ModelName = "model" }
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

    [Fact]
    public async Task BuildAsync_WithConfiguredAuxiliaryDefault_DoesNotCreateClient()
    {
        var provider = new BuildTrackingTextToSpeechProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        var config = new AgentConfig
        {
            Clients = new AgentClientsConfig
            {
                TextToSpeech = new TextToSpeechClientConfig
                {
                    ProviderKey = provider.ProviderKey,
                    ModelName = "speech-model"
                }
            }
        };

        var agent = await new AgentBuilder(config, registry).BuildAsync();

        Assert.Equal(0, provider.CreateCount);
        agent.Dispose();
    }

    [Fact]
    public async Task Runs_WithConfiguredAuxiliaryDefault_ReuseManagedClientUntilAgentDisposal()
    {
        var provider = new BuildTrackingTextToSpeechProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        var chat = new FakeChatClient();
        chat.EnqueueTextResponse("first");
        chat.EnqueueTextResponse("second");
        var agent = await new AgentBuilder(new AgentConfig
        {
            Clients = new AgentClientsConfig
            {
                TextToSpeech = new TextToSpeechClientConfig
                {
                    ProviderKey = provider.ProviderKey,
                    ModelName = "speech-model"
                }
            }
        }, registry).WithChatClient(chat).BuildAsync();

        await agent.RunAsync("one");
        await agent.RunAsync("two");

        Assert.Equal(1, provider.CreateCount);
        Assert.Equal(0, provider.Clients.Single().DisposeCount);
        agent.Dispose();
        Assert.Equal(1, provider.Clients.Single().DisposeCount);
    }

    [Fact]
    public async Task Runs_WithExplicitAuxiliaryApiKey_DoNotReuseOrLeakOwnedClients()
    {
        var provider = new BuildTrackingTextToSpeechProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        var chat = new FakeChatClient();
        chat.EnqueueTextResponse("first");
        chat.EnqueueTextResponse("second");
        var agent = await new AgentBuilder(new AgentConfig(), registry)
            .WithChatClient(chat)
            .BuildAsync();
        AgentRunConfig Run(string key) => new()
        {
            Clients = new AgentClientsConfig
            {
                TextToSpeech = new TextToSpeechClientConfig
                {
                    ProviderKey = provider.ProviderKey,
                    ModelName = "speech-model",
                    ApiKey = key
                }
            }
        };

        await agent.RunAsync("one", runConfig: Run("first-key"));
        await agent.RunAsync("two", runConfig: Run("second-key"));

        Assert.Equal(2, provider.CreateCount);
        Assert.Equal(["first-key", "second-key"], provider.ApiKeys);
        Assert.All(provider.Clients, client => Assert.Equal(1, client.DisposeCount));
        agent.Dispose();
        Assert.All(provider.Clients, client => Assert.Equal(1, client.DisposeCount));
    }

    [Fact]
    public async Task Runs_WithNamedAuxiliaryAuthentication_ResolveLocallyAndReuseByIdentity()
    {
        var provider = new BuildTrackingTextToSpeechProvider();
        var providers = new ProviderRegistry();
        providers.Register(provider);
        var registrations = new InMemoryProviderAuthenticationRegistry();
        registrations.Register(new ProviderAuthenticationRegistration
        {
            Key = "speech-work",
            ProviderKey = provider.ProviderKey,
            SecretKey = "speech:work",
            Families = new HashSet<ProviderClientFamily> { ProviderClientFamily.TextToSpeech }
        });
        var secrets = new ExplicitSecretResolver();
        secrets.Set("speech:work", "resolved-secret");
        var services = new ServiceCollection()
            .AddSingleton<IProviderAuthenticationRegistry>(registrations)
            .AddSingleton<IProviderCredentialResolver>(new SecretResolverProviderCredentialResolver(secrets))
            .BuildServiceProvider();
        var chat = new FakeChatClient();
        chat.EnqueueTextResponse("first");
        chat.EnqueueTextResponse("second");
        var agent = await new AgentBuilder(new AgentConfig
        {
            Clients = new AgentClientsConfig
            {
                TextToSpeech = new TextToSpeechClientConfig
                {
                    ProviderKey = provider.ProviderKey,
                    ModelName = "speech-model",
                    AuthenticationKey = "speech-work"
                }
            }
        }, providers)
            .WithServiceProvider(services)
            .WithChatClient(chat)
            .BuildAsync();

        await agent.RunAsync("one");
        await agent.RunAsync("two");

        Assert.Equal(1, provider.CreateCount);
        Assert.Equal("resolved-secret", Assert.Single(provider.ApiKeys));
        agent.Dispose();
        await services.DisposeAsync();
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

    private sealed class BuildTrackingTextToSpeechProvider : ITextToSpeechClientProvider
    {
        public string ProviderKey => "build-tracking-tts";
        public string DisplayName => "Build Tracking TTS";
        public int CreateCount { get; private set; }
        public List<TrackingTextToSpeechClient> Clients { get; } = [];
        public List<string?> ApiKeys { get; } = [];

        public ITextToSpeechClient CreateTextToSpeechClient(ProviderClientConfig config, IServiceProvider? services = null)
        {
            CreateCount++;
            ApiKeys.Add(config.ApiKey);
            var client = new TrackingTextToSpeechClient();
            Clients.Add(client);
            return client;
        }

        public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();
        public ProviderMetadata GetMetadata() => new() { ProviderKey = ProviderKey, DisplayName = DisplayName };
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class TrackingTextToSpeechClient : ITextToSpeechClient
    {
        public int DisposeCount { get; private set; }

        public Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new TextToSpeechResponse([]));

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() => DisposeCount++;
    }
}
