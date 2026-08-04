using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;
using HPD.Agent.Serialization;
using HPD.Agent.Providers.Anthropic;
using HPD.Serialization;

namespace HPD.Agent.Tests.Core;

public sealed class GeneratedProviderCompositionIntegrationTests
{
    [Fact]
    public void AgentEventSerializer_RoundTripsTypedRunProviderOptions()
    {
        var services = new ServiceCollection();
        HpdGeneratedProviderServiceCollectionExtensions.AddHpdGeneratedProviders(services);
        using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<ProviderComposition>();
        var input = new UserMessagesInputEvent
        {
            RunConfig = new AgentRunConfig
            {
                Clients = new AgentClientsConfig
                {
                    Chat = new ChatClientConfig
                    {
                        ProviderKey = "anthropic",
                        ModelName = "claude-test",
                        ProviderOptions = new AnthropicChatRequestOptions
                        {
                            ThinkingBudgetTokens = 4096
                        }
                    }
                }
            }
        };

        var json = AgentEventSerializer.ToJson(input, composition);
        var roundTrip = Assert.IsType<UserMessagesInputEvent>(
            AgentEventSerializer.FromInputJson(json, composition));

        var chat = Assert.IsType<ChatClientConfig>(roundTrip.RunConfig!.Clients.Chat);
        Assert.Equal("anthropic", chat.ProviderKey);
        Assert.Equal(4096, Assert.IsType<AnthropicChatRequestOptions>(chat.ProviderOptions)
            .ThinkingBudgetTokens);
    }

    [Fact]
    public void GeneratedComposition_RegistersAnthropicWithoutModuleInitializer()
    {
        var services = new ServiceCollection();
        HpdGeneratedProviderServiceCollectionExtensions.AddHpdGeneratedProviders(services);
        using var provider = services.BuildServiceProvider();

        var composition = provider.GetRequiredService<ProviderComposition>();
        Assert.IsType<AgentFactory>(provider.GetRequiredService<IAgentConfigFactory>());
        Assert.True(composition.Descriptors.TryGet("anthropic", out var descriptor));
        var registration = composition.Runtime.GetFactory("anthropic", ProviderClientFamily.Chat);

        Assert.Equal("Anthropic (Claude)", descriptor!.DisplayName);
        Assert.Equal(["ANTHROPIC_API_KEY"],
            composition.SecretAliases.GetEnvironmentVariables("anthropic:ApiKey"));
        Assert.Equal("anthropic", registration.Factory().ProviderKey);
        Assert.True(composition.Descriptors.TryGet("ollama", out _));
        Assert.True(composition.Descriptors.TryGet("onnx-runtime", out _));
        Assert.True(composition.Serialization.TryGet(
            "ollama",
            ProviderClientFamily.Chat,
            ProviderPayloadKind.OperationOptions,
            out _));
        Assert.Equal(
            ["ONNX_MODEL_PATH", "ONNX_RUNTIME_MODEL_PATH"],
            composition.SecretAliases.GetEnvironmentVariables("onnx-runtime:ModelPath"));
    }

    [Fact]
    public void AgentBuilder_CompositionConstructor_MaterializesGeneratedProviders()
    {
        var services = new ServiceCollection();
        HpdGeneratedProviderServiceCollectionExtensions.AddHpdGeneratedProviders(services);
        using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<ProviderComposition>();

        var builder = new AgentBuilder(new AgentConfig(), composition);

        Assert.True(builder.ProviderRegistry.IsRegistered("anthropic"));
    }

