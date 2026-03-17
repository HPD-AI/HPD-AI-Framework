using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers.Anthropic;
using Microsoft.Extensions.AI;
using Xunit;
using System.Net;
using Anthropic.Exceptions;

namespace HPD.Agent.Providers.Tests;

public class AnthropicProviderTests
{
    [Fact]
    public void WithAnthropic_ShouldConfigureProvider()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithAnthropic("claude-sonnet-4-5-20250929", apiKey: "test-api-key", opts =>
        {
            opts.MaxTokens = 2048;
            opts.Temperature = 0.5;
            opts.ThinkingBudgetTokens = 4096;
            opts.ServiceTier = "auto";
        });

        // Assert
        var config = builder.Config;
        config.Provider.Should().NotBeNull();
        config.Provider.ProviderKey.Should().Be("anthropic");
        config.Provider.ApiKey.Should().Be("test-api-key");
        config.Provider.ModelName.Should().Be("claude-sonnet-4-5-20250929");

        // Verify typed config
        var providerConfig = config.Provider.GetTypedProviderConfig<AnthropicProviderConfig>();
        providerConfig.Should().NotBeNull();
        providerConfig.MaxTokens.Should().Be(2048);
        providerConfig.Temperature.Should().Be(0.5);
        providerConfig.ThinkingBudgetTokens.Should().Be(4096);
        providerConfig.ServiceTier.Should().Be("auto");
    }

    [Fact]
    public void WithAnthropic_ShouldUseDefaults()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithAnthropic("claude-sonnet-4-5-20250929", apiKey: "test-api-key");

        // Assert
        var providerConfig = builder.Config.Provider.GetTypedProviderConfig<AnthropicProviderConfig>();
        providerConfig.Should().NotBeNull();
        providerConfig.MaxTokens.Should().Be(4096); // Default
        providerConfig.Temperature.Should().BeNull();
        providerConfig.ThinkingBudgetTokens.Should().BeNull();
    }

    [Fact]
    public void WithAnthropic_ShouldThrowWhenModelIsEmpty()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act & Assert
        var act = () => builder.WithAnthropic("", apiKey: "test-api-key");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model*");
    }

    [Fact]
    public async Task WithAnthropic_ShouldResolveFromEnvironmentVariable()
    {
        // Arrange
        var builder = new AgentBuilder();
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "env-api-key");

        try
        {
            // Act
            builder.WithAnthropic("claude-sonnet-4-5-20250929"); // No explicit API key

            // API key resolution happens at Build() time via ISecretResolver
            var agent = await builder.BuildAsync();

            // Assert - verify the provider was created successfully with the resolved key
            // (If the key wasn't resolved, Build() would have thrown an exception)
            agent.Should().NotBeNull();
            agent.BaseClient.Should().NotBeNull();
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        }
    }

    [Fact]
    public void WithAnthropic_ShouldPreferExplicitApiKeyOverEnvironment()
    {
        // Arrange
        var builder = new AgentBuilder();
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "env-api-key");

        try
        {
            // Act
            builder.WithAnthropic("claude-sonnet-4-5-20250929", apiKey: "explicit-key");

            // Assert
            builder.Config.Provider.ApiKey.Should().Be("explicit-key");
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        }
    }

    [Fact]
    public void AnthropicProviderConfig_ShouldBeSerializable()
    {
        // Arrange
        var config = new AnthropicProviderConfig
        {
            MaxTokens = 2048,
            Temperature = 0.7,
            TopP = 0.9,
            TopK = 40,
            StopSequences = new List<string> { "STOP" },
            ThinkingBudgetTokens = 4096,
            ServiceTier = "auto"
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(config, AnthropicJsonContext.Default.AnthropicProviderConfig);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("maxTokens");
        json.Should().Contain("2048");
        json.Should().Contain("temperature");
        json.Should().Contain("thinkingBudgetTokens");
        json.Should().Contain("serviceTier");
    }

    [Fact]
    public void AnthropicProviderConfig_ShouldBeDeserializable()
    {
        // Arrange
        var json = """
        {
            "maxTokens": 2048,
            "temperature": 0.7,
            "topP": 0.9,
            "topK": 40,
            "stopSequences": ["STOP"],
            "thinkingBudgetTokens": 4096,
            "serviceTier": "auto"
        }
        """;

        // Act
        var config = System.Text.Json.JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicProviderConfig);

        // Assert
        config.Should().NotBeNull();
        config!.MaxTokens.Should().Be(2048);
        config.Temperature.Should().Be(0.7);
        config.TopP.Should().Be(0.9);
        config.TopK.Should().Be(40);
        config.StopSequences.Should().ContainSingle("STOP");
        config.ThinkingBudgetTokens.Should().Be(4096);
        config.ServiceTier.Should().Be("auto");
    }

    [Fact]
    public void WithAnthropic_ShouldConfigurePromptCaching()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithAnthropic("claude-sonnet-4-5-20250929", apiKey: "test-api-key", opts =>
        {
            opts.EnablePromptCaching = true;
            opts.PromptCacheTTLMinutes = 10;
        });

        // Assert
        var providerConfig = builder.Config.Provider.GetTypedProviderConfig<AnthropicProviderConfig>();
        providerConfig.Should().NotBeNull();
        providerConfig!.EnablePromptCaching.Should().BeTrue();
        providerConfig.PromptCacheTTLMinutes.Should().Be(10);
    }

    [Fact]
    public void WithAnthropic_ShouldThrowWhenPromptCacheTTLInvalid()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act & Assert - TTL too low
        var actLow = () => builder.WithAnthropic("claude-sonnet-4-5-20250929", apiKey: "test-api-key", opts =>
        {
            opts.EnablePromptCaching = true;
            opts.PromptCacheTTLMinutes = 0;
        });
        actLow.Should().Throw<ArgumentException>()
            .WithMessage("*PromptCacheTTLMinutes*1 and 60*");

        // Act & Assert - TTL too high
        var actHigh = () => builder.WithAnthropic("claude-sonnet-4-5-20250929", apiKey: "test-api-key", opts =>
        {
            opts.EnablePromptCaching = true;
            opts.PromptCacheTTLMinutes = 61;
        });
        actHigh.Should().Throw<ArgumentException>()
            .WithMessage("*PromptCacheTTLMinutes*1 and 60*");
    }

    [Fact]
    public void WithAnthropic_ShouldAcceptClientFactory()
    {
        // Arrange
        var builder = new AgentBuilder();
        var factoryCalled = false;
        Func<IChatClient, IChatClient> clientFactory = client =>
        {
            factoryCalled = true;
            return client; // Just return the same client for testing
        };

        // Act
        builder.WithAnthropic("claude-sonnet-4-5-20250929",
            apiKey: "test-api-key",
            clientFactory: clientFactory);

        // Assert
        builder.Config.Provider.AdditionalProperties.Should().ContainKey("ClientFactory");
        builder.Config.Provider.AdditionalProperties!["ClientFactory"].Should().Be(clientFactory);
    }

    #region Error Handler Tests

    [Fact]
    public void AnthropicErrorHandler_ShouldParseInsufficientCreditsError()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var responseBody = """
        {"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to upgrade or purchase credits."},"request_id":"req_011CWvoj7eE1PS1hfGEZhE4j"}
        """;

        var exception = new AnthropicBadRequestException
        {
            StatusCode = HttpStatusCode.BadRequest,
            ResponseBody = responseBody
        };

        // Act
        var details = handler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(400);
        details.Category.Should().Be(ErrorCategory.RateLimitTerminal); // Don't retry
        details.ErrorType.Should().Be("invalid_request_error");
        details.Message.Should().Contain("credit balance is too low");
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldParseRateLimitError()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var responseBody = """
        {"type":"error","error":{"type":"rate_limit_error","message":"Rate limit exceeded. Please retry after 5 seconds."},"request_id":"req_012345"}
        """;

        var exception = new AnthropicRateLimitException
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            ResponseBody = responseBody
        };

        // Act
        var details = handler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(429);
        details.Category.Should().Be(ErrorCategory.RateLimitRetryable); // Should retry
        details.ErrorType.Should().Be("rate_limit_error");
        details.RetryAfter.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldParseAuthenticationError()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var responseBody = """
        {"type":"error","error":{"type":"authentication_error","message":"Invalid API key"},"request_id":"req_012345"}
        """;

        var exception = new AnthropicUnauthorizedException
        {
            StatusCode = HttpStatusCode.Unauthorized,
            ResponseBody = responseBody
        };

        // Act
        var details = handler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(401);
        details.Category.Should().Be(ErrorCategory.AuthError);
        details.ErrorType.Should().Be("authentication_error");
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldParseContextLengthError()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var responseBody = """
        {"type":"error","error":{"type":"invalid_request_error","message":"Your prompt is too long. The maximum context length is 200000 tokens."},"request_id":"req_012345"}
        """;

        var exception = new AnthropicBadRequestException
        {
            StatusCode = HttpStatusCode.BadRequest,
            ResponseBody = responseBody
        };

        // Act
        var details = handler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(400);
        details.Category.Should().Be(ErrorCategory.ContextWindow);
        details.ErrorCode.Should().Be("context_length_exceeded");
        details.Message.Should().Contain("prompt is too long");
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldNotRetryTerminalErrors()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.RateLimitTerminal,
            StatusCode = 400
        };

        // Act
        var retryDelay = handler.GetRetryDelay(details, attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().BeNull(); // Should not retry
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldRetryServerErrors()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.ServerError,
            StatusCode = 500
        };

        // Act
        var retryDelay = handler.GetRetryDelay(details, attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().NotBeNull();
        retryDelay.Should().Be(TimeSpan.FromSeconds(1)); // First attempt
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldRespectRetryAfterHeader()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.RateLimitRetryable,
            StatusCode = 429,
            RetryAfter = TimeSpan.FromSeconds(10)
        };

        // Act
        var retryDelay = handler.GetRetryDelay(details, attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().Be(TimeSpan.FromSeconds(10)); // Uses provider's delay
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldParseNetworkIOException()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var exception = new AnthropicIOException("Connection timeout occurred");

        // Act
        var details = handler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().BeNull();
        details.Category.Should().Be(ErrorCategory.Transient); // Should retry
        details.ErrorCode.Should().Be("network_error");
        details.Message.Should().Contain("Connection timeout");
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldParseStreamingException()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var exception = new AnthropicSseException("Failed to parse SSE stream");

        // Act
        var details = handler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().BeNull();
        details.Category.Should().Be(ErrorCategory.Transient); // Should retry
        details.ErrorCode.Should().Be("streaming_error");
        details.Message.Should().Contain("SSE stream");
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldParseInvalidDataException()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var exception = new AnthropicInvalidDataException("Unable to deserialize response");

        // Act
        var details = handler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().BeNull();
        details.Category.Should().Be(ErrorCategory.ClientError); // Don't retry
        details.ErrorCode.Should().Be("invalid_data");
        details.Message.Should().Contain("deserialize");
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldRetryNetworkErrors()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.Transient,
            ErrorCode = "network_error"
        };

        // Act
        var retryDelay = handler.GetRetryDelay(details, attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().NotBeNull();
        retryDelay.Should().Be(TimeSpan.FromSeconds(1)); // First attempt
    }

    [Fact]
    public void AnthropicErrorHandler_ShouldNotRetryInvalidData()
    {
        // Arrange
        var handler = new AnthropicErrorHandler();
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.ClientError,
            ErrorCode = "invalid_data"
        };

        // Act
        var retryDelay = handler.GetRetryDelay(details, attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().BeNull(); // Should not retry
    }

    #endregion

    #region AgentConfig Serialization Tests

    [Fact]
    public void AgentConfig_WithAnthropicProvider_ShouldSerializeToJson()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "Test Agent",
            Provider = new ProviderConfig
            {
                ProviderKey = "anthropic",
                ModelName = "claude-sonnet-4-5",
                ApiKey = "sk-ant-test-key"
            }
        };

        var anthropicOpts = new AnthropicProviderConfig
        {
            MaxTokens = 4096,
            EnablePromptCaching = true,
            Temperature = 1.0f,
            ThinkingBudgetTokens = 2048
        };
        config.Provider.SetTypedProviderConfig(anthropicOpts);

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("providerKey");
        json.Should().Contain("anthropic");
        json.Should().Contain("modelName");
        json.Should().Contain("claude-sonnet-4-5");
        json.Should().Contain("providerOptionsJson");
        json.Should().Contain("maxTokens");
        json.Should().Contain("4096");
        json.Should().Contain("enablePromptCaching");
        json.Should().Contain("true");
        json.Should().Contain("temperature");
        json.Should().Contain("thinkingBudgetTokens");
        json.Should().Contain("2048");
    }

    [Fact]
    public void AgentConfig_WithAnthropicProvider_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = """
        {
            "name": "Test Agent",
            "provider": {
                "providerKey": "anthropic",
                "modelName": "claude-sonnet-4-5",
                "apiKey": "sk-ant-test-key",
                "providerOptionsJson": "{\"maxTokens\":4096,\"enablePromptCaching\":true,\"temperature\":1.0,\"thinkingBudgetTokens\":2048}"
            }
        }
        """;

        // Act
        var config = System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        config.Should().NotBeNull();
        config!.Name.Should().Be("Test Agent");
        config.Provider.Should().NotBeNull();
        config.Provider!.ProviderKey.Should().Be("anthropic");
        config.Provider.ModelName.Should().Be("claude-sonnet-4-5");
        config.Provider.ApiKey.Should().Be("sk-ant-test-key");
        config.Provider.ProviderOptionsJson.Should().NotBeNullOrEmpty();

        // Verify typed config can be retrieved
        var anthropicConfig = config.Provider.GetTypedProviderConfig<AnthropicProviderConfig>();
        anthropicConfig.Should().NotBeNull();
        anthropicConfig!.MaxTokens.Should().Be(4096);
        anthropicConfig.EnablePromptCaching.Should().BeTrue();
        anthropicConfig.Temperature.Should().Be(1.0f);
        anthropicConfig.ThinkingBudgetTokens.Should().Be(2048);
    }

    [Fact]
    public void AgentConfig_WithAnthropicProvider_ShouldRoundTripCorrectly()
    {
        // Arrange - Create config with Anthropic provider
        var originalConfig = new AgentConfig
        {
            Name = "Round Trip Test",
            MaxAgenticIterations = 20,
            SystemInstructions = "You are a test assistant.",
            Provider = new ProviderConfig
            {
                ProviderKey = "anthropic",
                ModelName = "claude-sonnet-4-5-20250929",
                ApiKey = "sk-ant-original-key",
                Endpoint = "https://api.anthropic.com"
            }
        };

        var originalAnthropicOpts = new AnthropicProviderConfig
        {
            MaxTokens = 8192,
            EnablePromptCaching = true,
            PromptCacheTTLMinutes = 15,
            Temperature = 0.7f,
            TopP = 0.95f,
            TopK = 50,
            ThinkingBudgetTokens = 4096,
            ServiceTier = "auto",
            StopSequences = new List<string> { "STOP", "END" }
        };
        originalConfig.Provider.SetTypedProviderConfig(originalAnthropicOpts);

        // Act - Serialize and deserialize
        var json = System.Text.Json.JsonSerializer.Serialize(originalConfig, HPDJsonContext.Default.AgentConfig);
        var deserializedConfig = System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig);

        // Assert - Basic config properties
        deserializedConfig.Should().NotBeNull();
        deserializedConfig!.Name.Should().Be("Round Trip Test");
        deserializedConfig.MaxAgenticIterations.Should().Be(20);
        deserializedConfig.SystemInstructions.Should().Be("You are a test assistant.");

        // Assert - Provider properties
        deserializedConfig.Provider.Should().NotBeNull();
        deserializedConfig.Provider!.ProviderKey.Should().Be("anthropic");
        deserializedConfig.Provider.ModelName.Should().Be("claude-sonnet-4-5-20250929");
        deserializedConfig.Provider.ApiKey.Should().Be("sk-ant-original-key");
        deserializedConfig.Provider.Endpoint.Should().Be("https://api.anthropic.com");

        // Assert - Anthropic-specific config
        var deserializedAnthropicOpts = deserializedConfig.Provider.GetTypedProviderConfig<AnthropicProviderConfig>();
        deserializedAnthropicOpts.Should().NotBeNull();
        deserializedAnthropicOpts!.MaxTokens.Should().Be(8192);
        deserializedAnthropicOpts.EnablePromptCaching.Should().BeTrue();
        deserializedAnthropicOpts.PromptCacheTTLMinutes.Should().Be(15);
        deserializedAnthropicOpts.Temperature.Should().Be(0.7f);
        deserializedAnthropicOpts.TopP.Should().Be(0.95f);
        deserializedAnthropicOpts.TopK.Should().Be(50);
        deserializedAnthropicOpts.ThinkingBudgetTokens.Should().Be(4096);
        deserializedAnthropicOpts.ServiceTier.Should().Be("auto");
        deserializedAnthropicOpts.StopSequences.Should().HaveCount(2);
        deserializedAnthropicOpts.StopSequences.Should().Contain("STOP");
        deserializedAnthropicOpts.StopSequences.Should().Contain("END");
    }

    [Fact]
    public void AgentConfig_SetTypedProviderConfig_ShouldUpdateProviderOptionsJson()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "anthropic",
                ModelName = "claude-sonnet-4-5"
            }
        };

        // Act - Set typed config
        var anthropicOpts = new AnthropicProviderConfig
        {
            MaxTokens = 2048,
            Temperature = 0.5f
        };
        config.Provider.SetTypedProviderConfig(anthropicOpts);

        // Assert - ProviderOptionsJson should be populated
        config.Provider.ProviderOptionsJson.Should().NotBeNullOrEmpty();
        config.Provider.ProviderOptionsJson.Should().Contain("maxTokens");
        config.Provider.ProviderOptionsJson.Should().Contain("2048");
        config.Provider.ProviderOptionsJson.Should().Contain("temperature");
        config.Provider.ProviderOptionsJson.Should().Contain("0.5");

        // Verify we can retrieve it back
        var retrieved = config.Provider.GetTypedProviderConfig<AnthropicProviderConfig>();
        retrieved.Should().NotBeNull();
        retrieved!.MaxTokens.Should().Be(2048);
        retrieved.Temperature.Should().Be(0.5f);
    }

    [Fact]
    public void AgentConfig_GetTypedProviderConfig_ShouldCacheResult()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "anthropic",
                ModelName = "claude-sonnet-4-5"
            }
        };

        var anthropicOpts = new AnthropicProviderConfig
        {
            MaxTokens = 4096,
            Temperature = 1.0f
        };
        config.Provider.SetTypedProviderConfig(anthropicOpts);

        // Act - Call GetTypedProviderConfig multiple times
        var first = config.Provider.GetTypedProviderConfig<AnthropicProviderConfig>();
        var second = config.Provider.GetTypedProviderConfig<AnthropicProviderConfig>();

        // Assert - Should return the same cached instance
        first.Should().BeSameAs(second);
        first!.MaxTokens.Should().Be(4096);
        first.Temperature.Should().Be(1.0f);
    }

    [Fact]
    public void AgentConfig_WithAnthropicProvider_ShouldHandleNullableProperties()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "anthropic",
                ModelName = "claude-sonnet-4-5"
            }
        };

        var anthropicOpts = new AnthropicProviderConfig
        {
            MaxTokens = 4096,
            // Leave Temperature, TopP, etc. as null
            EnablePromptCaching = false
        };
        config.Provider.SetTypedProviderConfig(anthropicOpts);

        // Act - Serialize and deserialize
        var json = System.Text.Json.JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        var deserializedOpts = deserialized!.Provider.GetTypedProviderConfig<AnthropicProviderConfig>();
        deserializedOpts.Should().NotBeNull();
        deserializedOpts!.MaxTokens.Should().Be(4096);
        deserializedOpts.Temperature.Should().BeNull();
        deserializedOpts.TopP.Should().BeNull();
        deserializedOpts.TopK.Should().BeNull();
        deserializedOpts.ThinkingBudgetTokens.Should().BeNull();
        deserializedOpts.PromptCacheTTLMinutes.Should().BeNull();
        deserializedOpts.EnablePromptCaching.Should().BeFalse();
    }

    #endregion
}
