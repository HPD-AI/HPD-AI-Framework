using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Cerebras;
using HPD.Agent.Providers.DeepSeek;
using HPD.Agent.Providers.Hyperbolic;
using HPD.Agent.Providers.LMStudio;
using HPD.Agent.Providers.MiniMax;
using HPD.Agent.Providers.Nebius;
using HPD.Agent.Providers.NvidiaNim;
using HPD.Agent.Providers.Nscale;
using HPD.Agent.Providers.OpenAICompatible;
using HPD.Agent.Providers.OVHcloud;
using HPD.Agent.Providers.Perplexity;
using HPD.Agent.Providers.SambaNova;
using HPD.Agent.Providers.Scaleway;
using HPD.Agent.Providers.SiliconFlow;
using HPD.Agent.Providers.Venice;
using HPD.Agent.Providers.Zai;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace HPD.Agent.Providers.Tests;

public sealed class SimpleOpenAICompatibleProviderTests
{
    public static IEnumerable<object[]> Providers()
    {
        yield return
        [
            new ProviderCase(
                "cerebras",
                "Cerebras",
                CerebrasProvider.DefaultChatModel,
                CerebrasProvider.DefaultEndpoint,
                () => new CerebrasProvider(),
                CerebrasProviderModule.Initialize,
                () => new CerebrasProviderConfig(),
                builder => builder.WithCerebras(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "CEREBRAS_API_KEY",
                "CEREBRAS_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "deepseek",
                "DeepSeek",
                DeepSeekProvider.DefaultChatModel,
                DeepSeekProvider.DefaultEndpoint,
                () => new DeepSeekProvider(),
                DeepSeekProviderModule.Initialize,
                () => new DeepSeekProviderConfig(),
                builder => builder.WithDeepSeek(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "DEEPSEEK_API_KEY",
                "DEEPSEEK_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "sambanova",
                "SambaNova",
                SambaNovaProvider.DefaultChatModel,
                SambaNovaProvider.DefaultEndpoint,
                () => new SambaNovaProvider(),
                SambaNovaProviderModule.Initialize,
                () => new SambaNovaProviderConfig(),
                builder => builder.WithSambaNova(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "SAMBANOVA_API_KEY",
                "SAMBANOVA_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "hyperbolic",
                "Hyperbolic",
                HyperbolicProvider.DefaultChatModel,
                HyperbolicProvider.DefaultEndpoint,
                () => new HyperbolicProvider(),
                HyperbolicProviderModule.Initialize,
                () => new HyperbolicProviderConfig(),
                builder => builder.WithHyperbolic(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "HYPERBOLIC_API_KEY",
                "HYPERBOLIC_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "ovhcloud",
                "OVHcloud AI Endpoints",
                OVHcloudProvider.DefaultChatModel,
                OVHcloudProvider.DefaultEndpoint,
                () => new OVHcloudProvider(),
                OVHcloudProviderModule.Initialize,
                () => new OVHcloudProviderConfig(),
                builder => builder.WithOVHcloud(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "OVHCLOUD_API_KEY",
                "OVHCLOUD_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "nscale",
                "Nscale",
                NscaleProvider.DefaultChatModel,
                NscaleProvider.DefaultEndpoint,
                () => new NscaleProvider(),
                NscaleProviderModule.Initialize,
                () => new NscaleProviderConfig(),
                builder => builder.WithNscale(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "NSCALE_API_KEY",
                "NSCALE_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "venice",
                "Venice.ai",
                VeniceProvider.DefaultChatModel,
                VeniceProvider.DefaultEndpoint,
                () => new VeniceProvider(),
                VeniceProviderModule.Initialize,
                () => new VeniceProviderConfig(),
                builder => builder.WithVenice(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "VENICE_API_KEY",
                "VENICE_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "perplexity",
                "Perplexity",
                PerplexityProvider.DefaultChatModel,
                PerplexityProvider.DefaultEndpoint,
                () => new PerplexityProvider(),
                PerplexityProviderModule.Initialize,
                () => new PerplexityProviderConfig(),
                builder => builder.WithPerplexity(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "PERPLEXITY_API_KEY",
                "PERPLEXITY_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "lmstudio",
                "LM Studio",
                LMStudioProvider.DefaultChatModel,
                LMStudioProvider.DefaultEndpoint,
                () => new LMStudioProvider(),
                LMStudioProviderModule.Initialize,
                () => new LMStudioProviderConfig(),
                builder => builder.WithLMStudio(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "LMSTUDIO_API_KEY",
                "LMSTUDIO_ENDPOINT",
                RequiresApiKey: false)
        ];
        yield return
        [
            new ProviderCase(
                "nebius",
                "Nebius Token Factory",
                NebiusProvider.DefaultChatModel,
                NebiusProvider.DefaultEndpoint,
                () => new NebiusProvider(),
                NebiusProviderModule.Initialize,
                () => new NebiusProviderConfig(),
                builder => builder.WithNebius(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "NEBIUS_API_KEY",
                "NEBIUS_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "nvidia-nim",
                "NVIDIA NIM",
                NvidiaNimProvider.DefaultChatModel,
                NvidiaNimProvider.DefaultEndpoint,
                () => new NvidiaNimProvider(),
                NvidiaNimProviderModule.Initialize,
                () => new NvidiaNimProviderConfig(),
                builder => builder.WithNvidiaNim(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "NVIDIA_API_KEY",
                "NVIDIA_NIM_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "siliconflow",
                "SiliconFlow",
                SiliconFlowProvider.DefaultChatModel,
                SiliconFlowProvider.DefaultEndpoint,
                () => new SiliconFlowProvider(),
                SiliconFlowProviderModule.Initialize,
                () => new SiliconFlowProviderConfig(),
                builder => builder.WithSiliconFlow(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "SILICONFLOW_API_KEY",
                "SILICONFLOW_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "scaleway",
                "Scaleway Generative APIs",
                ScalewayProvider.DefaultChatModel,
                ScalewayProvider.DefaultEndpoint,
                () => new ScalewayProvider(),
                ScalewayProviderModule.Initialize,
                () => new ScalewayProviderConfig(),
                builder => builder.WithScaleway(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "SCW_SECRET_KEY",
                "SCALEWAY_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "zai",
                "Z.AI",
                ZaiProvider.DefaultChatModel,
                ZaiProvider.DefaultEndpoint,
                () => new ZaiProvider(),
                ZaiProviderModule.Initialize,
                () => new ZaiProviderConfig(),
                builder => builder.WithZai(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "ZAI_API_KEY",
                "ZAI_ENDPOINT")
        ];
        yield return
        [
            new ProviderCase(
                "minimax",
                "MiniMax",
                MiniMaxProvider.DefaultChatModel,
                MiniMaxProvider.DefaultEndpoint,
                () => new MiniMaxProvider(),
                MiniMaxProviderModule.Initialize,
                () => new MiniMaxProviderConfig(),
                builder => builder.WithMiniMax(
                    model: "test-model",
                    apiKey: "test-key",
                    endpoint: "https://example.test/v1/",
                    configure: config => config.Temperature = 0.2f),
                "MINIMAX_API_KEY",
                "MINIMAX_ENDPOINT")
        ];
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Provider_ShouldHaveExpectedMetadata(ProviderCase providerCase)
    {
        var provider = providerCase.CreateProvider();

        var metadata = provider.GetMetadata();

        metadata.ProviderKey.Should().Be(providerCase.Key);
        metadata.DisplayName.Should().Be(providerCase.DisplayName);
        metadata.Families.Should().ContainKey(ProviderClientFamily.Chat);
        metadata.Families[ProviderClientFamily.Chat].DefaultModelId.Should().Be(providerCase.DefaultModel);
        metadata.Families[ProviderClientFamily.Chat].Capabilities!["OpenAICompatibleEndpoint"]
            .Should().Be(providerCase.DefaultEndpoint.ToString());
        metadata.Families.Should().NotContainKey(ProviderClientFamily.Embeddings);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ProviderDiscovery_ShouldRegisterConfigAndSecretAliases(ProviderCase providerCase)
    {
        providerCase.Initialize();

        ProviderDiscovery.GetProviderConfigType(providerCase.Key).Should().NotBeNull();
        SecretAliasRegistry.GetAll().Should().ContainKey($"{providerCase.Key}:ApiKey")
            .WhoseValue.Should().Contain(providerCase.ApiKeyAlias);
        SecretAliasRegistry.GetAll().Should().ContainKey($"{providerCase.Key}:Endpoint")
            .WhoseValue.Should().Contain(providerCase.EndpointAlias);
    }

    [Theory]
    [InlineData("lmstudio", "LM_STUDIO_API_KEY", "LM_STUDIO_API_BASE")]
    [InlineData("scaleway", "SCW_SECRET_KEY", "SCW_BASE_URL")]
    [InlineData("zai", "BIGMODEL_API_KEY", "BIGMODEL_BASE_URL")]
    [InlineData("minimax", "MINIMAX_API_KEY", "MINIMAX_API_BASE")]
    [InlineData("nvidia-nim", "NVIDIA_NIM_API_KEY", "NVIDIA_BASE_URL")]
    public void ProviderDiscovery_ShouldRegisterLiteLlmInspiredAliases(
        string providerKey,
        string apiKeyAlias,
        string endpointAlias)
    {
        foreach (var providerCase in Providers().Select(data => (ProviderCase)data[0]))
        {
            providerCase.Initialize();
        }

        SecretAliasRegistry.GetAll().Should().ContainKey($"{providerKey}:ApiKey")
            .WhoseValue.Should().Contain(apiKeyAlias);
        SecretAliasRegistry.GetAll().Should().ContainKey($"{providerKey}:Endpoint")
            .WhoseValue.Should().Contain(endpointAlias);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed(ProviderCase providerCase)
    {
        var config = ValidConfig(providerCase);
        config.SetProviderConfig(providerCase.CreateProviderConfig());

        var result = providerCase.CreateProvider().ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ValidateConfiguration_WithMissingApiKey_ShouldFail(ProviderCase providerCase)
    {
        var config = ValidConfig(providerCase);
        config.ApiKey = null;

        var result = providerCase.CreateProvider().ValidateConfiguration(config, ProviderClientFamily.Chat);

        if (providerCase.RequiresApiKey)
        {
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("API key", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ValidateConfiguration_WithInvalidConfig_ShouldFail(ProviderCase providerCase)
    {
        var config = ValidConfig(providerCase);
        config.SetProviderConfig(providerCase.CreateProviderConfig(config =>
        {
            config.Temperature = 2.1f;
            config.ResponseFormat = "xml";
        }));

        var result = providerCase.CreateProvider().ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature"));
        result.Errors.Should().Contain(e => e.Contains("ResponseFormat"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void BuilderExtension_ShouldConfigureChatProvider(ProviderCase providerCase)
    {
        var builder = providerCase.ConfigureBuilder(new AgentBuilder());

        var config = builder.Config.Clients?.Chat;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be(providerCase.Key);
        config.ModelName.Should().Be("test-model");
        config.ApiKey.Should().Be("test-key");
        config.Endpoint.Should().Be("https://example.test/v1/");
        config.GetProviderConfig<OpenAICompatibleProviderConfig>()!
            .Temperature.Should().Be(0.2f);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ProviderDiscovery_ConfigJsonRoundTrips(ProviderCase providerCase)
    {
        providerCase.Initialize();
        var config = providerCase.CreateProviderConfig(options =>
        {
            options.Temperature = 0.4f;
            options.ResponseFormat = "json_object";
        });

        var json = ProviderDiscovery.SerializeProviderConfig(providerCase.Key, config);
        var roundTrip = ProviderDiscovery.DeserializeProviderConfig(providerCase.Key, json);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void CreateChatClient_WithFakeSecret_ShouldCreateClient(ProviderCase providerCase)
    {
        var config = ValidConfig(providerCase);

        using var chatClient = providerCase.CreateProvider().CreateChatClient(config, CreateServices());

        chatClient.Should().NotBeNull();
        chatClient.GetService(typeof(ChatClientMetadata))
            .Should()
            .BeOfType<ChatClientMetadata>()
            .Which.DefaultModelId.Should().Be(providerCase.DefaultModel);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ErrorHandler_ShouldMapOpenAICompatibleErrors(ProviderCase providerCase)
    {
        var handler = providerCase.CreateProvider().CreateErrorHandler();

        var details = handler.ParseError(new HttpRequestException(
            """Status: 429 {"error":{"message":"rate limit exceeded","type":"rate_limit_error"}}""",
            null,
            System.Net.HttpStatusCode.TooManyRequests));

        details.Should().NotBeNull();
        details!.Category.Should().Be(ErrorCategory.RateLimitRetryable);
        handler.GetRetryDelay(details, 1, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(10))
            .Should().Be(TimeSpan.FromSeconds(2));
    }

    private static ClientProviderConfig ValidConfig(ProviderCase providerCase)
        => new()
        {
            ProviderKey = providerCase.Key,
            ModelName = providerCase.DefaultModel,
            ApiKey = "test-key"
        };

    private static IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretResolver>(new EnvironmentSecretResolver());
        return services.BuildServiceProvider();
    }

    public sealed record ProviderCase(
        string Key,
        string DisplayName,
        string DefaultModel,
        Uri DefaultEndpoint,
        Func<IChatClientProvider> CreateProvider,
        Action Initialize,
        Func<OpenAICompatibleProviderConfig> CreateConfig,
        Func<AgentBuilder, AgentBuilder> ConfigureBuilder,
        string ApiKeyAlias,
        string EndpointAlias,
        bool RequiresApiKey = true)
    {
        public OpenAICompatibleProviderConfig CreateProviderConfig(
            Action<OpenAICompatibleProviderConfig>? configure = null)
        {
            var config = CreateConfig();
            configure?.Invoke(config);
            return config;
        }
    }
}