    [Theory]
    [InlineData(HpdConfigFormat.Json, """
        { "clients": { "chat": {
          "providerOptions": { "thinkingBudgetTokens": 4096 },
          "providerConfig": {},
          "modelName": "claude-test",
          "providerKey": "anthropic"
        } } }
        """)]
    [InlineData(HpdConfigFormat.Yaml, """
        clients:
          chat:
            providerOptions:
              thinkingBudgetTokens: 4096
            providerConfig: {}
            modelName: claude-test
            providerKey: anthropic
        """)]
    public void ConfigSerializer_BindsGeneratedPayloadsIndependentlyOfPropertyOrder(
        HpdConfigFormat format,
        string document)
    {
        var services = new ServiceCollection();
        HpdGeneratedProviderServiceCollectionExtensions.AddHpdGeneratedProviders(services);
        using var provider = services.BuildServiceProvider();

        var config = HpdAgentConfigSerializer.Deserialize(
            document,
            provider.GetRequiredService<ProviderComposition>(),
            format)!;

        Assert.IsType<AnthropicProviderConfig>(config.Clients.Chat!.ProviderConfig);
        var options = Assert.IsType<AnthropicChatRequestOptions>(config.Clients.Chat.ProviderOptions);
        Assert.Equal(4096, options.ThinkingBudgetTokens);

        var roundTripText = HpdAgentConfigSerializer.Serialize(
            config,
            provider.GetRequiredService<ProviderComposition>(),
            format);
        var roundTrip = HpdAgentConfigSerializer.Deserialize(
            roundTripText,
            provider.GetRequiredService<ProviderComposition>(),
            format)!;
        Assert.IsType<AnthropicProviderConfig>(roundTrip.Clients.Chat!.ProviderConfig);
        Assert.Equal(
            4096,
            Assert.IsType<AnthropicChatRequestOptions>(roundTrip.Clients.Chat.ProviderOptions)
                .ThinkingBudgetTokens);
    }

    [Theory]
    [InlineData(HpdConfigFormat.Json, """
        { "providerProfiles": { "anthropic": { "chat": {
          "providerConfig": {},
          "providerOptions": { "thinkingBudgetTokens": 4096 }
        } } } }
        """)]
    [InlineData(HpdConfigFormat.Yaml, """
        providerProfiles:
          anthropic:
            chat:
              providerConfig: {}
              providerOptions:
                thinkingBudgetTokens: 4096
        """)]
    public void ConfigSerializer_UsesOuterProfileKeyForGeneratedPayloads(
        HpdConfigFormat format,
        string document)
    {
        using var provider = CreateProvider();
        var composition = provider.GetRequiredService<ProviderComposition>();

        var config = HpdAgentConfigSerializer.Deserialize(document, composition, format)!;
        var chat = config.ProviderProfiles["anthropic"].Chat!;

        Assert.Null(chat.ProviderKey);
        Assert.IsType<AnthropicProviderConfig>(chat.ProviderConfig);
        Assert.Equal(4096, Assert.IsType<AnthropicChatRequestOptions>(chat.ProviderOptions).ThinkingBudgetTokens);

        var serialized = HpdAgentConfigSerializer.Serialize(config, composition, format);
        var roundTrip = HpdAgentConfigSerializer.Deserialize(serialized, composition, format)!;
        var roundTripChat = roundTrip.ProviderProfiles["anthropic"].Chat!;
        Assert.Null(roundTripChat.ProviderKey);
        Assert.IsType<AnthropicProviderConfig>(roundTripChat.ProviderConfig);
        Assert.Equal(
            4096,
            Assert.IsType<AnthropicChatRequestOptions>(roundTripChat.ProviderOptions).ThinkingBudgetTokens);
    }

    [Theory]
    [InlineData(HpdConfigFormat.Json, """
        { "providerProfiles": { "anthropic": { "chat": {
          "providerKey": "openai",
          "providerConfig": {}
        } } } }
        """)]
    [InlineData(HpdConfigFormat.Yaml, """
        providerProfiles:
          anthropic:
            chat:
              providerKey: openai
              providerConfig: {}
        """)]
    public void ConfigSerializer_RejectsNestedProviderKeyThatContradictsProfile(
        HpdConfigFormat format,
        string document)
    {
        using var provider = CreateProvider();
        var composition = provider.GetRequiredService<ProviderComposition>();

        var exception = Assert.Throws<AgentRunConfigurationException>(
            () => HpdAgentConfigSerializer.Deserialize(document, composition, format));

        Assert.Equal("ProviderProfileKeyMismatch", exception.Code);
        Assert.Equal("providerProfiles.anthropic.chat.providerKey", exception.Path);
        Assert.Equal("anthropic", exception.ProviderKey);
    }

