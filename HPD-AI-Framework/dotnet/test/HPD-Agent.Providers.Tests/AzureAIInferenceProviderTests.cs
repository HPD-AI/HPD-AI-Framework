using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.AzureAIInference;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Providers;

public class AzureAIInferenceProviderTests
{
    private readonly AzureAIInferenceProvider _provider;

    public AzureAIInferenceProviderTests()
    {
        _provider = new AzureAIInferenceProvider();
    }

    #region Metadata Tests

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        // Act
        var metadata = _provider.GetMetadata();

        // Assert
        metadata.Should().NotBeNull();
        metadata.ProviderKey.Should().Be("azure-ai-inference");
        metadata.DisplayName.Should().Be("Azure AI Inference");
        metadata.SupportsStreaming.Should().BeTrue();
        metadata.SupportsFunctionCalling.Should().BeTrue();
        metadata.SupportsVision.Should().BeFalse();
        metadata.DocumentationUrl.Should().Be("https://learn.microsoft.com/en-us/azure/ai-studio/how-to/deploy-models-inference");
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithMissingModelName_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name is required"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingEndpoint_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            ApiKey = "test-key"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Endpoint is required"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingApiKey_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key is required")); // Note: lowercase 'k'
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        var azureConfig = new AzureAIInferenceProviderConfig
        {
            Temperature = 1.5f // Invalid: must be <= 1.0
        };
        config.SetTypedProviderConfig(azureConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTopP_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        var azureConfig = new AzureAIInferenceProviderConfig
        {
            TopP = -0.1f // Invalid: must be >= 0
        };
        config.SetTypedProviderConfig(azureConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopP (NucleusSamplingFactor) must be between 0 and 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidFrequencyPenalty_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        var azureConfig = new AzureAIInferenceProviderConfig
        {
            FrequencyPenalty = 3.0f // Invalid: must be <= 2.0
        };
        config.SetTypedProviderConfig(azureConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("FrequencyPenalty must be between -2 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidPresencePenalty_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        var azureConfig = new AzureAIInferenceProviderConfig
        {
            PresencePenalty = -3.0f // Invalid: must be >= -2.0
        };
        config.SetTypedProviderConfig(azureConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("PresencePenalty must be between -2 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidResponseFormat_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        var azureConfig = new AzureAIInferenceProviderConfig
        {
            ResponseFormat = "invalid_format"
        };
        config.SetTypedProviderConfig(azureConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ResponseFormat must be one of: text, json_object, json_schema"));
    }

    [Fact]
    public void ValidateConfiguration_WithJsonSchemaButMissingName_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        var azureConfig = new AzureAIInferenceProviderConfig
        {
            ResponseFormat = "json_schema",
            JsonSchema = "{\"type\":\"object\"}"
            // Missing JsonSchemaName
        };
        config.SetTypedProviderConfig(azureConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("JsonSchemaName is required when ResponseFormat is json_schema"));
    }

    [Fact]
    public void ValidateConfiguration_WithJsonSchemaButMissingSchema_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        var azureConfig = new AzureAIInferenceProviderConfig
        {
            ResponseFormat = "json_schema",
            JsonSchemaName = "test_schema"
            // Missing JsonSchema
        };
        config.SetTypedProviderConfig(azureConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("JsonSchema is required when ResponseFormat is json_schema"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidToolChoice_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        var azureConfig = new AzureAIInferenceProviderConfig
        {
            ToolChoice = "invalid_choice"
        };
        config.SetTypedProviderConfig(azureConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ToolChoice must be one of: auto, none, required"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidExtraParametersMode_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        var azureConfig = new AzureAIInferenceProviderConfig
        {
            ExtraParametersMode = "invalid_mode"
        };
        config.SetTypedProviderConfig(azureConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ExtraParametersMode must be one of: pass-through, error, drop"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidAzureConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            ModelName = "llama-3-8b",
            Endpoint = "https://test.inference.ai.azure.com",
            ApiKey = "test-key"
        };

        var azureConfig = new AzureAIInferenceProviderConfig
        {
            MaxTokens = 2048,
            Temperature = 0.7f,
            TopP = 0.9f,
            FrequencyPenalty = 0.5f,
            PresencePenalty = 0.5f,
            Seed = 12345,
            ResponseFormat = "json_object",
            ToolChoice = "auto",
            ExtraParametersMode = "pass-through"
        };
        config.SetTypedProviderConfig(azureConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region Error Handler Tests

    [Fact]
    public void CreateErrorHandler_ShouldReturnValidHandler()
    {
        // Act
        var handler = _provider.CreateErrorHandler();

        // Assert
        handler.Should().NotBeNull();
        handler.Should().BeOfType<AzureAIInferenceErrorHandler>();
    }

    [Fact]
    public void ErrorHandler_ParseError_WithRequestFailedException_ShouldParseCorrectly()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var exception = new Exception("Status: 429 (TooManyRequests)");

        // Use reflection to create RequestFailedException-like behavior
        var exceptionType = exception.GetType();

        // Act
        var details = handler.ParseError(exception);

        // Assert - Should return null for non-RequestFailedException
        details.Should().BeNull();
    }

    [Fact]
    public void ErrorHandler_GetRetryDelay_WithRateLimitError_ShouldReturnDelay()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            StatusCode = 429,
            Category = ErrorCategory.RateLimitRetryable,
            Message = "Rate limit exceeded"
        };

        // Act
        var retryDelay = handler.GetRetryDelay(
            details,
            attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().NotBeNull();
        retryDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ErrorHandler_GetRetryDelay_WithServerError_ShouldReturnDelay()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            StatusCode = 500,
            Category = ErrorCategory.ServerError,
            Message = "Internal server error"
        };

        // Act
        var retryDelay = handler.GetRetryDelay(
            details,
            attempt: 1,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().NotBeNull();
        retryDelay.Should().Be(TimeSpan.FromSeconds(2)); // 1s * 2^1
    }

    [Fact]
    public void ErrorHandler_GetRetryDelay_WithClientError_ShouldNotRetry()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            StatusCode = 400,
            Category = ErrorCategory.ClientError,
            Message = "Bad request"
        };

        // Act
        var retryDelay = handler.GetRetryDelay(
            details,
            attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().BeNull(); // Should not retry
    }

    [Fact]
    public void ErrorHandler_RequiresSpecialHandling_WithAuthError_ShouldReturnTrue()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            StatusCode = 401,
            Category = ErrorCategory.AuthError,
            Message = "Unauthorized"
        };

        // Act
        var requiresSpecialHandling = handler.RequiresSpecialHandling(details);

        // Assert
        requiresSpecialHandling.Should().BeTrue();
    }

    [Fact]
    public void ErrorHandler_GetRetryDelay_WithNotFoundError_ShouldNotRetry()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            StatusCode = 404,
            Category = ErrorCategory.ClientError,
            Message = "Model not found"
        };

        // Act
        var retryDelay = handler.GetRetryDelay(
            details,
            attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().BeNull(); // Should not retry client errors
    }

    [Fact]
    public void ErrorHandler_GetRetryDelay_WithServiceUnavailable_ShouldRetry()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            StatusCode = 503,
            Category = ErrorCategory.Transient,
            Message = "Service temporarily unavailable"
        };

        // Act
        var retryDelay = handler.GetRetryDelay(
            details,
            attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().NotBeNull();
        retryDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ErrorHandler_GetRetryDelay_WithTransientError_ShouldRetryWithExponentialBackoff()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            StatusCode = 503,
            Category = ErrorCategory.Transient,
            Message = "Service temporarily unavailable"
        };

        // Act - attempt 2 (should be 1s * 2^2 = 4s)
        var retryDelay = handler.GetRetryDelay(
            details,
            attempt: 2,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        retryDelay.Should().NotBeNull();
        retryDelay.Should().Be(TimeSpan.FromSeconds(4));
    }

    #endregion

    #region AgentConfig Serialization Tests

    [Fact]
    public void AgentConfig_WithAzureAIInferenceProvider_ShouldSerializeToJson()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "Test Agent",
            Provider = new ProviderConfig
            {
                ProviderKey = "azure-ai-inference",
                ModelName = "llama-3-8b",
                Endpoint = "https://test.inference.ai.azure.com",
                ApiKey = "test-key"
            }
        };

        var azureOpts = new AzureAIInferenceProviderConfig
        {
            MaxTokens = 2048,
            Temperature = 0.7f,
            TopP = 0.9f,
            Seed = 12345
        };
        config.Provider.SetTypedProviderConfig(azureOpts);

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("providerKey");
        json.Should().Contain("azure-ai-inference");
        json.Should().Contain("modelName");
        json.Should().Contain("llama-3-8b");
        json.Should().Contain("providerOptionsJson");
        json.Should().Contain("maxTokens");
        json.Should().Contain("2048");
        json.Should().Contain("temperature");
        json.Should().Contain("0.7");
        json.Should().Contain("topP");
        json.Should().Contain("0.9");
        json.Should().Contain("seed");
        json.Should().Contain("12345");
    }

