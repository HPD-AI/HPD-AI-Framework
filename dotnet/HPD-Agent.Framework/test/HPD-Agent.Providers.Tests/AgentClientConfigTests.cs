using System.Text.Json;
using FluentAssertions;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001

namespace HPD.Agent.Providers.Tests;

public class AgentClientsConfigTests
{
    [Fact]
    public void ResolveClientConfig_MergesProviderDefaultsFamilyAndRunOverrides()
    {
        var config = new AgentConfig
        {
            Clients = new AgentClientsConfig
            {
                Providers = new()
                {
                    ["openai"] = new ProviderClientConfig
                    {
                        ProviderKey = "openai",
                        ApiKey = "agent-key",
                        Endpoint = "https://agent.example",
                        ConstructionOptions = JsonDocument.Parse("""{"organizationId":"org_1","projectId":"proj_agent"}""").RootElement.Clone()
                    }
                },
                Chat = new ProviderClientConfig
                {
                    ProviderKey = "openai",
                    ModelName = "gpt-agent",
                    ConstructionOptions = JsonDocument.Parse("""{"providerFeature":"agent-default"}""").RootElement.Clone()
                }
            }
        };

        var runClients = new AgentClientsConfig
        {
            Providers = new()
            {
                ["openai"] = new ProviderClientConfig
                {
                    Endpoint = "https://run.example",
                    ConstructionOptions = JsonDocument.Parse("""{"projectId":"proj_run"}""").RootElement.Clone()
                }
            },
            Chat = new ProviderClientConfig
            {
                ModelName = "gpt-run",
                ConstructionOptions = JsonDocument.Parse("""{"requestProfile":"interactive"}""").RootElement.Clone()
            }
        };

        var resolved = config.ResolveClientConfig(ProviderClientFamily.Chat, runClients);

        resolved.Should().NotBeNull();
        resolved!.ProviderKey.Should().Be("openai");
        resolved.ModelName.Should().Be("gpt-run");
        resolved.ApiKey.Should().Be("agent-key");
        resolved.Endpoint.Should().Be("https://run.example");

        using var json = JsonDocument.Parse(resolved.GetConstructionOptionsRawJson()!);
        var root = json.RootElement;
        root.GetProperty("organizationId").GetString().Should().Be("org_1");
        root.GetProperty("projectId").GetString().Should().Be("proj_run");
        root.GetProperty("providerFeature").GetString().Should().Be("agent-default");
        root.GetProperty("requestProfile").GetString().Should().Be("interactive");
    }

    [Fact]
    public void ResolveClientConfig_RejectsNonObjectProviderOptionsWhenMerging()
    {
        var config = new AgentConfig
        {
            Clients = new AgentClientsConfig
            {
                Providers = new()
                {
                    ["openai"] = new ProviderClientConfig
                    {
                        ProviderKey = "openai",
                        ConstructionOptions = JsonDocument.Parse("[]").RootElement.Clone()
                    }
                },
                Chat = new ProviderClientConfig
                {
                    ProviderKey = "openai",
                    ConstructionOptions = JsonDocument.Parse("""{"ok":true}""").RootElement.Clone()
                }
            }
        };

        var act = () => config.ResolveClientConfig(ProviderClientFamily.Chat);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConstructionOptions merge requires*JSON object*");
    }

    [Fact]
    public void ResolveClientConfig_WhenProviderChanges_DiscardsPreviousProviderState()
    {
        var config = new AgentConfig
        {
            Clients = new AgentClientsConfig
            {
                Providers = new()
                {
                    ["anthropic"] = new ProviderClientConfig
                    {
                        ProviderKey = "anthropic",
                        Endpoint = "https://anthropic.example",
                        AuthenticationKey = "anthropic-default",
                        ConstructionOptions = JsonDocument.Parse("""{"thinkingBudget":4096}""").RootElement.Clone()
                    },
                    ["openai"] = new ProviderClientConfig
                    {
                        ProviderKey = "openai",
                        Endpoint = "https://openai.example",
                        AuthenticationKey = "openai-default",
                        ConstructionOptions = JsonDocument.Parse("""{"organizationId":"org_1"}""").RootElement.Clone()
                    }
                },
                Chat = new ProviderClientConfig
                {
                    ProviderKey = "anthropic",
                    ModelName = "claude-agent",
                    CustomHeaders = new() { ["anthropic-version"] = "2023-06-01" },
                    ConstructionOptions = JsonDocument.Parse("""{"anthropicOnly":true}""").RootElement.Clone()
                }
            }
        };

        var resolved = config.ResolveClientConfig(
            ProviderClientFamily.Chat,
            new AgentClientsConfig
            {
                Chat = new ProviderClientConfig
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

        using var json = JsonDocument.Parse(resolved.GetConstructionOptionsRawJson()!);
        json.RootElement.GetProperty("organizationId").GetString().Should().Be("org_1");
        json.RootElement.TryGetProperty("anthropicOnly", out _).Should().BeFalse();
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
