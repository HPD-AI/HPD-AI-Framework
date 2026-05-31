using System.Text.Json;
using FluentAssertions;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001

namespace HPD.Agent.Providers.Tests;

public class AgentClientConfigTests
{
    [Fact]
    public void ResolveClientConfig_MergesProviderDefaultsFamilyAndRunOverrides()
    {
        var config = new AgentConfig
        {
            Clients = new AgentClientConfig
            {
                Providers = new()
                {
                    ["openai"] = new ClientProviderConfig
                    {
                        ProviderKey = "openai",
                        ApiKey = "agent-key",
                        Endpoint = "https://agent.example",
                        ProviderOptionsJson = "{\"organizationId\":\"org_1\",\"projectId\":\"proj_agent\"}"
                    }
                },
                Chat = new ClientProviderConfig
                {
                    ProviderKey = "openai",
                    ModelName = "gpt-agent",
                    ProviderOptionsJson = "{\"reasoningEffortLevel\":\"medium\"}"
                }
            }
        };

        var runClients = new AgentClientConfig
        {
            Providers = new()
            {
                ["openai"] = new ClientProviderConfig
                {
                    Endpoint = "https://run.example",
                    ProviderOptionsJson = "{\"projectId\":\"proj_run\"}"
                }
            },
            Chat = new ClientProviderConfig
            {
                ModelName = "gpt-run",
                ProviderOptionsJson = "{\"temperatureProfile\":\"creative\"}"
            }
        };

        var resolved = config.ResolveClientConfig(ProviderClientFamily.Chat, runClients);

        resolved.Should().NotBeNull();
        resolved!.ProviderKey.Should().Be("openai");
        resolved.ModelName.Should().Be("gpt-run");
        resolved.ApiKey.Should().Be("agent-key");
        resolved.Endpoint.Should().Be("https://run.example");

        using var json = JsonDocument.Parse(resolved.ProviderOptionsJson!);
        var root = json.RootElement;
        root.GetProperty("organizationId").GetString().Should().Be("org_1");
        root.GetProperty("projectId").GetString().Should().Be("proj_run");
        root.GetProperty("reasoningEffortLevel").GetString().Should().Be("medium");
        root.GetProperty("temperatureProfile").GetString().Should().Be("creative");
    }

    [Fact]
    public void ResolveClientConfig_RejectsNonObjectProviderOptionsJsonWhenMerging()
    {
        var config = new AgentConfig
        {
            Clients = new AgentClientConfig
            {
                Providers = new()
                {
                    ["openai"] = new ClientProviderConfig
                    {
                        ProviderKey = "openai",
                        ProviderOptionsJson = "[]"
                    }
                },
                Chat = new ClientProviderConfig
                {
                    ProviderKey = "openai",
                    ProviderOptionsJson = "{\"ok\":true}"
                }
            }
        };

        var act = () => config.ResolveClientConfig(ProviderClientFamily.Chat);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProviderOptionsJson merge requires*JSON object*");
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

        public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
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

        public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class TextToSpeechOnlyProvider : ITextToSpeechClientProvider
    {
        public string ProviderKey => "test";
        public string DisplayName => "Test";

        public ITextToSpeechClient CreateTextToSpeechClient(ClientProviderConfig config, IServiceProvider? services = null)
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

        public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }
}
