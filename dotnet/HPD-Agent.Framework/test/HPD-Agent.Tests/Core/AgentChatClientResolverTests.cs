using System.Runtime.CompilerServices;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.Core;

public sealed class AgentChatClientResolverTests
{
    private static AgentRunConfig RuntimeChat(string providerKey, string modelName) => new()
    {
        Clients = new AgentClientsConfig
        {
            Chat = new ChatClientConfig { ProviderKey = providerKey, ModelName = modelName }
        }
    };

    [Fact]
    public async Task ResolveAsync_FallbackToParent_PrefersBuilderDefault()
    {
        var agentClient = new FakeChatClient();
        var inheritedClient = new FakeChatClient();
        var resolver = new AgentChatClientResolver(null, null);

        await using var lease = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            BuilderDefault = AgentChatClientHandle.Borrowed(agentClient, AgentChatClientSource.BuilderDefault),
            ParentResolved = AgentChatClientHandle.Borrowed(inheritedClient, AgentChatClientSource.ParentResolved),
            ParentInheritance = ClientFamilyInheritanceMode.FallbackToParent
        });

        Assert.Same(agentClient, lease.Client);
    }

    [Fact]
    public async Task ResolveAsync_InheritResolved_PrefersParentOverBuilderDefault()
    {
        var agentClient = new FakeChatClient();
        var inheritedClient = new FakeChatClient();
        var resolver = new AgentChatClientResolver(null, null);

        await using var lease = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            BuilderDefault = AgentChatClientHandle.Borrowed(agentClient, AgentChatClientSource.BuilderDefault),
            ParentResolved = AgentChatClientHandle.Borrowed(inheritedClient, AgentChatClientSource.ParentResolved),
            ParentInheritance = ClientFamilyInheritanceMode.InheritResolved
        });

        Assert.Same(inheritedClient, lease.Client);
    }

    [Fact]
    public async Task ResolveAsync_Override_BeatsBuilderDefault()
    {
        var overrideClient = new FakeChatClient();
        var resolver = new AgentChatClientResolver(null, null);

        await using var lease = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = new AgentRunConfig
            {
                Clients = new AgentClientsConfig
                {
                    Chat = new ChatClientConfig
                    {
                        Override = new ClientOverride<IChatClient> { Client = overrideClient }
                    }
                }
            },
            BuilderDefault = AgentChatClientHandle.Borrowed(new FakeChatClient(), AgentChatClientSource.BuilderDefault)
        });

        Assert.Same(overrideClient, lease.Client);
        Assert.Equal(AgentChatClientSource.InjectedOverride, lease.Handle.Source);
    }

    [Fact]
    public async Task ResolveAsync_RuntimeProvider_ReusesClientUntilResolverDisposal()
    {
        var client = new TrackingChatClient();
        var registry = new ProviderRegistry();
        registry.Register(new TrackingProvider(client));
        using var resolver = new AgentChatClientResolver(registry, null);

        var lease = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = RuntimeChat("tracking", "model")
        });
        var childLease = lease.Handle.AcquireLease();

        await lease.DisposeAsync();
        Assert.Equal(0, client.DisposeCount);

        await childLease.DisposeAsync();
        Assert.Equal(0, client.DisposeCount);

        await using var second = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = RuntimeChat("tracking", "model")
        });

        Assert.Same(client, second.Client);
        Assert.Equal(1, ((TrackingProvider)registry.GetProvider("tracking")!).CreateCount);

        resolver.Dispose();
        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public async Task ResolveAsync_RuntimeApiKey_DoesNotReuseClientAcrossRuns()
    {
        var provider = new CreatingTrackingProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        using var resolver = new AgentChatClientResolver(registry, null);
        var request = new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = RuntimeChat("creating", "model")
        };
        request.RunConfig.Clients.Chat!.ApiKey = "runtime-only";

        await using (var first = await resolver.ResolveAsync(request)) { }
        await using (var second = await resolver.ResolveAsync(request)) { }

        Assert.Equal(2, provider.CreateCount);
        Assert.All(provider.Clients, static client => Assert.Equal(1, client.DisposeCount));
    }

    [Fact]
    public async Task ResolveAsync_ConcurrentEquivalentRequests_CreateOneClient()
    {
        var provider = new DelayedTrackingProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        using var resolver = new AgentChatClientResolver(registry, null);
        var request = new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = RuntimeChat("delayed", "model")
        };

        var leases = await Task.WhenAll(
            Enumerable.Range(0, 16)
                .Select(_ => resolver.ResolveAsync(request).AsTask()));

        Assert.Equal(1, provider.CreateCount);
        Assert.All(leases, lease => Assert.Same(leases[0].Client, lease.Client));
        foreach (var lease in leases)
            await lease.DisposeAsync();
    }

    [Fact]
    public async Task ResolveAsync_DifferentModels_UseDifferentCacheEntries()
    {
        var provider = new CreatingTrackingProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        using var resolver = new AgentChatClientResolver(registry, null);

        await using var first = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = RuntimeChat("creating", "model-a")
        });
        await using var second = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = RuntimeChat("creating", "model-b")
        });

        Assert.Equal(2, provider.CreateCount);
        Assert.NotSame(first.Client, second.Client);
    }

    [Theory]
    [InlineData("endpoint")]
    [InlineData("headers")]
    public async Task ResolveAsync_DifferentAcquisitionInputs_UseDifferentCacheEntries(string difference)
    {
        var provider = new CreatingTrackingProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        using var resolver = new AgentChatClientResolver(registry, null);
        var firstConfig = new ChatClientConfig { ProviderKey = "creating", ModelName = "model" };
        var secondConfig = (ChatClientConfig)ProviderClientConfigResolver.Clone(firstConfig);
        switch (difference)
        {
            case "endpoint": secondConfig.Endpoint = "https://example.test"; break;
            case "headers": secondConfig.CustomHeaders = new() { ["X-Tenant"] = "two" }; break;
        }

        await using var first = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig { Clients = new AgentClientsConfig { Chat = firstConfig } }
        });
        await using var second = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig { Clients = new AgentClientsConfig { Chat = secondConfig } }
        });

        Assert.Equal(2, provider.CreateCount);
        Assert.NotSame(first.Client, second.Client);
    }

    [Fact]
    public async Task ResolveAsync_HeaderOrder_DoesNotChangeCacheIdentity()
    {
        var provider = new CreatingTrackingProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        using var resolver = new AgentChatClientResolver(registry, null);

        static AgentChatClientResolutionRequest Request(Dictionary<string, string> headers) => new()
        {
            AgentConfig = new AgentConfig
            {
                Clients = new AgentClientsConfig
                {
                    Chat = new ChatClientConfig { ProviderKey = "creating", ModelName = "model", CustomHeaders = headers }
                }
            }
        };

        await using var first = await resolver.ResolveAsync(Request(new() { ["X-B"] = "2", ["X-A"] = "1" }));
        await using var second = await resolver.ResolveAsync(Request(new() { ["X-A"] = "1", ["X-B"] = "2" }));

        Assert.Equal(1, provider.CreateCount);
        Assert.Same(first.Client, second.Client);
    }

    [Fact]
    public async Task ResolveAsync_CancelledWaiter_DoesNotPoisonSharedConstruction()
    {
        var provider = new DelayedTrackingProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        using var resolver = new AgentChatClientResolver(registry, null);
        var request = new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = RuntimeChat("delayed", "model")
        };
        using var cancellation = new CancellationTokenSource(1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(request, cancellation.Token).AsTask());
        await using var successful = await resolver.ResolveAsync(request);

        Assert.NotNull(successful.Client);
        Assert.Equal(1, provider.CreateCount);
    }

    [Fact]
    public async Task ResolveAsync_FailedCreation_IsEvictedAndCanBeRetried()
    {
        var provider = new FailOnceTrackingProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        using var resolver = new AgentChatClientResolver(registry, null);
        var request = new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = RuntimeChat("fail-once", "model")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(request).AsTask());
        await using var lease = await resolver.ResolveAsync(request);

        Assert.NotNull(lease.Client);
        Assert.Equal(2, provider.CreateCount);
    }

    [Fact]
    public async Task ResolveAsync_NamedAuthentication_ResolvesStaticSecretLocally()
    {
        var client = new TrackingChatClient();
        var provider = new TrackingProvider(client);
        var providers = new ProviderRegistry();
        providers.Register(provider);
        var authentication = new InMemoryProviderAuthenticationRegistry();
        authentication.Register(new ProviderAuthenticationRegistration
        {
            Key = "tracking-work",
            ProviderKey = "tracking",
            SecretKey = "tracking:work:ApiKey"
        });
        var services = new ServiceCollection()
            .AddSingleton<IProviderAuthenticationRegistry>(authentication)
            .AddSingleton<ISecretResolver>(new ExplicitSecretResolver(new Dictionary<string, string>
            {
                ["tracking:work:ApiKey"] = "resolved-key"
            }))
            .BuildServiceProvider();
        var resolver = new AgentChatClientResolver(providers, services);

        var runConfig = RuntimeChat("tracking", "model");
        runConfig.Clients.Chat!.AuthenticationKey = "tracking-work";
        await using var lease = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = runConfig
        });

        Assert.Equal("resolved-key", provider.LastConfig?.ApiKey);
        Assert.Equal("tracking-work", provider.LastConfig?.AuthenticationKey);
    }

    [Fact]
    public async Task ResolveAsync_MultipleRegistrationsWithoutDefault_FailsExplicitly()
    {
        var providers = new ProviderRegistry();
        providers.Register(new TrackingProvider(new TrackingChatClient()));
        var authentication = new InMemoryProviderAuthenticationRegistry();
        authentication.Register(new ProviderAuthenticationRegistration
        {
            Key = "tracking-one",
            ProviderKey = "tracking",
            SecretKey = "tracking:one:ApiKey"
        });
        authentication.Register(new ProviderAuthenticationRegistration
        {
            Key = "tracking-two",
            ProviderKey = "tracking",
            SecretKey = "tracking:two:ApiKey"
        });
        var services = new ServiceCollection()
            .AddSingleton<IProviderAuthenticationRegistry>(authentication)
            .BuildServiceProvider();
        using var resolver = new AgentChatClientResolver(providers, services);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync(new AgentChatClientResolutionRequest
            {
                AgentConfig = new AgentConfig(),
                RunConfig = RuntimeChat("tracking", "model")
            }).AsTask());

        Assert.Contains("AuthenticationSelectionRequired", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_UniqueHostDefault_IsSelected()
    {
        var provider = new TrackingProvider(new TrackingChatClient());
        var providers = new ProviderRegistry();
        providers.Register(provider);
        var authentication = new InMemoryProviderAuthenticationRegistry();
        authentication.Register(new ProviderAuthenticationRegistration
        {
            Key = "tracking-one",
            ProviderKey = "tracking",
            SecretKey = "tracking:one:ApiKey"
        });
        authentication.Register(new ProviderAuthenticationRegistration
        {
            Key = "tracking-default",
            ProviderKey = "tracking",
            SecretKey = "tracking:default:ApiKey",
            IsDefault = true
        });
        var services = new ServiceCollection()
            .AddSingleton<IProviderAuthenticationRegistry>(authentication)
            .AddSingleton<ISecretResolver>(new ExplicitSecretResolver(new Dictionary<string, string>
            {
                ["tracking:default:ApiKey"] = "default-key"
            }))
            .BuildServiceProvider();
        using var resolver = new AgentChatClientResolver(providers, services);

        await using var lease = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = new AgentConfig(),
            RunConfig = RuntimeChat("tracking", "model")
        });

        Assert.Equal("tracking-default", provider.LastConfig?.AuthenticationKey);
        Assert.Equal("default-key", provider.LastConfig?.ApiKey);
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    [InlineData("x-api-key")]
    [InlineData("api-key")]
    [InlineData("Proxy-Authorization")]
    public async Task ResolveAsync_RejectsAuthenticationInSerializableHeaders(string header)
    {
        var providers = new ProviderRegistry();
        providers.Register(new TrackingProvider(new TrackingChatClient()));
        using var resolver = new AgentChatClientResolver(providers, null);

        var runConfig = RuntimeChat("tracking", "model");
        runConfig.Clients.Chat!.CustomHeaders = new() { [header] = "secret" };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync(new AgentChatClientResolutionRequest
            {
                AgentConfig = new AgentConfig(),
                RunConfig = runConfig
            }).AsTask());

        Assert.Contains("cannot be used for provider authentication", exception.Message, StringComparison.Ordinal);
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
        public ProviderClientConfig? LastConfig { get; private set; }
        public int CreateCount { get; private set; }
        public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            LastConfig = config;
            return client;
        }
        public IProviderErrorHandler CreateErrorHandler() => new NoopErrorHandler();
        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>()
        };
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class CreatingTrackingProvider : IChatClientProvider
    {
        public string ProviderKey => "creating";
        public string DisplayName => "Creating";
        public int CreateCount { get; private set; }
        public List<TrackingChatClient> Clients { get; } = new();

        public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            var client = new TrackingChatClient();
            Clients.Add(client);
            return client;
        }

        public IProviderErrorHandler CreateErrorHandler() => new NoopErrorHandler();
        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>()
        };
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class DelayedTrackingProvider : IChatClientProvider
    {
        private int _createCount;
        public string ProviderKey => "delayed";
        public string DisplayName => "Delayed";
        public int CreateCount => Volatile.Read(ref _createCount);

        public async ValueTask<IChatClient> CreateChatClientAsync(
            ProviderClientConfig config,
            IServiceProvider? services = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _createCount);
            await Task.Delay(25, cancellationToken);
            return new TrackingChatClient();
        }

        public IProviderErrorHandler CreateErrorHandler() => new NoopErrorHandler();
        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName
        };
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class FailOnceTrackingProvider : IChatClientProvider
    {
        private int _createCount;
        public string ProviderKey => "fail-once";
        public string DisplayName => "Fail Once";
        public int CreateCount => Volatile.Read(ref _createCount);

        public ValueTask<IChatClient> CreateChatClientAsync(
            ProviderClientConfig config,
            IServiceProvider? services = null,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _createCount) == 1)
                throw new InvalidOperationException("expected first failure");
            return ValueTask.FromResult<IChatClient>(new TrackingChatClient());
        }

        public IProviderErrorHandler CreateErrorHandler() => new NoopErrorHandler();
        public ProviderMetadata GetMetadata() => new() { ProviderKey = ProviderKey, DisplayName = DisplayName };
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class NoopErrorHandler : IProviderErrorHandler
    {
        public ProviderErrorDetails? ParseError(Exception exception) => null;
        public TimeSpan? GetRetryDelay(ProviderErrorDetails details, int attempt, TimeSpan initialDelay, double multiplier, TimeSpan maxDelay) => null;
        public bool RequiresSpecialHandling(ProviderErrorDetails details) => false;
    }
}
