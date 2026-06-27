#if NET10_0_OR_GREATER
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Cohere;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Providers.Tests;

public class CohereProviderTests
{
    private readonly CohereProvider _provider = new();

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        var metadata = _provider.GetMetadata();

        metadata.ProviderKey.Should().Be("cohere");
        metadata.DisplayName.Should().Be("Cohere");
        metadata.DocumentationUri.Should().Be(new Uri("https://docs.cohere.com/"));

        var chat = metadata.Families[ProviderClientFamily.Chat];
        chat.Capabilities!["SupportsStreaming"].Should().Be(true);
        chat.Capabilities["SupportsFunctionCalling"].Should().Be(true);
        chat.Capabilities["SupportsVision"].Should().Be(false);

        metadata.Families.Should().ContainKey(ProviderClientFamily.Embeddings);
        metadata.Families[ProviderClientFamily.Embeddings].DefaultModelId.Should().Be("embed-english-v3.0");
    }

    [Fact]
    public void Provider_ShouldImplementChatAndEmbeddings()
    {
        _provider.Should().BeAssignableTo<IChatClientProvider>();
        _provider.Should().BeAssignableTo<IEmbeddingGeneratorProvider>();
    }

    [Fact]
    public void CreateEmbeddingGenerator_WithValidConfig_ShouldCreateClient()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "cohere",
            ModelName = "embed-english-v3.0",
            ApiKey = "test-key"
        };

        using var embeddingGenerator = _provider.CreateEmbeddingGenerator(config, CreateServices());

        embeddingGenerator.Should().NotBeNull();
        embeddingGenerator.GetService(typeof(EmbeddingGeneratorMetadata))
            .Should()
            .BeOfType<EmbeddingGeneratorMetadata>()
            .Which.DefaultModelId.Should().Be("embed-english-v3.0");
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "cohere",
            ModelName = "command-r-plus",
            ApiKey = "test-key"
        };

        config.SetProviderConfig(new CohereProviderConfig
        {
            Temperature = 0.7f,
            TopP = 0.9f,
            TopK = 50,
            MaxOutputTokens = 1024,
            Seed = 123,
            StopSequences = ["END"],
            EmbeddingModelId = "embed-v4.0"
        });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithMissingModelName_ShouldFail()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "cohere",
            ApiKey = "test-key"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name") && e.Contains("required"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingApiKey_ShouldFail()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "cohere",
            ModelName = "command-r-plus"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("required"));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(5.1f)]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail(float temperature)
    {
        var config = ValidConfig();
        config.SetProviderConfig(new CohereProviderConfig { Temperature = temperature });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 5"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTopP_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new CohereProviderConfig { TopP = 1.1f });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopP must be between 0 and 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTokenLimit_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new CohereProviderConfig { MaxOutputTokens = 0 });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxOutputTokens must be greater than 0"));
    }

    [Fact]
    public void WithCohere_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithCohere(
                model: "command-r-plus",
                apiKey: "test-key",
                configure: options =>
                {
                    options.Temperature = 0.2f;
                    options.TopP = 0.8f;
                    options.MaxOutputTokens = 512;
                    options.StopSequences = ["stop"];
                });

        var config = builder.Config.Clients?.Chat;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("cohere");
        config.ModelName.Should().Be("command-r-plus");
        config.ApiKey.Should().Be("test-key");

        var cohereConfig = config.GetProviderConfig<CohereProviderConfig>();
        cohereConfig.Should().NotBeNull();
        cohereConfig!.Temperature.Should().Be(0.2f);
        cohereConfig.TopP.Should().Be(0.8f);
        cohereConfig.MaxOutputTokens.Should().Be(512);
        cohereConfig.StopSequences.Should().Equal("stop");
    }

    [Fact]
    public void WithCohere_WithInvalidModel_ShouldThrow()
    {
        var builder = new AgentBuilder();

        var act = () => builder.WithCohere(model: " ");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model is required*");
    }

    [Fact]
    public void WithCohere_WithInvalidOptions_ShouldThrow()
    {
        var builder = new AgentBuilder();

        var act = () => builder.WithCohere(configure: options => options.TopK = 0);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*TopK must be greater than 0*");
    }

    [Fact]
    public void WithCohereEmbeddings_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithCohereEmbeddings(
                model: "embed-v4.0",
                apiKey: "test-key",
                configure: options => options.EmbeddingModelId = "embed-v4.0");

        var config = builder.Config.Clients?.Embeddings;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("cohere");
        config.ModelName.Should().Be("embed-v4.0");
        config.ApiKey.Should().Be("test-key");

        var cohereConfig = config.GetProviderConfig<CohereProviderConfig>(ProviderClientFamily.Embeddings);
        cohereConfig.Should().NotBeNull();
        cohereConfig!.EmbeddingModelId.Should().Be("embed-v4.0");
        config.GetProviderOptionsRawJson().Should().Contain("embed-v4.0");
    }

    [Fact]
    public void CohereJsonContext_ShouldSerializeAndDeserializeConfig()
    {
        var config = new CohereProviderConfig
        {
            Temperature = 0.3f,
            TopP = 0.95f,
            TopK = 40,
            MaxOutputTokens = 2048,
            Seed = 12345,
            StopSequences = ["END", "STOP"],
            EmbeddingModelId = "embed-v4.0"
        };

        var json = JsonSerializer.Serialize(config, CohereJsonContext.Default.CohereProviderConfig);
        var roundTrip = JsonSerializer.Deserialize(json, CohereJsonContext.Default.CohereProviderConfig);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyHttpAuthError()
    {
        var handler = new CohereErrorHandler();
        var exception = new HttpRequestException("Unauthorized: invalid api key", null, HttpStatusCode.Unauthorized);

        var details = handler.ParseError(exception);

        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(401);
        details.Category.Should().Be(ErrorCategory.AuthError);
        handler.RequiresSpecialHandling(details).Should().BeTrue();
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyRateLimitAsRetryable()
    {
        var handler = new CohereErrorHandler();
        var exception = new HttpRequestException("too many requests", null, HttpStatusCode.TooManyRequests);

        var details = handler.ParseError(exception);

        details.Should().NotBeNull();
        details!.Category.Should().Be(ErrorCategory.RateLimitRetryable);
        handler.GetRetryDelay(details, 1, TimeSpan.FromMilliseconds(100), 2, TimeSpan.FromSeconds(1))
            .Should()
            .Be(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyGenericMessageWithStatus()
    {
        var handler = new CohereErrorHandler();
        var exception = new InvalidOperationException("""{"message":"model not found","code":"model_not_found"} Status: 404""");

        var details = handler.ParseError(exception);

        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(404);
        details.Category.Should().Be(ErrorCategory.ModelNotFound);
        details.ErrorCode.Should().Be("model_not_found");
    }

    private static ClientProviderConfig ValidConfig()
        => new()
        {
            ProviderKey = "cohere",
            ModelName = "command-r-plus",
            ApiKey = "test-key"
        };

    private static IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretResolver>(new ExplicitSecretResolver());
        return services.BuildServiceProvider();
    }
}
#endif
