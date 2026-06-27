#if NET10_0_OR_GREATER
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Cnblogs.DashScope.Core;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.DashScope;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Providers.Tests;

public class DashScopeProviderTests
{
    private readonly DashScopeProvider _provider = new();

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        var metadata = _provider.GetMetadata();

        metadata.ProviderKey.Should().Be("dashscope");
        metadata.DisplayName.Should().Be("DashScope");
        metadata.DocumentationUri.Should().Be(new Uri("https://help.aliyun.com/zh/model-studio/"));

        var chat = metadata.Families[ProviderClientFamily.Chat];
        chat.DefaultModelId.Should().Be("qwen-plus");
        chat.Capabilities!["SupportsStreaming"].Should().Be(true);
        chat.Capabilities["SupportsFunctionCalling"].Should().Be(true);
        chat.Capabilities["SupportsReasoning"].Should().Be(true);
        chat.Capabilities["SupportsVision"].Should().Be(true);

        metadata.Families.Should().ContainKey(ProviderClientFamily.Embeddings);
        metadata.Families[ProviderClientFamily.Embeddings].DefaultModelId.Should().Be("text-embedding-v4");
    }

    [Fact]
    public void Provider_ShouldImplementChatAndEmbeddings()
    {
        _provider.Should().BeAssignableTo<IChatClientProvider>();
        _provider.Should().BeAssignableTo<IEmbeddingGeneratorProvider>();
    }

    [Fact]
    public void CreateChatClient_WithValidConfig_ShouldCreateClient()
    {
        var config = ValidConfig();

        using var chatClient = _provider.CreateChatClient(config, CreateServices());

        chatClient.Should().NotBeNull();
        chatClient.GetService(typeof(ChatClientMetadata))
            .Should()
            .BeOfType<ChatClientMetadata>()
            .Which.DefaultModelId.Should().Be("qwen-plus");
    }

    [Fact]
    public void CreateEmbeddingGenerator_WithValidConfig_ShouldCreateClient()
    {
        var config = new ClientProviderConfig
        {
            ProviderKey = "dashscope",
            ModelName = "text-embedding-v4",
            ApiKey = "test-key"
        };

        using var embeddingGenerator = _provider.CreateEmbeddingGenerator(config, CreateServices());

        embeddingGenerator.Should().NotBeNull();
        embeddingGenerator.GetService(typeof(EmbeddingGeneratorMetadata))
            .Should()
            .BeOfType<EmbeddingGeneratorMetadata>()
            .Which.DefaultModelId.Should().Be("text-embedding-v4");
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        var config = ValidConfig();
        config.Endpoint = "https://dashscope.aliyuncs.com/api/v1/";
        config.SetProviderConfig(new DashScopeProviderConfig
        {
            BaseAddress = "https://dashscope.aliyuncs.com/api/v1/",
            WebsocketBaseAddress = "wss://dashscope.aliyuncs.com/api-ws/v1/inference/",
            WorkspaceId = "workspace",
            UseVl = false,
            Temperature = 0.7f,
            TopP = 0.9f,
            TopK = 50,
            MaxOutputTokens = 1024,
            Seed = 123,
            StopSequences = ["END"],
            EmbeddingModelId = "text-embedding-v4",
            EmbeddingDimensions = 1024
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
            ProviderKey = "dashscope",
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
            ProviderKey = "dashscope",
            ModelName = "qwen-plus"
        };

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("DASHSCOPE_API_KEY"));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(2.1f)]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail(float temperature)
    {
        var config = ValidConfig();
        config.SetProviderConfig(new DashScopeProviderConfig { Temperature = temperature });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidEndpoint_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new DashScopeProviderConfig { BaseAddress = "not a uri" });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("BaseAddress must be a valid"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidEmbeddingDimensions_ShouldFail()
    {
        var config = ValidConfig();
        config.SetProviderConfig(new DashScopeProviderConfig { EmbeddingDimensions = 0 });

        var result = _provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("EmbeddingDimensions must be greater than 0"));
    }

    [Fact]
    public void WithDashScope_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithDashScope(
                model: "qwen-plus",
                apiKey: "test-key",
                endpoint: "https://dashscope.aliyuncs.com/api/v1/",
                configure: options =>
                {
                    options.Temperature = 0.2f;
                    options.TopP = 0.8f;
                    options.MaxOutputTokens = 512;
                    options.WorkspaceId = "workspace";
                    options.StopSequences = ["stop"];
                });

        var config = builder.Config.Clients?.Chat;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("dashscope");
        config.ModelName.Should().Be("qwen-plus");
        config.ApiKey.Should().Be("test-key");
        config.Endpoint.Should().Be("https://dashscope.aliyuncs.com/api/v1/");

        var dashScopeConfig = config.GetProviderConfig<DashScopeProviderConfig>();
        dashScopeConfig.Should().NotBeNull();
        dashScopeConfig!.Temperature.Should().Be(0.2f);
        dashScopeConfig.TopP.Should().Be(0.8f);
        dashScopeConfig.MaxOutputTokens.Should().Be(512);
        dashScopeConfig.WorkspaceId.Should().Be("workspace");
        dashScopeConfig.StopSequences.Should().Equal("stop");
    }

    [Fact]
    public void WithDashScope_WithInvalidModel_ShouldThrow()
    {
        var builder = new AgentBuilder();

        var act = () => builder.WithDashScope(model: " ");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model is required*");
    }

    [Fact]
    public void WithDashScope_WithInvalidOptions_ShouldThrow()
    {
        var builder = new AgentBuilder();

        var act = () => builder.WithDashScope(configure: options => options.TopK = 0);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*TopK must be greater than 0*");
    }

    [Fact]
    public void WithDashScopeEmbeddings_ShouldConfigureBuilder()
    {
        var builder = new AgentBuilder()
            .WithDashScopeEmbeddings(
                model: "text-embedding-v4",
                apiKey: "test-key",
                configure: options => options.EmbeddingDimensions = 1024);

        var config = builder.Config.Clients?.Embeddings;

        config.Should().NotBeNull();
        config!.ProviderKey.Should().Be("dashscope");
        config.ModelName.Should().Be("text-embedding-v4");
        config.ApiKey.Should().Be("test-key");

        var dashScopeConfig = config.GetProviderConfig<DashScopeProviderConfig>(ProviderClientFamily.Embeddings);
        dashScopeConfig.Should().NotBeNull();
        dashScopeConfig!.EmbeddingModelId.Should().Be("text-embedding-v4");
        dashScopeConfig.EmbeddingDimensions.Should().Be(1024);
        config.GetProviderOptionsRawJson().Should().Contain("text-embedding-v4");
    }

    [Fact]
    public void DashScopeJsonContext_ShouldSerializeAndDeserializeConfig()
    {
        var config = new DashScopeProviderConfig
        {
            BaseAddress = "https://dashscope.aliyuncs.com/api/v1/",
            WebsocketBaseAddress = "wss://dashscope.aliyuncs.com/api-ws/v1/inference/",
            WorkspaceId = "workspace",
            SocketPoolSize = 16,
            TimeoutSeconds = 30,
            UseVl = true,
            Temperature = 0.3f,
            TopP = 0.95f,
            TopK = 40,
            MaxOutputTokens = 2048,
            Seed = 12345,
            StopSequences = ["END", "STOP"],
            EmbeddingModelId = "text-embedding-v4",
            EmbeddingDimensions = 1024
        };

        var json = JsonSerializer.Serialize(config, DashScopeJsonContext.Default.DashScopeProviderConfig);
        var roundTrip = JsonSerializer.Deserialize(json, DashScopeJsonContext.Default.DashScopeProviderConfig);

        roundTrip.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyDashScopeAuthError()
    {
        var handler = new DashScopeErrorHandler();
        var exception = new DashScopeException(
            "https://dashscope.aliyuncs.com/api/v1/services/aigc/text-generation/generation",
            401,
            new DashScopeError
            {
                Code = "InvalidApiKey",
                Message = "Invalid API key",
                RequestId = "request-id"
            },
            "Invalid API key");

        var details = handler.ParseError(exception);

        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(401);
        details.Category.Should().Be(ErrorCategory.AuthError);
        details.ErrorCode.Should().Be("InvalidApiKey");
        details.RequestId.Should().Be("request-id");
        handler.RequiresSpecialHandling(details).Should().BeTrue();
    }

    [Fact]
    public void ErrorHandler_ShouldClassifyRateLimitAsRetryable()
    {
        var handler = new DashScopeErrorHandler();
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
            ProviderKey = "dashscope",
            ModelName = "qwen-plus",
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