    [Fact]
    public void ConfigSerializer_CanonicalizesProfileKeysAndRejectsCanonicalDuplicates()
    {
        using var provider = CreateProvider();
        var composition = provider.GetRequiredService<ProviderComposition>();
        var oneProfile = HpdAgentConfigSerializer.Deserialize(
            """{ "providerProfiles": { "Anthropic": { "chat": { "providerConfig": {} } } } }""",
            composition)!;

        Assert.True(oneProfile.ProviderProfiles.ContainsKey("anthropic"));
        var serialized = HpdAgentConfigSerializer.Serialize(oneProfile, composition);
        Assert.Contains("\"anthropic\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Anthropic\"", serialized, StringComparison.Ordinal);

        var exception = Assert.Throws<AgentRunConfigurationException>(() =>
            HpdAgentConfigSerializer.Deserialize(
                """{ "providerProfiles": { "anthropic": {}, "Anthropic": {} } }""",
                composition));

        Assert.Equal("DuplicateProviderProfile", exception.Code);
        Assert.Equal("anthropic", exception.ProviderKey);
    }

    [Fact]
    public void ConfigSerializer_OmitsRedundantNestedProfileProviderKeyWhenWriting()
    {
        using var provider = CreateProvider();
        var composition = provider.GetRequiredService<ProviderComposition>();
        var config = new AgentConfig
        {
            ProviderProfiles = new Dictionary<string, AgentProviderProfile>
            {
                ["anthropic"] = new()
                {
                    Chat = new ChatClientConfig
                    {
                        ProviderKey = "anthropic",
                        ProviderConfig = new AnthropicProviderConfig()
                    }
                }
            }
        };

        var serialized = HpdAgentConfigSerializer.Serialize(config, composition);

        Assert.DoesNotContain("providerKey", serialized, StringComparison.OrdinalIgnoreCase);
        var roundTrip = HpdAgentConfigSerializer.Deserialize(serialized, composition)!;
        Assert.IsType<AnthropicProviderConfig>(roundTrip.ProviderProfiles["anthropic"].Chat!.ProviderConfig);
    }

    [Theory]
    [InlineData(HpdConfigFormat.Json)]
    [InlineData(HpdConfigFormat.Yaml)]
    public void RunConfigSerializer_RoundTripsGeneratedProviderPayloads(HpdConfigFormat format)
    {
        var services = new ServiceCollection();
        HpdGeneratedProviderServiceCollectionExtensions.AddHpdGeneratedProviders(services);
        using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<ProviderComposition>();
        var config = new AgentRunConfig
        {
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    ProviderKey = "anthropic",
                    ModelName = "claude-test",
                    ProviderConfig = new AnthropicProviderConfig(),
                    ProviderOptions = new AnthropicChatRequestOptions
                    {
                        ThinkingBudgetTokens = 2048
                    }
                }
            }
        };

        var document = HpdAgentConfigSerializer.Serialize(config, composition, format);
        var roundTrip = HpdAgentConfigSerializer.DeserializeRunConfig(document, composition, format)!;

        Assert.IsType<AnthropicProviderConfig>(roundTrip.Clients.Chat!.ProviderConfig);
        Assert.Equal(
            2048,
            Assert.IsType<AnthropicChatRequestOptions>(roundTrip.Clients.Chat.ProviderOptions)
                .ThinkingBudgetTokens);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        HpdGeneratedProviderServiceCollectionExtensions.AddHpdGeneratedProviders(services);
        return services.BuildServiceProvider();
    }
}
