#if NET10_0_OR_GREATER
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Groq;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Providers.Tests;

public class GroqProviderTests
{
    private readonly GroqProvider _provider = new();

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        var metadata = _provider.GetMetadata();

        metadata.ProviderKey.Should().Be("groq");
        metadata.DisplayName.Should().Be("Groq");
        metadata.DocumentationUri.Should().Be(new Uri("https://console.groq.com/docs/"));

        var chat = metadata.Families[ProviderClientFamily.Chat];
        chat.DefaultModelId.Should().Be("llama-3.3-70b-versatile");
        chat.Capabilities!["SupportsStreaming"].Should().Be(true);
        chat.Capabilities["SupportsFunctionCalling"].Should().Be(true);
        chat.Capabilities["SupportsJsonResponseFormat"].Should().Be(true);

        metadata.Families.Should().NotContainKey(ProviderClientFamily.Embeddings);
    }

    [Fact]
    public void Provider_ShouldImplementChatOnly()
    {
        _provider.Should().BeAssignableTo<IChatClientProvider>();
        _provider.Should().NotBeAssignableTo<IEmbeddingGeneratorProvider>();
    }

    [Fact]
    public void ProviderContributionRegistry_ShouldRegisterProviderConfigTypeAndSecretAliases()
    {
        GroqProviderModule.Initialize();

        ProviderContributionRegistry.GetProviderConfigType("groq").Should().NotBeNull();
        SecretAliasRegistry.GetAll().Should().ContainKey("groq:ApiKey")
            .WhoseValue.Should().Equal("GROQ_API_KEY");
        SecretAliasRegistry.GetAll().Should().ContainKey("groq:Endpoint")
            .WhoseValue.Should().Equal("GROQ_ENDPOINT");
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new GroqProviderConfig
        {
            Temperature = 0.7,
            TopP = 0.9,
            MaxOutputTokens = 1024,
            Seed = 123,
            StopSequences = ["END"],
            ResponseFormat = "json_object",
            ToolChoice = "required"
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
            ProviderKey = "groq",
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
            ProviderKey = "groq",
            ModelName = "llama-3.3-70b-versatile"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("GROQ_API_KEY"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidEndpoint_ShouldFail()
    {
        var config = ValidConfig();
        config.Endpoint = "not a uri";

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Endpoint"));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail(double temperature)
    {
        var config = ValidConfig();
        config.SetProviderConfig(new GroqProviderConfig { Temperature = temperature });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidResponseFormat_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new GroqProviderConfig { ResponseFormat = "xml" });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ResponseFormat"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidToolChoice_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new GroqProviderConfig { ToolChoice = "maybe" });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ToolChoice"));
    }

    [Fact]
    public void WithGroq_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithGroq(
                model: "openai/gpt-oss-20b",
                apiKey: "test-key",
                endpoint: "https://example.test/openai/v1/",
                configure: options =>
                {
                    options.Temperature = 0.2;
                    options.TopP = 0.8;
                    options.MaxOutputTokens = 512;
                    options.ResponseFormat = "json_object";
                    options.ToolChoice = "required";
                    options.StopSequences = ["stop"];
                });

        var config = builder.Config.Clients?.Chat;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("groq");
        config.ModelName.Should().Be("openai/gpt-oss-20b");
        config.ApiKey.Should().Be("test-key");
        config.Endpoint.Should().Be("https://example.test/openai/v1/");

        var groqConfig = config.GetProviderConfig<GroqProviderConfig>();
        groqConfig.Should().NotBeNull();
        groqConfig!.Temperature.Should().Be(0.2);
        groqConfig.TopP.Should().Be(0.8);
        groqConfig.MaxOutputTokens.Should().Be(512);
        groqConfig.ResponseFormat.Should().Be("json_object");
        groqConfig.ToolChoice.Should().Be("required");
        groqConfig.StopSequences.Should().Equal("stop");
    }

    [Fact]
    public void GroqJsonContext_ShouldSerializeAndDeserializeConfig()
    {
        var config = new GroqProviderConfig
        {
            Temperature = 0.3,
            TopP = 0.95,
            MaxOutputTokens = 2048,
            Seed = 12345,
            StopSequences = ["END", "STOP"],
            ResponseFormat = "json_object",
            ToolChoice = "none"
        };

        var json = JsonSerializer.Serialize(config, GroqJsonContext.Default.GroqProviderConfig);
        var roundTrip = JsonSerializer.Deserialize(json, GroqJsonContext.Default.GroqProviderConfig);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void ProviderContributionRegistry_ConfigJsonRoundTrips()
    {
        GroqProviderModule.Initialize();
        var config = new GroqProviderConfig
        {
            Temperature = 0.4,
            ResponseFormat = "json_object"
        };

        var json = ProviderContributionRegistry.SerializeProviderConfig("groq", config);
        var roundTrip = ProviderContributionRegistry.DeserializeProviderConfig("groq", json);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void CreateChatClient_WithFakeSecret_ShouldCreateClient()
    {
        var config = ValidConfig();

        using var chatClient = _provider.CreateChatClient(config, CreateServices());

        chatClient.Should().NotBeNull();
        chatClient.GetService(typeof(ChatClientMetadata))
            .Should()
            .BeOfType<ChatClientMetadata>()
            .Which.DefaultModelId.Should().Be("llama-3.3-70b-versatile");
        chatClient.GetService(typeof(GroqProviderConfig)).Should().BeNull();
    }

    [Fact]
    public void CreateChatClient_ShouldResolveGroqApiKeyAliasFromEnvironment()
    {
        GroqProviderModule.Initialize();
        global::System.Environment.SetEnvironmentVariable("GROQ_API_KEY", "env-key");

        try
        {
            var config = new ClientProviderConfig
            {
                ProviderKey = "groq",
                ModelName = "llama-3.3-70b-versatile"
            };

            using var chatClient = _provider.CreateChatClient(config, CreateServices());

            chatClient.Should().NotBeNull();
        }
        finally
        {
            global::System.Environment.SetEnvironmentVariable("GROQ_API_KEY", null);
        }
    }

    [Fact]
    public void CreateChatClient_WithConfig_ShouldExposeDefaultsAndMetadata()
    {
        var config = ValidConfig();
        var providerConfig = new GroqProviderConfig
        {
            Temperature = 0.25,
            TopP = 0.8,
            MaxOutputTokens = 123,
            Seed = 5,
            StopSequences = ["END"],
            ResponseFormat = "json_object",
            ToolChoice = "required"
        };
        config.SetProviderConfig(providerConfig);

        using var chatClient = _provider.CreateChatClient(config, CreateServices());

        chatClient.GetService(typeof(ChatClientMetadata))
            .Should()
            .BeOfType<ChatClientMetadata>()
            .Which.ProviderName.Should().Be("groq");
        chatClient.GetService(typeof(GroqProviderConfig)).Should().BeSameAs(providerConfig);
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyHttpAuthError()
    {
        var handler = new GroqErrorHandler();
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
        var handler = new GroqErrorHandler();
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
        var handler = new GroqErrorHandler();
        var exception = new InvalidOperationException("""{"error":{"message":"model not found","code":"model_not_found"}} Status: 404""");

        var details = handler.ParseError(exception);

        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(404);
        details.Category.Should().Be(ErrorCategory.ModelNotFound);
        details.ErrorCode.Should().Be("model_not_found");
    }

    private static ClientProviderConfig ValidConfig()
        => new()
        {
            ProviderKey = "groq",
            ModelName = "llama-3.3-70b-versatile",
            ApiKey = "test-key"
        };

    private static IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretResolver>(new EnvironmentSecretResolver());
        return services.BuildServiceProvider();
    }
}
#endif
