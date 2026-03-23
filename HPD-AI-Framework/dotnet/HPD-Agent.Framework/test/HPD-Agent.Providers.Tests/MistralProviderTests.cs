using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Mistral;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using Xunit;

namespace HPD.Agent.Tests.Providers;

public class MistralProviderTests
{
    private readonly MistralProvider _provider;

    public MistralProviderTests()
    {
        _provider = new MistralProvider();
    }

    #region Metadata Tests

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        // Act
        var metadata = _provider.GetMetadata();

        // Assert
        metadata.Should().NotBeNull();
        metadata.ProviderKey.Should().Be("mistral");
        metadata.DisplayName.Should().Be("Mistral");
        metadata.SupportsStreaming.Should().BeTrue();
        metadata.SupportsFunctionCalling.Should().BeTrue();
        metadata.SupportsVision.Should().BeFalse();
        metadata.DocumentationUrl.Should().Be("https://docs.mistral.ai/");
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "mistral",
            ModelName = "mistral-large-latest",
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
            ProviderKey = "mistral",
            ApiKey = "test-key"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name") && e.Contains("required"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingApiKey_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "mistral",
            ModelName = "mistral-large-latest"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("required"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "mistral",
            ModelName = "mistral-large-latest",
            ApiKey = "test-key"
        };

        var mistralConfig = new MistralProviderConfig
        {
            Temperature = 1.5m // Invalid: must be <= 1.0
        };
        config.SetTypedProviderConfig(mistralConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0.0 and 1.0"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "mistral",
            ModelName = "mistral-large-latest",
            ApiKey = "test-key"
        };

        var mistralConfig = new MistralProviderConfig
        {
            Temperature = -0.1m // Invalid: must be >= 0
        };
        config.SetTypedProviderConfig(mistralConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0.0 and 1.0"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidTemperatureRange_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "mistral",
            ModelName = "mistral-large-latest",
            ApiKey = "test-key"
        };

