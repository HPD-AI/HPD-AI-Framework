using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;
using HPD.Agent.Serialization;
using HPD.Agent.Providers.Anthropic;
using HPD.Serialization;

namespace HPD.Agent.Tests.Core;

public sealed class GeneratedProviderCompositionIntegrationTests
{
    [Fact]
    public void GeneratedComposition_RegistersAnthropicWithoutModuleInitializer()
    {
        var services = new ServiceCollection();
        services.AddHpdGeneratedProviders();
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
        services.AddHpdGeneratedProviders();
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
        services.AddHpdGeneratedProviders();
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
    [InlineData(HpdConfigFormat.Json)]
    [InlineData(HpdConfigFormat.Yaml)]
    public void RunConfigSerializer_RoundTripsGeneratedProviderPayloads(HpdConfigFormat format)
    {
        var services = new ServiceCollection();
        services.AddHpdGeneratedProviders();
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
}
