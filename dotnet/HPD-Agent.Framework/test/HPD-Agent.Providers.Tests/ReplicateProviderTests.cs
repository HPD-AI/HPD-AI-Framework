#if NET10_0_OR_GREATER
#pragma warning disable MEAI001

using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Replicate;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Tests.Providers;

public class ReplicateProviderTests
{
    private readonly ReplicateProvider _provider = new();

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        var metadata = _provider.GetMetadata();

        metadata.ProviderKey.Should().Be("replicate");
        metadata.DisplayName.Should().Be("Replicate");
        metadata.DocumentationUri.Should().Be(new Uri("https://replicate.com/docs"));
        metadata.Families.Should().ContainKey(ProviderClientFamily.ImageGeneration);
        metadata.Families[ProviderClientFamily.ImageGeneration].DefaultModelId.Should().Be("black-forest-labs/flux-schnell");
        metadata.Families.Should().NotContainKey(ProviderClientFamily.Chat);
        metadata.Families.Should().NotContainKey(ProviderClientFamily.Embeddings);
    }

    [Fact]
    public void Provider_ShouldImplementImageGenerationOnly()
    {
        _provider.Should().BeAssignableTo<IImageGeneratorProvider>();
        _provider.Should().NotBeAssignableTo<IChatClientProvider>();
        _provider.Should().NotBeAssignableTo<IEmbeddingGeneratorProvider>();
    }

    [Fact]
    public void ProviderDiscovery_ShouldRegisterProviderConfigTypeAndSecretAliases()
    {
        ReplicateProviderModule.Initialize();

        ProviderDiscovery.GetProviderConfigType("replicate", ProviderClientFamily.ImageGeneration)
            .Should().NotBeNull();

        SecretAliasRegistry.GetAll().Should().ContainKey("replicate:ApiKey")
            .WhoseValue.Should().Contain(["REPLICATE_API_KEY", "REPLICATE_API_TOKEN"]);
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "replicate",
            ModelName = "black-forest-labs/flux-schnell",
            ApiKey = "test-key"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.ImageGeneration);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithModelOwner_AllowsModelNameWithoutSlash()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "replicate",
            ModelName = "flux-schnell",
            ApiKey = "test-key"
        };
        config.SetProviderConfig(new ReplicateProviderConfig { ModelOwner = "black-forest-labs" }, ProviderClientFamily.ImageGeneration);

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.ImageGeneration);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateConfiguration_WithMissingApiKey_ShouldFail()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "replicate",
            ModelName = "black-forest-labs/flux-schnell"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.ImageGeneration);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("Replicate"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidModelFormat_ShouldFail()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "replicate",
            ModelName = "flux-schnell",
            ApiKey = "test-key"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.ImageGeneration);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("owner/model"));
    }

    [Fact]
    public void ValidateConfiguration_ForChatFamily_ShouldFail()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "replicate",
            ModelName = "black-forest-labs/flux-schnell",
            ApiKey = "test-key"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("only image generation"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidOptions_ShouldFail()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "replicate",
            ModelName = "black-forest-labs/flux-schnell",
            ApiKey = "test-key"
        };
        config.SetProviderConfig(new ReplicateProviderConfig
        {
            TimeoutSeconds = 0,
            PollingIntervalSeconds = -1,
            OutputMediaType = string.Empty
        }, ProviderClientFamily.ImageGeneration);

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.ImageGeneration);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TimeoutSeconds"));
        result.Errors.Should().Contain(e => e.Contains("PollingIntervalSeconds"));
        result.Errors.Should().Contain(e => e.Contains("OutputMediaType"));
    }

    [Fact]
    public void CreateImageGenerator_WithValidConfig_ShouldCreateClient()
    {
        var services = CreateServices();
        var config = new ClientProviderConfig
        {
            ProviderKey = "replicate",
            ModelName = "black-forest-labs/flux-schnell",
            ApiKey = "test-key"
        };

        using var imageGenerator = _provider.CreateImageGenerator(config, services);

        imageGenerator.Should().NotBeNull();
        imageGenerator.GetService(typeof(ImageGeneratorMetadata))
            .Should().BeOfType<ImageGeneratorMetadata>()
            .Which.DefaultModelId.Should().Be("black-forest-labs/flux-schnell");
    }

    [Fact]
    public void CreateImageGenerator_ShouldResolveReplicateTokenAliasFromEnvironment()
    {
        global::System.Environment.SetEnvironmentVariable("REPLICATE_API_TOKEN", "env-key");
        try
        {
            var config = new ClientProviderConfig
            {
                ProviderKey = "replicate",
                ModelName = "black-forest-labs/flux-schnell"
            };

            using var imageGenerator = _provider.CreateImageGenerator(config, CreateEnvironmentServices());

            imageGenerator.Should().NotBeNull();
        }
        finally
        {
            global::System.Environment.SetEnvironmentVariable("REPLICATE_API_TOKEN", null);
        }
    }

    [Fact]
    public void WithReplicateImageGeneration_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithReplicateImageGeneration(
                model: "black-forest-labs/flux-schnell",
                apiKey: "test-key",
                configure: options =>
                {
                    options.Input = new() { ["aspect_ratio"] = "16:9" };
                    options.TimeoutSeconds = 30;
                });

        var config = builder.Config.Clients?.ImageGeneration;
        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("replicate");
        config.ModelName.Should().Be("black-forest-labs/flux-schnell");
        config.ApiKey.Should().Be("test-key");

        var replicateConfig = config.GetProviderConfig<ReplicateProviderConfig>(ProviderClientFamily.ImageGeneration);
        replicateConfig.Should().NotBeNull();
        replicateConfig!.Input.Should().ContainKey("aspect_ratio");
        replicateConfig.TimeoutSeconds.Should().Be(30);
    }

    private static IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretResolver>(new ExplicitSecretResolver());
        return services.BuildServiceProvider();
    }

    private static IServiceProvider CreateEnvironmentServices()
    {
        ReplicateProviderModule.Initialize();
        var services = new ServiceCollection();
        services.AddSingleton<ISecretResolver>(new EnvironmentSecretResolver());
        return services.BuildServiceProvider();
    }
}
#endif