        var mistralConfig = new MistralProviderConfig
        {
            Temperature = 0.7m // Valid: between 0.0 and 1.0
        };
        config.SetTypedProviderConfig(mistralConfig);

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
            ProviderKey = "mistral",
            ModelName = "mistral-large-latest",
            ApiKey = "test-key"
        };

        var mistralConfig = new MistralProviderConfig
        {
            TopP = 1.5m // Invalid: must be <= 1.0
        };
        config.SetTypedProviderConfig(mistralConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopP must be between 0.0 and 1.0"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidResponseFormat_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "mistral",
            ModelName = "mistral-large-latest",
            ApiKey = "test-key"
        };

        var mistralConfig = new MistralProviderConfig
        {
            ResponseFormat = "invalid_format"
        };
        config.SetTypedProviderConfig(mistralConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ResponseFormat must be one of: text, json_object"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidToolChoice_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "mistral",
            ModelName = "mistral-large-latest",
            ApiKey = "test-key"
        };

        var mistralConfig = new MistralProviderConfig
        {
            ToolChoice = "invalid_choice"
        };
        config.SetTypedProviderConfig(mistralConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ToolChoice must be one of: auto, any, none"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidMistralConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "mistral",
            ModelName = "mistral-large-latest",
            ApiKey = "test-key"
        };

        var mistralConfig = new MistralProviderConfig
        {
            MaxTokens = 4096,
            Temperature = 0.7m,
            TopP = 0.9m,
            RandomSeed = 12345,
            ResponseFormat = "json_object",
            SafePrompt = true,
            ToolChoice = "auto",
            ParallelToolCalls = true
        };
        config.SetTypedProviderConfig(mistralConfig);

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
        handler.Should().BeOfType<MistralErrorHandler>();
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
    public void AgentConfig_WithMistralProvider_ShouldSerializeToJson()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "Test Agent",
            Provider = new ProviderConfig
            {
                ProviderKey = "mistral",
                ModelName = "mistral-large-latest",
                ApiKey = "test-key"
            }
        };

        var mistralOpts = new MistralProviderConfig
        {
            MaxTokens = 4096,
            Temperature = 0.7m,
            TopP = 0.9m,
            RandomSeed = 12345
        };
        config.Provider.SetTypedProviderConfig(mistralOpts);

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("providerKey");
        json.Should().Contain("mistral");
        json.Should().Contain("modelName");
        json.Should().Contain("mistral-large-latest");
        json.Should().Contain("providerOptionsJson");
        json.Should().Contain("maxTokens");
        json.Should().Contain("4096");
        json.Should().Contain("temperature");
        json.Should().Contain("0.7");
        json.Should().Contain("topP");
        json.Should().Contain("0.9");
        json.Should().Contain("randomSeed");
        json.Should().Contain("12345");
    }

    [Fact]
    public void AgentConfig_WithMistralProvider_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = """
        {
            "name": "Test Agent",
            "provider": {
                "providerKey": "mistral",
                "modelName": "mistral-large-latest",
                "apiKey": "test-key",
                "providerOptionsJson": "{\"maxTokens\":4096,\"temperature\":0.7,\"topP\":0.9,\"randomSeed\":12345}"
            }
        }
        """;

        // Act
        var config = System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        config.Should().NotBeNull();
        config!.Name.Should().Be("Test Agent");
        config.Provider.Should().NotBeNull();
        config.Provider!.ProviderKey.Should().Be("mistral");
        config.Provider.ModelName.Should().Be("mistral-large-latest");
        config.Provider.ApiKey.Should().Be("test-key");
        config.Provider.ProviderOptionsJson.Should().NotBeNullOrEmpty();

        // Verify typed config can be retrieved
        var mistralConfig = config.Provider.GetTypedProviderConfig<MistralProviderConfig>();
        mistralConfig.Should().NotBeNull();
        mistralConfig!.MaxTokens.Should().Be(4096);
        mistralConfig.Temperature.Should().Be(0.7m);
        mistralConfig.TopP.Should().Be(0.9m);
        mistralConfig.RandomSeed.Should().Be(12345);
    }

    [Fact]
    public void AgentConfig_WithMistralProvider_ShouldRoundTripCorrectly()
    {
        // Arrange - Create config with Mistral provider
        var originalConfig = new AgentConfig
        {
            Name = "Round Trip Test",
            MaxAgenticIterations = 20,
            SystemInstructions = "You are a test assistant.",
            Provider = new ProviderConfig
            {
                ProviderKey = "mistral",
                ModelName = "mistral-large-latest",
                ApiKey = "test-key"
            }
        };

        var originalMistralOpts = new MistralProviderConfig
        {
            MaxTokens = 4096,
            Temperature = 0.7m,
            TopP = 0.9m,
            RandomSeed = 12345,
            ResponseFormat = "json_object",
            SafePrompt = true,
            ToolChoice = "auto",
            ParallelToolCalls = true
        };
        originalConfig.Provider.SetTypedProviderConfig(originalMistralOpts);

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
        deserializedConfig.Provider!.ProviderKey.Should().Be("mistral");
        deserializedConfig.Provider.ModelName.Should().Be("mistral-large-latest");
        deserializedConfig.Provider.ApiKey.Should().Be("test-key");

        // Assert - Mistral-specific config
        var deserializedMistralOpts = deserializedConfig.Provider.GetTypedProviderConfig<MistralProviderConfig>();
        deserializedMistralOpts.Should().NotBeNull();
        deserializedMistralOpts!.MaxTokens.Should().Be(4096);
        deserializedMistralOpts.Temperature.Should().Be(0.7m);
        deserializedMistralOpts.TopP.Should().Be(0.9m);
        deserializedMistralOpts.RandomSeed.Should().Be(12345);
        deserializedMistralOpts.ResponseFormat.Should().Be("json_object");
        deserializedMistralOpts.SafePrompt.Should().BeTrue();
        deserializedMistralOpts.ToolChoice.Should().Be("auto");
        deserializedMistralOpts.ParallelToolCalls.Should().BeTrue();
    }

    [Fact]
    public void AgentConfig_SetTypedProviderConfig_ShouldUpdateProviderOptionsJson()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "mistral",
                ModelName = "mistral-large-latest"
            }
        };

        // Act - Set typed config
        var mistralOpts = new MistralProviderConfig
        {
            MaxTokens = 4096,
            Temperature = 0.5m
        };
        config.Provider.SetTypedProviderConfig(mistralOpts);

        // Assert - ProviderOptionsJson should be populated
        config.Provider.ProviderOptionsJson.Should().NotBeNullOrEmpty();
        config.Provider.ProviderOptionsJson.Should().Contain("maxTokens");
        config.Provider.ProviderOptionsJson.Should().Contain("4096");
        config.Provider.ProviderOptionsJson.Should().Contain("temperature");
        config.Provider.ProviderOptionsJson.Should().Contain("0.5");

        // Verify we can retrieve it back
        var retrieved = config.Provider.GetTypedProviderConfig<MistralProviderConfig>();
        retrieved.Should().NotBeNull();
        retrieved!.MaxTokens.Should().Be(4096);
        retrieved.Temperature.Should().Be(0.5m);
    }

    [Fact]
    public void AgentConfig_GetTypedProviderConfig_ShouldCacheResult()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "mistral",
                ModelName = "mistral-large-latest"
            }
        };

        var mistralOpts = new MistralProviderConfig
        {
            MaxTokens = 4096,
            Temperature = 0.7m
        };
        config.Provider.SetTypedProviderConfig(mistralOpts);

        // Act - Call GetTypedProviderConfig multiple times
        var first = config.Provider.GetTypedProviderConfig<MistralProviderConfig>();
        var second = config.Provider.GetTypedProviderConfig<MistralProviderConfig>();

        // Assert - Should return the same cached instance
        first.Should().BeSameAs(second);
        first!.MaxTokens.Should().Be(4096);
        first.Temperature.Should().Be(0.7m);
    }

    [Fact]
    public void AgentConfig_WithMistralProvider_ShouldHandleNullableProperties()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "mistral",
                ModelName = "mistral-large-latest"
            }
        };

        var mistralOpts = new MistralProviderConfig
        {
            MaxTokens = 4096
            // Leave Temperature, TopP, etc. as null
        };
        config.Provider.SetTypedProviderConfig(mistralOpts);

        // Act - Serialize and deserialize
        var json = System.Text.Json.JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        var deserializedOpts = deserialized!.Provider.GetTypedProviderConfig<MistralProviderConfig>();
        deserializedOpts.Should().NotBeNull();
        deserializedOpts!.MaxTokens.Should().Be(4096);
        deserializedOpts.Temperature.Should().BeNull();
        deserializedOpts.TopP.Should().BeNull();
        deserializedOpts.RandomSeed.Should().BeNull();
        deserializedOpts.SafePrompt.Should().BeNull();
    }

    #endregion

    #region AgentBuilder Extension Tests

    [Fact]
    public void WithMistral_ShouldConfigureProvider()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithMistral(
            model: "mistral-large-latest",
            apiKey: "test-api-key",
            configure: opts =>
            {
                opts.MaxTokens = 4096;
                opts.Temperature = 0.7m;
                opts.RandomSeed = 12345;
            });

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.ProviderKey.Should().Be("mistral");
        builder.Config.Provider.ModelName.Should().Be("mistral-large-latest");
        builder.Config.Provider.ApiKey.Should().Be("test-api-key");

        var mistralConfig = builder.Config.Provider.GetTypedProviderConfig<MistralProviderConfig>();
        mistralConfig.Should().NotBeNull();
        mistralConfig!.MaxTokens.Should().Be(4096);
        mistralConfig.Temperature.Should().Be(0.7m);
        mistralConfig.RandomSeed.Should().Be(12345);
    }

    [Fact]
    public void WithMistral_WithoutApiKey_ShouldStillConfigure()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act (API key will be resolved from environment)
        builder.WithMistral(
            model: "mistral-large-latest");

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.ProviderKey.Should().Be("mistral");
        builder.Config.Provider.ModelName.Should().Be("mistral-large-latest");
        // ApiKey will be null (will be resolved at runtime from environment)
        builder.Config.Provider.ApiKey.Should().BeNull();
    }

    [Fact]
    public void WithMistral_WithJsonObjectFormat_ShouldConfigureCorrectly()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithMistral(
            model: "mistral-large-latest",
            apiKey: "test-key",
            configure: opts =>
            {
                opts.ResponseFormat = "json_object";
                opts.Temperature = 0.3m;
            });

        // Assert
        var mistralConfig = builder.Config.Provider!.GetTypedProviderConfig<MistralProviderConfig>();
        mistralConfig.Should().NotBeNull();
        mistralConfig!.ResponseFormat.Should().Be("json_object");
        mistralConfig.Temperature.Should().Be(0.3m);
    }

    [Fact]
    public void WithMistral_WithClientFactory_ShouldStoreInAdditionalProperties()
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
        builder.WithMistral(
            model: "mistral-large-latest",
            apiKey: "test-key",
            clientFactory: clientFactory);

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.AdditionalProperties.Should().NotBeNull();
        builder.Config.Provider.AdditionalProperties.Should().ContainKey("ClientFactory");
        builder.Config.Provider.AdditionalProperties!["ClientFactory"].Should().BeSameAs(clientFactory);
    }

    [Fact]
    public void WithMistral_WithNullBuilder_ShouldThrowArgumentNullException()
    {
        // Arrange
        AgentBuilder? builder = null;

        // Act
        var act = () => builder!.WithMistral(
            model: "mistral-large-latest");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithMistral_WithNullModel_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithMistral(
            model: null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model*");
    }

    [Fact]
    public void WithMistral_WithEmptyModel_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithMistral(
            model: "");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model*");
    }

    [Fact]
    public void WithMistral_WithInvalidTemperature_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithMistral(
            model: "mistral-large-latest",
            configure: opts => opts.Temperature = 1.5m);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature*");
    }

    [Fact]
    public void WithMistral_WithNegativeTemperature_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithMistral(
            model: "mistral-large-latest",
            configure: opts => opts.Temperature = -0.1m);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature*");
    }

    [Fact]
    public void WithMistral_WithValidTemperatureRange_ShouldSucceed()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithMistral(
            model: "mistral-large-latest",
            apiKey: "test-key",
            configure: opts => opts.Temperature = 0.7m);

        // Assert
        var mistralConfig = builder.Config.Provider!.GetTypedProviderConfig<MistralProviderConfig>();
        mistralConfig.Should().NotBeNull();
        mistralConfig!.Temperature.Should().Be(0.7m);
    }

    [Fact]
    public void WithMistral_WithInvalidTopP_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithMistral(
            model: "mistral-large-latest",
            configure: opts => opts.TopP = 2.0m);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*TopP*");
    }

    [Fact]
    public void WithMistral_WithInvalidResponseFormat_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithMistral(
            model: "mistral-large-latest",
            configure: opts => opts.ResponseFormat = "invalid_format");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ResponseFormat*");
    }

    [Fact]
    public void WithMistral_WithInvalidToolChoice_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithMistral(
            model: "mistral-large-latest",
            configure: opts => opts.ToolChoice = "invalid");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ToolChoice*");
    }

    [Fact]
    public void WithMistral_WithAllValidOptions_ShouldConfigureCorrectly()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithMistral(
            model: "mistral-large-latest",
            apiKey: "test-key",
            configure: opts =>
            {
                opts.MaxTokens = 4096;
                opts.Temperature = 0.7m;
                opts.TopP = 0.9m;
                opts.RandomSeed = 12345;
                opts.ResponseFormat = "json_object";
                opts.SafePrompt = true;
                opts.ToolChoice = "auto";
                opts.ParallelToolCalls = true;
            });

        // Assert
        var mistralConfig = builder.Config.Provider!.GetTypedProviderConfig<MistralProviderConfig>();
        mistralConfig.Should().NotBeNull();
        mistralConfig!.MaxTokens.Should().Be(4096);
        mistralConfig.Temperature.Should().Be(0.7m);
        mistralConfig.TopP.Should().Be(0.9m);
        mistralConfig.RandomSeed.Should().Be(12345);
        mistralConfig.ResponseFormat.Should().Be("json_object");
        mistralConfig.SafePrompt.Should().BeTrue();
        mistralConfig.ToolChoice.Should().Be("auto");
        mistralConfig.ParallelToolCalls.Should().BeTrue();
    }

    #endregion
}
