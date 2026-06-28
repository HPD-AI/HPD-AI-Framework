#if NET10_0_OR_GREATER
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.DeepInfra;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Providers.Tests;

public class DeepInfraProviderTests
{
    private readonly DeepInfraProvider _provider = new();

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        var metadata = _provider.GetMetadata();

        metadata.ProviderKey.Should().Be("deepinfra");
        metadata.DisplayName.Should().Be("DeepInfra");
        metadata.DocumentationUri.Should().Be(new Uri("https://deepinfra.com/docs/openai_api"));

        var chat = metadata.Families[ProviderClientFamily.Chat];
        chat.DefaultModelId.Should().Be("meta-llama/Meta-Llama-3-8B-Instruct");
        chat.Capabilities!["SupportsStreaming"].Should().Be(true);
        chat.Capabilities["SupportsFunctionCalling"].Should().Be(true);
        chat.Capabilities["SupportsJsonResponseFormat"].Should().Be(true);
    }

    [Fact]
    public void Provider_ShouldImplementChatOnly()
    {
        _provider.Should().BeAssignableTo<IChatClientProvider>();
        _provider.Should().NotBeAssignableTo<IEmbeddingGeneratorProvider>();
    }

    [Fact]
    public void ProviderContributionRegistry_ShouldRegisterProviderConfigAndSecretAliases()
    {
        DeepInfraProviderModule.Initialize();

        ProviderContributionRegistry.GetProviderConfigType("deepinfra").Should().NotBeNull();
        SecretAliasRegistry.GetAll()["deepinfra:ApiKey"].Should().Equal("DEEPINFRA_API_KEY");
        SecretAliasRegistry.GetAll()["deepinfra:Endpoint"].Should().Equal("DEEPINFRA_ENDPOINT", "DEEPINFRA_BASE_URL");
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        var config = ValidConfig();
        config.Endpoint = "https://api.deepinfra.com/v1/openai/";
        config.SetProviderConfig(new DeepInfraProviderConfig
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
            ProviderKey = "deepinfra",
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
            ProviderKey = "deepinfra",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("DEEPINFRA_API_KEY"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidOptions_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new DeepInfraProviderConfig
        {
            Temperature = 2.1,
            TopP = -0.1,
            MaxOutputTokens = 0,
            StopSequences = [""],
            ResponseFormat = "xml",
            ToolChoice = "named"
        });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature"));
        result.Errors.Should().Contain(e => e.Contains("TopP"));
        result.Errors.Should().Contain(e => e.Contains("MaxOutputTokens"));
        result.Errors.Should().Contain(e => e.Contains("StopSequences"));
        result.Errors.Should().Contain(e => e.Contains("ResponseFormat"));
        result.Errors.Should().Contain(e => e.Contains("ToolChoice"));
    }

    [Fact]
    public void CreateChatClient_WithFakeSecret_ShouldCreateClientWithDefaultEndpointAndOptions()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new DeepInfraProviderConfig
        {
            Temperature = 0.2,
            TopP = 0.8,
            MaxOutputTokens = 512,
            Seed = 42,
            StopSequences = ["stop"],
            ResponseFormat = "json_object",
            ToolChoice = "none"
        });

        using var chatClient = _provider.CreateChatClient(config, CreateServices());

        chatClient.Should().NotBeNull();
        chatClient.GetService(typeof(ChatClientMetadata))
            .Should()
            .BeOfType<ChatClientMetadata>()
            .Which.DefaultModelId.Should().Be("meta-llama/Meta-Llama-3-8B-Instruct");

        var httpClient = chatClient.GetService(typeof(HttpClient)).Should().BeOfType<HttpClient>().Subject;
        httpClient.BaseAddress.Should().Be(new Uri("https://api.deepinfra.com/v1/openai/"));
    }

    [Fact]
    public void CreateChatClient_ShouldResolveApiKeyAndEndpointAliasesFromEnvironment()
    {
        DeepInfraProviderModule.Initialize();
        global::System.Environment.SetEnvironmentVariable("DEEPINFRA_API_KEY", "env-key");
        global::System.Environment.SetEnvironmentVariable("DEEPINFRA_ENDPOINT", "https://proxy.example/v1/openai");

        try
        {
            using var chatClient = _provider.CreateChatClient(
                new ClientProviderConfig
                {
                    ProviderKey = "deepinfra",
                    ModelName = "meta-llama/Meta-Llama-3-8B-Instruct"
                },
                CreateEnvironmentServices());

            var httpClient = chatClient.GetService(typeof(HttpClient)).Should().BeOfType<HttpClient>().Subject;
            httpClient.BaseAddress.Should().Be(new Uri("https://proxy.example/v1/openai/"));
        }
        finally
        {
            global::System.Environment.SetEnvironmentVariable("DEEPINFRA_API_KEY", null);
            global::System.Environment.SetEnvironmentVariable("DEEPINFRA_ENDPOINT", null);
        }
    }

    [Fact]
    public void WithDeepInfra_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithDeepInfra(
                model: "Qwen/Qwen2.5-72B-Instruct",
                apiKey: "test-key",
                endpoint: "https://api.deepinfra.com/v1/openai/",
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
        config!.ProviderKey.Should().Be("deepinfra");
        config.ModelName.Should().Be("Qwen/Qwen2.5-72B-Instruct");
        config.ApiKey.Should().Be("test-key");
        config.Endpoint.Should().Be("https://api.deepinfra.com/v1/openai/");

        var deepInfraConfig = config.GetProviderConfig<DeepInfraProviderConfig>();
        deepInfraConfig.Should().NotBeNull();
        deepInfraConfig!.Temperature.Should().Be(0.2);
        deepInfraConfig.TopP.Should().Be(0.8);
        deepInfraConfig.MaxOutputTokens.Should().Be(512);
        deepInfraConfig.ResponseFormat.Should().Be("json_object");
        deepInfraConfig.ToolChoice.Should().Be("required");
        deepInfraConfig.StopSequences.Should().Equal("stop");
    }

    [Fact]
    public void DeepInfraJsonContext_ShouldSerializeAndDeserializeConfig()
    {
        var config = new DeepInfraProviderConfig
        {
            Temperature = 0.3,
            TopP = 0.95,
            MaxOutputTokens = 2048,
            Seed = 12345,
            StopSequences = ["END", "STOP"],
            ResponseFormat = "json_object",
            ToolChoice = "none"
        };

        var json = JsonSerializer.Serialize(config, DeepInfraJsonContext.Default.DeepInfraProviderConfig);
        var roundTrip = JsonSerializer.Deserialize(json, DeepInfraJsonContext.Default.DeepInfraProviderConfig);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void ProviderContributionRegistry_ConfigJsonRoundTrips()
    {
        DeepInfraProviderModule.Initialize();
        var config = new DeepInfraProviderConfig
        {
            Temperature = 0.4,
            ResponseFormat = "json_object",
            ToolChoice = "auto"
        };

        var json = ProviderContributionRegistry.SerializeProviderConfig("deepinfra", config);
        var roundTrip = ProviderContributionRegistry.DeserializeProviderConfig("deepinfra", json);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyAuthError()
    {
        var handler = new DeepInfraErrorHandler();
        var exception = new HttpRequestException(
            """DeepInfra API request failed [Status: 401 Unauthorized]. Response: {"error":{"message":"Invalid API key","code":"invalid_api_key"}}""",
            null,
            HttpStatusCode.Unauthorized);

        var details = handler.ParseError(exception);

        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(401);
        details.Category.Should().Be(ErrorCategory.AuthError);
        details.ErrorCode.Should().Be("invalid_api_key");
        details.Message.Should().Be("Invalid API key");
        handler.RequiresSpecialHandling(details).Should().BeTrue();
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyRateLimitAsRetryable()
    {
        var handler = new DeepInfraErrorHandler();
        var exception = new HttpRequestException("too many requests", null, HttpStatusCode.TooManyRequests);

        var details = handler.ParseError(exception);

        details.Should().NotBeNull();
        details!.Category.Should().Be(ErrorCategory.RateLimitRetryable);
        handler.GetRetryDelay(details, 1, TimeSpan.FromMilliseconds(100), 2, TimeSpan.FromSeconds(1))
            .Should()
            .Be(TimeSpan.FromMilliseconds(200));
    }

    private static ClientProviderConfig ValidConfig()
        => new()
        {
            ProviderKey = "deepinfra",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "test-key"
        };

    private static IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretResolver>(new ExplicitSecretResolver());
        return services.BuildServiceProvider();
    }

    private static IServiceProvider CreateEnvironmentServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretResolver>(new EnvironmentSecretResolver());
        return services.BuildServiceProvider();
    }
}
#endif