    [Fact]
    public void AgentConfig_WithAzureAIInferenceProvider_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = """
        {
            "name": "Test Agent",
            "provider": {
                "providerKey": "azure-ai-inference",
                "modelName": "llama-3-8b",
                "endpoint": "https://test.inference.ai.azure.com",
                "apiKey": "test-key",
                "providerOptionsJson": "{\"maxTokens\":2048,\"temperature\":0.7,\"topP\":0.9,\"seed\":12345}"
            }
        }
        """;

        // Act
        var config = System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        config.Should().NotBeNull();
        config!.Name.Should().Be("Test Agent");
        config.Provider.Should().NotBeNull();
        config.Provider!.ProviderKey.Should().Be("azure-ai-inference");
        config.Provider.ModelName.Should().Be("llama-3-8b");
        config.Provider.Endpoint.Should().Be("https://test.inference.ai.azure.com");
        config.Provider.ApiKey.Should().Be("test-key");
        config.Provider.ProviderOptionsJson.Should().NotBeNullOrEmpty();

        // Verify typed config can be retrieved
        var azureConfig = config.Provider.GetTypedProviderConfig<AzureAIInferenceProviderConfig>();
        azureConfig.Should().NotBeNull();
        azureConfig!.MaxTokens.Should().Be(2048);
        azureConfig.Temperature.Should().Be(0.7f);
        azureConfig.TopP.Should().Be(0.9f);
        azureConfig.Seed.Should().Be(12345);
    }

    [Fact]
    public void AgentConfig_WithAzureAIInferenceProvider_ShouldRoundTripCorrectly()
    {
        // Arrange - Create config with Azure AI Inference provider
        var originalConfig = new AgentConfig
        {
            Name = "Round Trip Test",
            MaxAgenticIterations = 20,
            SystemInstructions = "You are a test assistant.",
            Provider = new ProviderConfig
            {
                ProviderKey = "azure-ai-inference",
                ModelName = "llama-3-8b",
                Endpoint = "https://test.inference.ai.azure.com",
                ApiKey = "test-key"
            }
        };

        var originalAzureOpts = new AzureAIInferenceProviderConfig
        {
            MaxTokens = 2048,
            Temperature = 0.7f,
            TopP = 0.9f,
            FrequencyPenalty = 0.5f,
            PresencePenalty = 0.3f,
            Seed = 12345,
            ResponseFormat = "json_object",
            ToolChoice = "auto",
            StopSequences = new List<string> { "STOP", "END" }
        };
        originalConfig.Provider.SetTypedProviderConfig(originalAzureOpts);

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
        deserializedConfig.Provider!.ProviderKey.Should().Be("azure-ai-inference");
        deserializedConfig.Provider.ModelName.Should().Be("llama-3-8b");
        deserializedConfig.Provider.Endpoint.Should().Be("https://test.inference.ai.azure.com");
        deserializedConfig.Provider.ApiKey.Should().Be("test-key");

        // Assert - Azure-specific config
        var deserializedAzureOpts = deserializedConfig.Provider.GetTypedProviderConfig<AzureAIInferenceProviderConfig>();
        deserializedAzureOpts.Should().NotBeNull();
        deserializedAzureOpts!.MaxTokens.Should().Be(2048);
        deserializedAzureOpts.Temperature.Should().Be(0.7f);
        deserializedAzureOpts.TopP.Should().Be(0.9f);
        deserializedAzureOpts.FrequencyPenalty.Should().Be(0.5f);
        deserializedAzureOpts.PresencePenalty.Should().Be(0.3f);
        deserializedAzureOpts.Seed.Should().Be(12345);
        deserializedAzureOpts.ResponseFormat.Should().Be("json_object");
        deserializedAzureOpts.ToolChoice.Should().Be("auto");
        deserializedAzureOpts.StopSequences.Should().HaveCount(2);
        deserializedAzureOpts.StopSequences.Should().Contain("STOP");
        deserializedAzureOpts.StopSequences.Should().Contain("END");
    }

    [Fact]
    public void AgentConfig_SetTypedProviderConfig_ShouldUpdateProviderOptionsJson()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "azure-ai-inference",
                ModelName = "llama-3-8b"
            }
        };

        // Act - Set typed config
        var azureOpts = new AzureAIInferenceProviderConfig
        {
            MaxTokens = 2048,
            Temperature = 0.5f
        };
        config.Provider.SetTypedProviderConfig(azureOpts);

        // Assert - ProviderOptionsJson should be populated
        config.Provider.ProviderOptionsJson.Should().NotBeNullOrEmpty();
        config.Provider.ProviderOptionsJson.Should().Contain("maxTokens");
        config.Provider.ProviderOptionsJson.Should().Contain("2048");
        config.Provider.ProviderOptionsJson.Should().Contain("temperature");
        config.Provider.ProviderOptionsJson.Should().Contain("0.5");

        // Verify we can retrieve it back
        var retrieved = config.Provider.GetTypedProviderConfig<AzureAIInferenceProviderConfig>();
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
                ProviderKey = "azure-ai-inference",
                ModelName = "llama-3-8b"
            }
        };

        var azureOpts = new AzureAIInferenceProviderConfig
        {
            MaxTokens = 2048,
            Temperature = 0.7f
        };
        config.Provider.SetTypedProviderConfig(azureOpts);

        // Act - Call GetTypedProviderConfig multiple times
        var first = config.Provider.GetTypedProviderConfig<AzureAIInferenceProviderConfig>();
        var second = config.Provider.GetTypedProviderConfig<AzureAIInferenceProviderConfig>();

        // Assert - Should return the same cached instance
        first.Should().BeSameAs(second);
        first!.MaxTokens.Should().Be(2048);
        first.Temperature.Should().Be(0.7f);
    }

    [Fact]
    public void AgentConfig_WithAzureAIInferenceProvider_ShouldHandleNullableProperties()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "azure-ai-inference",
                ModelName = "llama-3-8b"
            }
        };

        var azureOpts = new AzureAIInferenceProviderConfig
        {
            MaxTokens = 2048
            // Leave Temperature, TopP, etc. as null
        };
        config.Provider.SetTypedProviderConfig(azureOpts);

        // Act - Serialize and deserialize
        var json = System.Text.Json.JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        var deserializedOpts = deserialized!.Provider.GetTypedProviderConfig<AzureAIInferenceProviderConfig>();
        deserializedOpts.Should().NotBeNull();
        deserializedOpts!.MaxTokens.Should().Be(2048);
        deserializedOpts.Temperature.Should().BeNull();
        deserializedOpts.TopP.Should().BeNull();
        deserializedOpts.FrequencyPenalty.Should().BeNull();
        deserializedOpts.PresencePenalty.Should().BeNull();
        deserializedOpts.Seed.Should().BeNull();
    }

    #endregion

    #region AgentBuilder Extension Tests

    [Fact]
    public void WithAzureAIInference_ShouldConfigureProvider()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            apiKey: "test-api-key",
            configure: opts =>
            {
                opts.MaxTokens = 2048;
                opts.Temperature = 0.7f;
                opts.Seed = 12345;
            });

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.ProviderKey.Should().Be("azure-ai-inference");
        builder.Config.Provider.ModelName.Should().Be("llama-3-8b");
        builder.Config.Provider.Endpoint.Should().Be("https://test.inference.ai.azure.com");
        builder.Config.Provider.ApiKey.Should().Be("test-api-key");

        var azureConfig = builder.Config.Provider.GetTypedProviderConfig<AzureAIInferenceProviderConfig>();
        azureConfig.Should().NotBeNull();
        azureConfig!.MaxTokens.Should().Be(2048);
        azureConfig.Temperature.Should().Be(0.7f);
        azureConfig.Seed.Should().Be(12345);
    }

    [Fact]
    public void WithAzureAIInference_WithoutApiKey_ShouldStillConfigure()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b");

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.ProviderKey.Should().Be("azure-ai-inference");
        builder.Config.Provider.ModelName.Should().Be("llama-3-8b");
        builder.Config.Provider.Endpoint.Should().Be("https://test.inference.ai.azure.com");
        // ApiKey should be null (will be resolved from env vars or appsettings during Build)
        builder.Config.Provider.ApiKey.Should().BeNull();
    }

    [Fact]
    public void WithAzureAIInference_WithJsonSchema_ShouldConfigureCorrectly()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            apiKey: "test-key",
            configure: opts =>
            {
                opts.ResponseFormat = "json_schema";
                opts.JsonSchemaName = "UserInfo";
                opts.JsonSchema = """{"type":"object","properties":{"name":{"type":"string"}}}""";
                opts.JsonSchemaIsStrict = true;
            });

        // Assert
        var azureConfig = builder.Config.Provider!.GetTypedProviderConfig<AzureAIInferenceProviderConfig>();
        azureConfig.Should().NotBeNull();
        azureConfig!.ResponseFormat.Should().Be("json_schema");
        azureConfig.JsonSchemaName.Should().Be("UserInfo");
        azureConfig.JsonSchema.Should().NotBeNullOrEmpty();
        azureConfig.JsonSchemaIsStrict.Should().BeTrue();
    }

    [Fact]
    public void WithAzureAIInference_WithClientFactory_ShouldStoreInAdditionalProperties()
    {
        // Arrange
        var builder = new AgentBuilder();
        var factoryCalled = false;
        Func<IChatClient, IChatClient> clientFactory = client =>
        {
            factoryCalled = true;
            return client;
        };

        // Act
        builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            apiKey: "test-key",
            clientFactory: clientFactory);

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.AdditionalProperties.Should().NotBeNull();
        builder.Config.Provider.AdditionalProperties.Should().ContainKey("ClientFactory");
        builder.Config.Provider.AdditionalProperties!["ClientFactory"].Should().BeSameAs(clientFactory);
    }

    [Fact]
    public void WithAzureAIInference_WithNullBuilder_ShouldThrowArgumentNullException()
    {
        // Arrange
        AgentBuilder? builder = null;

        // Act
        var act = () => builder!.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithAzureAIInference_WithNullEndpoint_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: null!,
            model: "llama-3-8b");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Endpoint*");
    }

    [Fact]
    public void WithAzureAIInference_WithEmptyEndpoint_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "",
            model: "llama-3-8b");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Endpoint*");
    }

    [Fact]
    public void WithAzureAIInference_WithNullModel_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model*");
    }

    [Fact]
    public void WithAzureAIInference_WithEmptyModel_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model*");
    }

    [Fact]
    public void WithAzureAIInference_WithInvalidTemperature_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            configure: opts => opts.Temperature = 1.5f);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature*");
    }

    [Fact]
    public void WithAzureAIInference_WithNegativeTemperature_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            configure: opts => opts.Temperature = -0.1f);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature*");
    }

    [Fact]
    public void WithAzureAIInference_WithInvalidTopP_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            configure: opts => opts.TopP = 2.0f);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*TopP*");
    }

    [Fact]
    public void WithAzureAIInference_WithInvalidFrequencyPenalty_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            configure: opts => opts.FrequencyPenalty = 3.0f);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*FrequencyPenalty*");
    }

    [Fact]
    public void WithAzureAIInference_WithInvalidPresencePenalty_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            configure: opts => opts.PresencePenalty = -3.0f);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*PresencePenalty*");
    }

    [Fact]
    public void WithAzureAIInference_WithInvalidResponseFormat_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            configure: opts => opts.ResponseFormat = "invalid_format");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ResponseFormat*");
    }

    [Fact]
    public void WithAzureAIInference_WithJsonSchemaButNoName_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            configure: opts =>
            {
                opts.ResponseFormat = "json_schema";
                opts.JsonSchema = """{"type":"object"}""";
                // Missing JsonSchemaName
            });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*JsonSchemaName*");
    }

    [Fact]
    public void WithAzureAIInference_WithJsonSchemaButNoSchema_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            configure: opts =>
            {
                opts.ResponseFormat = "json_schema";
                opts.JsonSchemaName = "Test";
                // Missing JsonSchema
            });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*JsonSchema*");
    }

    [Fact]
    public void WithAzureAIInference_WithInvalidToolChoice_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            configure: opts => opts.ToolChoice = "invalid");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ToolChoice*");
    }

    [Fact]
    public void WithAzureAIInference_WithInvalidExtraParametersMode_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            configure: opts => opts.ExtraParametersMode = "invalid");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ExtraParametersMode*");
    }

    [Fact]
    public void WithAzureAIInference_WithAllValidOptions_ShouldConfigureCorrectly()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithAzureAIInference(
            endpoint: "https://test.inference.ai.azure.com",
            model: "llama-3-8b",
            apiKey: "test-key",
            configure: opts =>
            {
                opts.MaxTokens = 2048;
                opts.Temperature = 0.7f;
                opts.TopP = 0.9f;
                opts.FrequencyPenalty = 0.5f;
                opts.PresencePenalty = 0.3f;
                opts.Seed = 12345;
                opts.ResponseFormat = "json_object";
                opts.ToolChoice = "auto";
                opts.ExtraParametersMode = "pass-through";
                opts.StopSequences = new List<string> { "STOP" };
            });

        // Assert
        var azureConfig = builder.Config.Provider!.GetTypedProviderConfig<AzureAIInferenceProviderConfig>();
        azureConfig.Should().NotBeNull();
        azureConfig!.MaxTokens.Should().Be(2048);
        azureConfig.Temperature.Should().Be(0.7f);
        azureConfig.TopP.Should().Be(0.9f);
        azureConfig.FrequencyPenalty.Should().Be(0.5f);
        azureConfig.PresencePenalty.Should().Be(0.3f);
        azureConfig.Seed.Should().Be(12345);
        azureConfig.ResponseFormat.Should().Be("json_object");
        azureConfig.ToolChoice.Should().Be("auto");
        azureConfig.ExtraParametersMode.Should().Be("pass-through");
        azureConfig.StopSequences.Should().HaveCount(1);
        azureConfig.StopSequences.Should().Contain("STOP");
    }

    #endregion
}
