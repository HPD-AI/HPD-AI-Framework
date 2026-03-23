using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.HuggingFace;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Providers;

public class HuggingFaceProviderTests
{
    private readonly HuggingFaceProvider _provider;

    public HuggingFaceProviderTests()
    {
        _provider = new HuggingFaceProvider();
    }

    #region Metadata Tests

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        // Act
        var metadata = _provider.GetMetadata();

        // Assert
        metadata.Should().NotBeNull();
        metadata.ProviderKey.Should().Be("huggingface");
        metadata.DisplayName.Should().Be("Hugging Face");
        metadata.SupportsStreaming.Should().BeTrue();
        metadata.SupportsFunctionCalling.Should().BeFalse(); // HF Inference API doesn't support function calling
        metadata.SupportsVision.Should().BeFalse();
        metadata.DocumentationUrl.Should().Be("https://huggingface.co/docs/api-inference/index");
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithMissingApiKey_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") || e.Contains("HF_TOKEN"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingModelName_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ApiKey = "hf_test_key"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name") && e.Contains("repository ID"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            Temperature = 101.0 // Invalid: must be <= 100
        };
        config.SetTypedProviderConfig(hfConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 100"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            Temperature = -0.1 // Invalid: must be >= 0
        };
        config.SetTypedProviderConfig(hfConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 100"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidTemperatureRange_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            Temperature = 1.5 // Valid for HuggingFace
        };
        config.SetTypedProviderConfig(hfConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTopP_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            TopP = -0.1 // Invalid: must be >= 0
        };
        config.SetTypedProviderConfig(hfConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopP must be between 0 and 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithTopPGreaterThanOne_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            TopP = 1.1 // Invalid: must be <= 1
        };
        config.SetTypedProviderConfig(hfConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopP must be between 0 and 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeTopK_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            TopK = -1 // Invalid: must be >= 0
        };
        config.SetTypedProviderConfig(hfConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopK must be a positive integer"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeRepetitionPenalty_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            RepetitionPenalty = -0.1 // Invalid: must be >= 0
        };
        config.SetTypedProviderConfig(hfConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("RepetitionPenalty must be a positive number"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidMaxNewTokens_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            MaxNewTokens = 0 // Invalid: must be >= 1
        };
        config.SetTypedProviderConfig(hfConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxNewTokens must be at least 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidNumReturnSequences_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            NumReturnSequences = 0 // Invalid: must be >= 1
        };
        config.SetTypedProviderConfig(hfConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NumReturnSequences must be at least 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidMaxTime_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            MaxTime = 0 // Invalid: must be > 0
        };
        config.SetTypedProviderConfig(hfConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxTime must be a positive number"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidHuggingFaceConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
            ApiKey = "hf_test_key"
        };

        var hfConfig = new HuggingFaceProviderConfig
        {
            MaxNewTokens = 500,
            Temperature = 0.7,
            TopP = 0.9,
            TopK = 50,
            RepetitionPenalty = 1.1,
            DoSample = true,
            NumReturnSequences = 1,
            ReturnFullText = false,
            MaxTime = 30.0,
            UseCache = false,
            WaitForModel = true
        };
        config.SetTypedProviderConfig(hfConfig);

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
        handler.Should().BeOfType<HuggingFaceErrorHandler>();
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
    public void ErrorHandler_GetRetryDelay_WithModelLoadingError_ShouldRetry()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            StatusCode = 503,
            Category = ErrorCategory.Transient,
            Message = "Model is loading"
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
    public void ErrorHandler_GetRetryDelay_WithModelNotFoundError_ShouldNotRetry()
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
    public void AgentConfig_WithHuggingFaceProvider_ShouldSerializeToJson()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "Test Agent",
            Provider = new ProviderConfig
            {
                ProviderKey = "huggingface",
                ModelName = "meta-llama/Meta-Llama-3-8B-Instruct",
                ApiKey = "hf_test_key"
            }
        };

        var hfOpts = new HuggingFaceProviderConfig
        {
            MaxNewTokens = 250,
            Temperature = 0.7,
            TopP = 0.9,
            TopK = 50
        };
        config.Provider.SetTypedProviderConfig(hfOpts);

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("providerKey");
        json.Should().Contain("huggingface");
        json.Should().Contain("modelName");
        json.Should().Contain("meta-llama/Meta-Llama-3-8B-Instruct");
        json.Should().Contain("providerOptionsJson");
        json.Should().Contain("maxNewTokens");
        json.Should().Contain("250");
        json.Should().Contain("temperature");
        json.Should().Contain("0.7");
        json.Should().Contain("topP");
        json.Should().Contain("0.9");
        json.Should().Contain("topK");
        json.Should().Contain("50");
    }

    [Fact]
    public void AgentConfig_WithHuggingFaceProvider_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = """
        {
            "name": "Test Agent",
            "provider": {
                "providerKey": "huggingface",
                "modelName": "meta-llama/Meta-Llama-3-8B-Instruct",
                "apiKey": "hf_test_key",
                "providerOptionsJson": "{\"maxNewTokens\":250,\"temperature\":0.7,\"topP\":0.9,\"topK\":50}"
            }
        }
        """;

        // Act
        var config = System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        config.Should().NotBeNull();
        config!.Name.Should().Be("Test Agent");
        config.Provider.Should().NotBeNull();
        config.Provider!.ProviderKey.Should().Be("huggingface");
        config.Provider.ModelName.Should().Be("meta-llama/Meta-Llama-3-8B-Instruct");
        config.Provider.ApiKey.Should().Be("hf_test_key");
        config.Provider.ProviderOptionsJson.Should().NotBeNullOrEmpty();

        // Verify typed config can be retrieved
        var hfConfig = config.Provider.GetTypedProviderConfig<HuggingFaceProviderConfig>();
        hfConfig.Should().NotBeNull();
        hfConfig!.MaxNewTokens.Should().Be(250);
        hfConfig.Temperature.Should().Be(0.7);
        hfConfig.TopP.Should().Be(0.9);
        hfConfig.TopK.Should().Be(50);
    }

    [Fact]
    public void AgentConfig_WithHuggingFaceProvider_ShouldRoundTripCorrectly()
    {
        // Arrange - Create config with HuggingFace provider
        var originalConfig = new AgentConfig
        {
            Name = "Round Trip Test",
            MaxAgenticIterations = 20,
            SystemInstructions = "You are a test assistant.",
            Provider = new ProviderConfig
            {
                ProviderKey = "huggingface",
                ModelName = "mistralai/Mistral-7B-Instruct-v0.2",
                ApiKey = "hf_test_key"
            }
        };

        var originalHfOpts = new HuggingFaceProviderConfig
        {
            MaxNewTokens = 500,
            Temperature = 0.8,
            TopP = 0.95,
            TopK = 40,
            RepetitionPenalty = 1.2,
            DoSample = true,
            NumReturnSequences = 1,
            ReturnFullText = false,
            MaxTime = 30.0,
            UseCache = false,
            WaitForModel = true
        };
        originalConfig.Provider.SetTypedProviderConfig(originalHfOpts);

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
        deserializedConfig.Provider!.ProviderKey.Should().Be("huggingface");
        deserializedConfig.Provider.ModelName.Should().Be("mistralai/Mistral-7B-Instruct-v0.2");
        deserializedConfig.Provider.ApiKey.Should().Be("hf_test_key");

        // Assert - HuggingFace-specific config
        var deserializedHfOpts = deserializedConfig.Provider.GetTypedProviderConfig<HuggingFaceProviderConfig>();
        deserializedHfOpts.Should().NotBeNull();
        deserializedHfOpts!.MaxNewTokens.Should().Be(500);
        deserializedHfOpts.Temperature.Should().Be(0.8);
        deserializedHfOpts.TopP.Should().Be(0.95);
        deserializedHfOpts.TopK.Should().Be(40);
        deserializedHfOpts.RepetitionPenalty.Should().Be(1.2);
        deserializedHfOpts.DoSample.Should().BeTrue();
        deserializedHfOpts.NumReturnSequences.Should().Be(1);
        deserializedHfOpts.ReturnFullText.Should().BeFalse();
        deserializedHfOpts.MaxTime.Should().Be(30.0);
        deserializedHfOpts.UseCache.Should().BeFalse();
        deserializedHfOpts.WaitForModel.Should().BeTrue();
    }

    [Fact]
    public void AgentConfig_SetTypedProviderConfig_ShouldUpdateProviderOptionsJson()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "huggingface",
                ModelName = "meta-llama/Meta-Llama-3-8B-Instruct"
            }
        };

        // Act - Set typed config
        var hfOpts = new HuggingFaceProviderConfig
        {
            MaxNewTokens = 250,
            Temperature = 0.5
        };
        config.Provider.SetTypedProviderConfig(hfOpts);

        // Assert - ProviderOptionsJson should be populated
        config.Provider.ProviderOptionsJson.Should().NotBeNullOrEmpty();
        config.Provider.ProviderOptionsJson.Should().Contain("maxNewTokens");
        config.Provider.ProviderOptionsJson.Should().Contain("250");
        config.Provider.ProviderOptionsJson.Should().Contain("temperature");
        config.Provider.ProviderOptionsJson.Should().Contain("0.5");

        // Verify we can retrieve it back
        var retrieved = config.Provider.GetTypedProviderConfig<HuggingFaceProviderConfig>();
        retrieved.Should().NotBeNull();
        retrieved!.MaxNewTokens.Should().Be(250);
        retrieved.Temperature.Should().Be(0.5);
    }

    [Fact]
    public void AgentConfig_GetTypedProviderConfig_ShouldCacheResult()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "huggingface",
                ModelName = "meta-llama/Meta-Llama-3-8B-Instruct"
            }
        };

        var hfOpts = new HuggingFaceProviderConfig
        {
            MaxNewTokens = 250,
            Temperature = 0.7
        };
        config.Provider.SetTypedProviderConfig(hfOpts);

        // Act - Call GetTypedProviderConfig multiple times
        var first = config.Provider.GetTypedProviderConfig<HuggingFaceProviderConfig>();
        var second = config.Provider.GetTypedProviderConfig<HuggingFaceProviderConfig>();

        // Assert - Should return the same cached instance
        first.Should().BeSameAs(second);
        first!.MaxNewTokens.Should().Be(250);
        first.Temperature.Should().Be(0.7);
    }

    [Fact]
    public void AgentConfig_WithHuggingFaceProvider_ShouldHandleNullableProperties()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "huggingface",
                ModelName = "meta-llama/Meta-Llama-3-8B-Instruct"
            }
        };

        var hfOpts = new HuggingFaceProviderConfig
        {
            MaxNewTokens = 250
            // Leave Temperature, TopP, etc. as null
        };
        config.Provider.SetTypedProviderConfig(hfOpts);

        // Act - Serialize and deserialize
        var json = System.Text.Json.JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        var deserializedOpts = deserialized!.Provider.GetTypedProviderConfig<HuggingFaceProviderConfig>();
        deserializedOpts.Should().NotBeNull();
        deserializedOpts!.MaxNewTokens.Should().Be(250);
        deserializedOpts.Temperature.Should().BeNull();
        deserializedOpts.TopP.Should().BeNull();
        deserializedOpts.TopK.Should().BeNull();
        deserializedOpts.RepetitionPenalty.Should().BeNull();
    }

    #endregion

    #region AgentBuilder Extension Tests

    [Fact]
    public void WithHuggingFace_ShouldConfigureProvider()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct",
            apiKey: "hf_test_key",
            configure: opts =>
            {
                opts.MaxNewTokens = 500;
                opts.Temperature = 0.7;
                opts.TopK = 50;
            });

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.ProviderKey.Should().Be("huggingface");
        builder.Config.Provider.ModelName.Should().Be("meta-llama/Meta-Llama-3-8B-Instruct");
        builder.Config.Provider.ApiKey.Should().Be("hf_test_key");

        var hfConfig = builder.Config.Provider.GetTypedProviderConfig<HuggingFaceProviderConfig>();
        hfConfig.Should().NotBeNull();
        hfConfig!.MaxNewTokens.Should().Be(500);
        hfConfig.Temperature.Should().Be(0.7);
        hfConfig.TopK.Should().Be(50);
    }

    [Fact]
    public void WithHuggingFace_WithoutApiKey_ShouldStillConfigure()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act - API key will be resolved from environment (HF_TOKEN)
        builder.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct");

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.ProviderKey.Should().Be("huggingface");
        builder.Config.Provider.ModelName.Should().Be("meta-llama/Meta-Llama-3-8B-Instruct");
        // ApiKey will be null here but would be resolved from env at runtime
    }

    [Fact]
    public void WithHuggingFace_WithAllOptions_ShouldConfigureCorrectly()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithHuggingFace(
            model: "bigcode/starcoder2-15b",
            apiKey: "hf_test_key",
            configure: opts =>
            {
                opts.MaxNewTokens = 1000;
                opts.Temperature = 0.8;
                opts.TopP = 0.9;
                opts.TopK = 50;
                opts.RepetitionPenalty = 1.2;
                opts.DoSample = true;
                opts.NumReturnSequences = 1;
                opts.ReturnFullText = false;
                opts.MaxTime = 30.0;
                opts.UseCache = false;
                opts.WaitForModel = true;
            });

        // Assert
        var hfConfig = builder.Config.Provider!.GetTypedProviderConfig<HuggingFaceProviderConfig>();
        hfConfig.Should().NotBeNull();
        hfConfig!.MaxNewTokens.Should().Be(1000);
        hfConfig.Temperature.Should().Be(0.8);
        hfConfig.TopP.Should().Be(0.9);
        hfConfig.TopK.Should().Be(50);
        hfConfig.RepetitionPenalty.Should().Be(1.2);
        hfConfig.DoSample.Should().BeTrue();
        hfConfig.NumReturnSequences.Should().Be(1);
        hfConfig.ReturnFullText.Should().BeFalse();
        hfConfig.MaxTime.Should().Be(30.0);
        hfConfig.UseCache.Should().BeFalse();
        hfConfig.WaitForModel.Should().BeTrue();
    }

    [Fact]
    public void WithHuggingFace_WithNullBuilder_ShouldThrowArgumentNullException()
    {
        // Arrange
        AgentBuilder? builder = null;

        // Act
        var act = () => builder!.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithHuggingFace_WithNullModel_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithHuggingFace(
            model: null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model*");
    }

    [Fact]
    public void WithHuggingFace_WithEmptyModel_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithHuggingFace(
            model: "");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model*");
    }

    [Fact]
    public void WithHuggingFace_WithInvalidTemperature_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct",
            configure: opts => opts.Temperature = 101.0);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature*");
    }

    [Fact]
    public void WithHuggingFace_WithNegativeTemperature_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct",
            configure: opts => opts.Temperature = -0.1);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature*");
    }

    [Fact]
    public void WithHuggingFace_WithInvalidTopP_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct",
            configure: opts => opts.TopP = 2.0);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*TopP*");
    }

    [Fact]
    public void WithHuggingFace_WithNegativeTopK_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct",
            configure: opts => opts.TopK = -1);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*TopK*");
    }

    [Fact]
    public void WithHuggingFace_WithNegativeRepetitionPenalty_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct",
            configure: opts => opts.RepetitionPenalty = -0.1);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*RepetitionPenalty*");
    }

    [Fact]
    public void WithHuggingFace_WithInvalidMaxNewTokens_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct",
            configure: opts => opts.MaxNewTokens = 0);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxNewTokens*");
    }

    [Fact]
    public void WithHuggingFace_WithInvalidNumReturnSequences_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct",
            configure: opts => opts.NumReturnSequences = 0);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*NumReturnSequences*");
    }

    [Fact]
    public void WithHuggingFace_WithInvalidMaxTime_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithHuggingFace(
            model: "meta-llama/Meta-Llama-3-8B-Instruct",
            configure: opts => opts.MaxTime = 0);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxTime*");
    }

    #endregion
}
