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
    public void GeneratedComposition_ShouldRegisterProviderConfigTypeAndSecretAliases()
    {
        var composition = GetGeneratedComposition();

        composition.Serialization.TryGet(
            "replicate",
            ProviderClientFamily.ImageGeneration,
            ProviderPayloadKind.Configuration,
            out _).Should().BeTrue();
        composition.Serialization.TryGet(
            "replicate",
            ProviderClientFamily.ImageGeneration,
            ProviderPayloadKind.OperationOptions,
            out _).Should().BeTrue();
        composition.SecretAliases.GetEnvironmentVariables("replicate:ApiKey")
            .Should().ContainInOrder("REPLICATE_API_KEY", "REPLICATE_API_TOKEN");
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        var config = new ImageGenerationClientConfig
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
        var config = new ProviderClientConfig
        {
            ProviderKey = "replicate",
            ModelName = "flux-schnell",
            ApiKey = "test-key"
        };
        config.ProviderConfig = new ReplicateProviderConfig { ModelOwner = "black-forest-labs" };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.ImageGeneration);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateConfiguration_DefersMissingApiKeyToSecretResolution()
    {
        var config = new ProviderClientConfig
        {
            ProviderKey = "replicate",
            ModelName = "black-forest-labs/flux-schnell"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.ImageGeneration);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidModelFormat_ShouldFail()
    {
        var config = new ProviderClientConfig
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
        var config = new ProviderClientConfig
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
        var config = new ImageGenerationClientConfig
        {
            ProviderKey = "replicate",
            ModelName = "black-forest-labs/flux-schnell",
            ApiKey = "test-key"
        };
        config.ProviderOptions = new ReplicateImageOptions
        {
            TimeoutSeconds = 0,
            PollingIntervalSeconds = -1
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.ImageGeneration);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TimeoutSeconds"));
        result.Errors.Should().Contain(e => e.Contains("PollingIntervalSeconds"));
    }

    [Fact]
    public void CreateImageGenerator_WithValidConfig_ShouldCreateClient()
    {
        var services = CreateServices();
        var config = new ProviderClientConfig
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
            var config = new ProviderClientConfig
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
                mediaType: "image/png",
                configureOptions: options =>
                {
                    options.Input = new() { ["aspect_ratio"] = "16:9" };
                    options.TimeoutSeconds = 30;
                });

        var config = builder.Config.Clients?.ImageGeneration;
        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("replicate");
        config.ModelName.Should().Be("black-forest-labs/flux-schnell");
        config.ApiKey.Should().Be("test-key");
        config.MediaType.Should().Be("image/png");

        var replicateOptions = config.ProviderOptions as ReplicateImageOptions;
        replicateOptions.Should().NotBeNull();
        replicateOptions!.Input.Should().ContainKey("aspect_ratio");
        replicateOptions.TimeoutSeconds.Should().Be(30);
    }

    private static IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretResolver>(new ExplicitSecretResolver());
        return services.BuildServiceProvider();
    }

    private static IServiceProvider CreateEnvironmentServices()
    {
        var services = new ServiceCollection();
        var composition = GetGeneratedComposition();
        services.AddSingleton<ISecretResolver>(new EnvironmentSecretResolver(composition.SecretAliases));
        return services.BuildServiceProvider();
    }

    private static ProviderComposition GetGeneratedComposition()
    {
        var marker = typeof(ReplicateProvider).Assembly
            .GetCustomAttributes(typeof(HpdProviderManifestAttribute), inherit: false)
            .Cast<HpdProviderManifestAttribute>()
            .Single(attribute => attribute.ProviderKey == "replicate");
        var fragment = (ProviderManifestFragment)marker.ManifestType
            .GetProperty("Fragment")!
            .GetValue(null)!;
        return ProviderComposition.Create([fragment]);
    }
}
#endif
