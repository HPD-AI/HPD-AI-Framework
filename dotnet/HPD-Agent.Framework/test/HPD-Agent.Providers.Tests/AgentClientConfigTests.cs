using FluentAssertions;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001

namespace HPD.Agent.Providers.Tests;

public class AgentClientsConfigTests
{
    private sealed class TestProviderConfig : IProviderConfig
    {
        public string? Value { get; init; }
    }

    [Fact]
    public void ResolveClientConfig_MergesProviderDefaultsFamilyAndRunOverrides()
    {
        var config = new AgentConfig
        {
            ProviderProfiles = new Dictionary<string, AgentProviderProfile>
            {
                ["openai"] = new AgentProviderProfile
                {
                    Chat = new ChatClientConfig
                    {
                        ApiKey = "agent-key",
                        Endpoint = "https://agent.example",
                        ProviderConfig = new TestProviderConfig { Value = "profile" }
                    }
                }
            },
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    ProviderKey = "openai",
                    ModelName = "gpt-agent",
                    ProviderConfig = new TestProviderConfig { Value = "agent" }
                }
            }
        };

        var runClients = new AgentClientsConfig
        {
            Chat = new ChatClientConfig
            {
                ModelName = "gpt-run",
                Endpoint = "https://run.example",
                ProviderConfig = new TestProviderConfig { Value = "run" }
            }
        };

        var resolved = config.ResolveClientConfig(ProviderClientFamily.Chat, runClients);

        resolved.Should().NotBeNull();
        resolved!.ProviderKey.Should().Be("openai");
        resolved.ModelName.Should().Be("gpt-run");
        resolved.ApiKey.Should().Be("agent-key");
        resolved.Endpoint.Should().Be("https://run.example");

        resolved.ProviderConfig.Should().BeOfType<TestProviderConfig>()
            .Which.Value.Should().Be("run");
    }

    [Fact]
    public void ResolveClientConfig_UsesTypedProviderPayloadAsOneAtomicValue()
    {
        var config = new AgentConfig
        {
            ProviderProfiles = new Dictionary<string, AgentProviderProfile>
            {
                ["openai"] = new AgentProviderProfile
                {
                    Chat = new ChatClientConfig
                    {
                        ProviderConfig = new TestProviderConfig { Value = "profile" }
                    }
                }
            },
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    ProviderKey = "openai",
                    ProviderConfig = new TestProviderConfig { Value = "agent" }
                }
            }
        };

        var resolved = config.ResolveClientConfig(ProviderClientFamily.Chat);

        resolved!.ProviderConfig.Should().BeOfType<TestProviderConfig>()
            .Which.Value.Should().Be("agent");
    }

    [Fact]
    public void ResolveClientConfig_WhenProviderChanges_DiscardsPreviousProviderState()
    {
        var config = new AgentConfig
        {
            ProviderProfiles = new Dictionary<string, AgentProviderProfile>
            {
                ["anthropic"] = new AgentProviderProfile
                {
                    Chat = new ChatClientConfig
                    {
                        Endpoint = "https://anthropic.example",
                        AuthenticationKey = "anthropic-default",
                        ProviderConfig = new TestProviderConfig { Value = "anthropic" }
                    }
                },
                ["openai"] = new AgentProviderProfile
                {
                    Chat = new ChatClientConfig
                    {
                        Endpoint = "https://openai.example",
                        AuthenticationKey = "openai-default",
                        ProviderConfig = new TestProviderConfig { Value = "openai" }
                    }
                }
            },
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    ProviderKey = "anthropic",
                    ModelName = "claude-agent",
                    CustomHeaders = new() { ["anthropic-version"] = "2023-06-01" },
                    ProviderConfig = new TestProviderConfig { Value = "anthropic-agent" }
                }
            }
        };

        var resolved = config.ResolveClientConfig(
            ProviderClientFamily.Chat,
            new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    ProviderKey = "openai",
                    ModelName = "gpt-run"
                }
            });

        resolved.Should().NotBeNull();
        resolved!.ProviderKey.Should().Be("openai");
        resolved.ModelName.Should().Be("gpt-run");
        resolved.Endpoint.Should().Be("https://openai.example");
        resolved.AuthenticationKey.Should().Be("openai-default");
        resolved.CustomHeaders.Should().BeNull();

        resolved.ProviderConfig.Should().BeOfType<TestProviderConfig>()
            .Which.Value.Should().Be("openai");
    }

    [Fact]
    public void GetRequiredProvider_ReturnsTypedFamilyProvider()
    {
        var registry = new ProviderRegistry();
        var provider = new ChatOnlyProvider();
        registry.Register(provider);

        var resolved = registry.GetRequiredProvider<IChatClientProvider>("test");

        resolved.Should().BeSameAs(provider);
    }

    [Fact]
    public void GetRequiredProvider_RequiresCanonicalProviderKeyCasing()
    {
        var registry = new ProviderRegistry();
        registry.Register(new ChatOnlyProvider());

        var act = () => registry.GetRequiredProvider<IChatClientProvider>("Test");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Provider 'Test' is not registered*Available providers: test*");
    }

    [Fact]
    public void GetRequiredProvider_ThrowsClearErrorWhenProviderDoesNotSupportFamily()
    {
        var registry = new ProviderRegistry();
        registry.Register(new ChatOnlyProvider());

        var act = () => registry.GetRequiredProvider<IRealtimeClientProvider>("test");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not support client family 'Realtime'*Supported families: Chat*");
    }

    [Fact]
    public void ProviderRegistry_ComposesSameProviderKeyAcrossClientFamilies()
    {
        var registry = new ProviderRegistry();
        registry.Register(new ChatOnlyProvider());
        registry.Register(new TextToSpeechOnlyProvider());

        registry.GetRegisteredProviders().Should().ContainSingle().Which.Should().Be("test");
        registry.GetRequiredProvider<IChatClientProvider>("test").Should().NotBeNull();
        registry.GetRequiredProvider<ITextToSpeechClientProvider>("test").Should().NotBeNull();

        var metadata = registry.GetProvider("test")!.GetMetadata();
        metadata.Families.Keys.Should().BeEquivalentTo(new[]
        {
            ProviderClientFamily.Chat,
            ProviderClientFamily.TextToSpeech
        });

        registry.GetProvider<IRealtimeClientProvider>("test").Should().BeNull();
        registry.Invoking(static item => item.GetRequiredProvider<IRealtimeClientProvider>("test"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*does not support client family 'Realtime'*");
    }

    private sealed class ChatOnlyProvider : IChatClientProvider
    {
        public string ProviderKey => "test";
        public string DisplayName => "Test";

        public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat
                }
            }
        };

        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class TextToSpeechOnlyProvider : ITextToSpeechClientProvider
    {
        public string ProviderKey => "test";
        public string DisplayName => "Test";

        public ITextToSpeechClient CreateTextToSpeechClient(ProviderClientConfig config, IServiceProvider? services = null)
            => throw new NotSupportedException();

        public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.TextToSpeech] = new()
                {
                    Family = ProviderClientFamily.TextToSpeech
                }
            }
        };

        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }
}
