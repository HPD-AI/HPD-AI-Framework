using HPD.Agent.Providers;
using HPD.Agent.Providers.Anthropic;
using HPD.Agent.Serialization;
using HPD.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.Core;

public sealed class GeneratedProviderCompositionIntegrationTests
{
    [Fact]
    public void GeneratedComposition_RegistersProviderWithoutAssemblyScanning()
    {
        using var services = CreateProvider();
        var composition = services.GetRequiredService<ProviderComposition>();
        Assert.True(composition.Descriptors.TryGet("Anthropic", out var descriptor));
        Assert.Equal("anthropic", descriptor!.ProviderKey);
        Assert.Equal("anthropic", composition.Descriptors.Canonicalize("Anthropic"));
        Assert.NotNull(composition.Runtime.GetFactory("anthropic", "platform", ProviderClientFamily.Chat));
    }

    [Theory]
    [InlineData(HpdConfigFormat.Json)]
    [InlineData(HpdConfigFormat.Yaml)]
    public void RunConfigSerializer_RoundTripsAtomicSelectionAndGeneratedPayloads(HpdConfigFormat format)
    {
        using var services = CreateProvider();
        var composition = services.GetRequiredService<ProviderComposition>();
        var input = new AgentRunConfig
        {
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    Provider = new ProviderReference
                    {
                        Key = "anthropic", Backend = "platform",
                        Authentication = new ApiKeyProviderAuthentication { SecretKey = "anthropic:ApiKey" }
                    },
                    ModelName = "claude-test",
                    ProviderConfig = new AnthropicProviderConfig(),
                    ProviderOptions = new AnthropicChatRequestOptions { ThinkingBudgetTokens = 2048 }
                }
            }
        };

        var document = HpdAgentConfigSerializer.Serialize(input, composition, format);
        var output = HpdAgentConfigSerializer.DeserializeRunConfig(document, composition, format)!;

        Assert.Equal("anthropic", output.Clients.Chat!.Provider!.Key);
        Assert.Equal("platform", output.Clients.Chat.Provider.Backend);
        Assert.IsType<ApiKeyProviderAuthentication>(output.Clients.Chat.Provider.Authentication);
        Assert.IsType<AnthropicProviderConfig>(output.Clients.Chat.ProviderConfig);
        Assert.Equal(2048, Assert.IsType<AnthropicChatRequestOptions>(output.Clients.Chat.ProviderOptions).ThinkingBudgetTokens);
    }

    [Fact]
    public void AgentConfigSerializer_RoundTripsListSerializedProviderBackendProfiles()
    {
        using var services = CreateProvider();
        var composition = services.GetRequiredService<ProviderComposition>();
        var input = new AgentConfig
        {
            ProviderDefaults =
            {
                new AgentProviderFamilyDefault { Family = ProviderClientFamily.Chat, ProviderKey = "anthropic", BackendKey = "platform" }
            },
            ProviderProfiles =
            {
                new AgentProviderBackendProfile
                {
                    ProviderKey = "anthropic", BackendKey = "platform",
                    Clients = new AgentClientsConfig
                    {
                        Chat = new ChatClientConfig
                        {
                            Provider = new ProviderReference
                            {
                                Key = "anthropic", Backend = "platform",
                                Authentication = new ApiKeyProviderAuthentication { SecretKey = "anthropic:ApiKey" }
                            },
                            ProviderConfig = new AnthropicProviderConfig()
                        }
                    }
                }
            }
        };

        var document = HpdAgentConfigSerializer.Serialize(input, composition);
        var output = HpdAgentConfigSerializer.Deserialize(document, composition)!;

        Assert.Single(output.ProviderProfiles);
        Assert.Equal("anthropic", output.ProviderProfiles[0].ProviderKey);
        Assert.Equal("platform", output.ProviderProfiles[0].BackendKey);
        Assert.IsType<AnthropicProviderConfig>(output.ProviderProfiles[0].Clients.Chat!.ProviderConfig);
    }

    [Fact]
    public void ProviderProfileIndex_DeepSnapshotsMutableProviderOptionsAtBuildBoundary()
    {
        using var services = CreateProvider();
        var composition = services.GetRequiredService<ProviderComposition>();
        var options = new AnthropicChatRequestOptions { ThinkingBudgetTokens = 2048 };
        var config = new AgentConfig
        {
            ProviderDefaults =
            {
                new AgentProviderFamilyDefault
                {
                    Family = ProviderClientFamily.Chat,
                    ProviderKey = "anthropic",
                    BackendKey = "platform"
                }
            },
            ProviderProfiles =
            {
                new AgentProviderBackendProfile
                {
                    ProviderKey = "anthropic",
                    BackendKey = "platform",
                    Clients = new AgentClientsConfig
                    {
                        Chat = new ChatClientConfig
                        {
                            Provider = new ProviderReference
                            {
                                Key = "anthropic",
                                Backend = "platform",
                                Authentication = new ApiKeyProviderAuthentication
                                    { SecretKey = "anthropic:ApiKey" }
                            },
                            ProviderOptions = options
                        }
                    }
                }
            }
        };

        var index = AgentProviderProfileIndex.Create(config, composition);
        var resolver = new EffectiveProviderClientConfigResolver(composition);
        var beforeMutation = resolver.Resolve(config, ProviderClientFamily.Chat, profileIndex: index);
        options.ThinkingBudgetTokens = 8192;
        config.ProviderProfiles.Clear();

        var afterMutation = resolver.Resolve(config, ProviderClientFamily.Chat, profileIndex: index);

        Assert.Equal(beforeMutation.FamilyOperation.Fingerprint, afterMutation.FamilyOperation.Fingerprint);
        Assert.Equal(beforeMutation.ConstructionFingerprint, afterMutation.ConstructionFingerprint);
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Proxy-Authorization")]
    [InlineData("api-key")]
    [InlineData("x-api-key")]
    [InlineData("Cookie")]
    public void EffectiveResolver_RejectsCredentialBearingCustomHeaders(string header)
    {
        using var services = CreateProvider();
        var composition = services.GetRequiredService<ProviderComposition>();
        var config = new AgentConfig
        {
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    Provider = new ProviderReference
                    {
                        Key = "anthropic",
                        Backend = "platform",
                        Authentication = new ApiKeyProviderAuthentication { SecretKey = "anthropic:ApiKey" }
                    },
                    ModelName = "claude-test",
                    CustomHeaders = new Dictionary<string, string> { [header] = "secret-material" }
                }
            }
        };

        var error = Assert.Throws<AgentRunConfigurationException>(() =>
            new EffectiveProviderClientConfigResolver(composition).Resolve(config, ProviderClientFamily.Chat));

        Assert.Equal("AuthenticationHeaderNotAllowed", error.Code);
        Assert.DoesNotContain("secret-material", error.ToString(), StringComparison.Ordinal);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        HpdGeneratedProviderServiceCollectionExtensions.AddHpdGeneratedProviders(services);
        return services.BuildServiceProvider();
    }
}
