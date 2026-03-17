using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Bedrock;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Providers;

public class BedrockProviderTests
{
    private readonly BedrockProvider _provider;

    public BedrockProviderTests()
    {
        _provider = new BedrockProvider();
    }

    #region Metadata Tests

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        // Act
        var metadata = _provider.GetMetadata();

        // Assert
        metadata.Should().NotBeNull();
        metadata.ProviderKey.Should().Be("bedrock");
        metadata.DisplayName.Should().Be("AWS Bedrock");
        metadata.SupportsStreaming.Should().BeTrue();
        metadata.SupportsFunctionCalling.Should().BeTrue();
        metadata.SupportsVision.Should().BeTrue();
        metadata.DocumentationUrl.Should().Be("https://aws.amazon.com/bedrock/");
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void ValidateConfiguration_WithValidConfigAndEnvVar_ShouldSucceed()
    {
        // Arrange
        Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
        try
        {
            var config = new ProviderConfig
            {
                ProviderKey = "bedrock",
                ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
            };

            // Act
            var result = _provider.ValidateConfiguration(config);

            // Assert
            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_REGION", null);
        }
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfigInTypedConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1"
        };
        config.SetTypedProviderConfig(bedrockConfig);

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
            ProviderKey = "bedrock"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1"
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name") && e.Contains("model ID"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingRegion_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("AWS Region is required"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            Temperature = 1.5f // Invalid: must be <= 1.0 for Bedrock
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            Temperature = -0.1f // Invalid: must be >= 0
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidTemperatureRange_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            Temperature = 0.7f // Valid for Bedrock (0-1)
        };
        config.SetTypedProviderConfig(bedrockConfig);

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
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            TopP = 1.5f // Invalid: must be <= 1.0
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopP must be between 0 and 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidMaxTokens_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            MaxTokens = 0 // Invalid: must be >= 1
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxTokens must be at least 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithTooManyStopSequences_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            StopSequences = Enumerable.Range(0, 2501).Select(i => $"stop{i}").ToList() // Invalid: max 2500
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("StopSequences cannot exceed 2500 items"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidToolChoice_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            ToolChoice = "invalid_choice"
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ToolChoice must be one of: auto, any, tool"));
    }

    [Fact]
    public void ValidateConfiguration_WithToolChoiceToolButMissingName_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            ToolChoice = "tool"
            // Missing ToolChoiceName
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ToolChoiceName is required when ToolChoice is 'tool'"));
    }

    [Fact]
    public void ValidateConfiguration_WithGuardrailButMissingVersion_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            GuardrailIdentifier = "my-guardrail"
            // Missing GuardrailVersion
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("GuardrailVersion is required when GuardrailIdentifier is specified"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidGuardrailTrace_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            GuardrailTrace = "invalid"
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("GuardrailTrace must be either 'enabled' or 'disabled'"));
    }

    [Fact]
    public void ValidateConfiguration_WithAccessKeyButMissingSecret_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            AccessKeyId = "AKIAIOSFODNN7EXAMPLE"
            // Missing SecretAccessKey
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("SecretAccessKey is required when AccessKeyId is specified"));
    }

    [Fact]
    public void ValidateConfiguration_WithSecretButMissingAccessKey_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
            // Missing AccessKeyId
        };
        config.SetTypedProviderConfig(bedrockConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("AccessKeyId is required when SecretAccessKey is specified"));
    }

    [Fact]
    public void ValidateConfiguration_WithAllValidOptions_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
        };

        var bedrockConfig = new BedrockProviderConfig
        {
            Region = "us-east-1",
            MaxTokens = 4096,
            Temperature = 0.7f,
            TopP = 0.9f,
            StopSequences = new List<string> { "STOP", "END" },
            ToolChoice = "auto",
            GuardrailIdentifier = "my-guardrail",
            GuardrailVersion = "1",
            GuardrailTrace = "enabled",
            AccessKeyId = "AKIAIOSFODNN7EXAMPLE",
            SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
        };
        config.SetTypedProviderConfig(bedrockConfig);

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
        handler.Should().BeOfType<BedrockErrorHandler>();
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
            Message = "ThrottlingException: Rate limit exceeded"
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
            Message = "InternalServerException"
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
    public void ErrorHandler_GetRetryDelay_WithTransientError_ShouldRetry()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            StatusCode = 503,
            Category = ErrorCategory.Transient,
            Message = "ServiceUnavailableException"
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
    public void ErrorHandler_GetRetryDelay_WithClientError_ShouldNotRetry()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            StatusCode = 400,
            Category = ErrorCategory.ClientError,
            Message = "ValidationException: Invalid request"
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
            StatusCode = 403,
            Category = ErrorCategory.AuthError,
            Message = "AccessDeniedException"
        };

        // Act
        var requiresSpecialHandling = handler.RequiresSpecialHandling(details);

        // Assert
        requiresSpecialHandling.Should().BeTrue();
    }

    [Fact]
    public void ErrorHandler_GetRetryDelay_WithExponentialBackoff_ShouldIncreaseDelay()
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
    public void AgentConfig_WithBedrockProvider_ShouldSerializeToJson()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "Test Agent",
            Provider = new ProviderConfig
            {
                ProviderKey = "bedrock",
                ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
            }
        };

        var bedrockOpts = new BedrockProviderConfig
        {
            Region = "us-east-1",
            MaxTokens = 4096,
            Temperature = 0.7f,
            TopP = 0.9f
        };
        config.Provider.SetTypedProviderConfig(bedrockOpts);

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("providerKey");
        json.Should().Contain("bedrock");
        json.Should().Contain("modelName");
        json.Should().Contain("anthropic.claude-3-5-sonnet");
        json.Should().Contain("providerOptionsJson");
        json.Should().Contain("region");
        json.Should().Contain("us-east-1");
        json.Should().Contain("maxTokens");
        json.Should().Contain("4096");
        json.Should().Contain("temperature");
        json.Should().Contain("0.7");
    }

    [Fact]
    public void AgentConfig_WithBedrockProvider_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = """
        {
            "name": "Test Agent",
            "provider": {
                "providerKey": "bedrock",
                "modelName": "anthropic.claude-3-5-sonnet-20241022-v2:0",
                "providerOptionsJson": "{\"region\":\"us-east-1\",\"maxTokens\":4096,\"temperature\":0.7,\"topP\":0.9}"
            }
        }
        """;

        // Act
        var config = System.Text.Json.JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        config.Should().NotBeNull();
        config!.Name.Should().Be("Test Agent");
        config.Provider.Should().NotBeNull();
        config.Provider!.ProviderKey.Should().Be("bedrock");
        config.Provider.ModelName.Should().Be("anthropic.claude-3-5-sonnet-20241022-v2:0");
        config.Provider.ProviderOptionsJson.Should().NotBeNullOrEmpty();

        // Verify typed config can be retrieved
        var bedrockConfig = config.Provider.GetTypedProviderConfig<BedrockProviderConfig>();
        bedrockConfig.Should().NotBeNull();
        bedrockConfig!.Region.Should().Be("us-east-1");
        bedrockConfig.MaxTokens.Should().Be(4096);
        bedrockConfig.Temperature.Should().Be(0.7f);
        bedrockConfig.TopP.Should().Be(0.9f);
    }

    [Fact]
    public void AgentConfig_WithBedrockProvider_ShouldRoundTripCorrectly()
    {
        // Arrange - Create config with Bedrock provider
        var originalConfig = new AgentConfig
        {
            Name = "Round Trip Test",
            MaxAgenticIterations = 20,
            SystemInstructions = "You are a test assistant.",
            Provider = new ProviderConfig
            {
                ProviderKey = "bedrock",
                ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
            }
        };

        var originalBedrockOpts = new BedrockProviderConfig
        {
            Region = "us-west-2",
            MaxTokens = 8192,
            Temperature = 0.8f,
            TopP = 0.95f,
            StopSequences = new List<string> { "STOP", "END" },
            ToolChoice = "auto",
            GuardrailIdentifier = "my-guardrail",
            GuardrailVersion = "1",
            EnablePromptCaching = true,
            AccessKeyId = "AKIAIOSFODNN7EXAMPLE",
            SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
        };
        originalConfig.Provider.SetTypedProviderConfig(originalBedrockOpts);

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
        deserializedConfig.Provider!.ProviderKey.Should().Be("bedrock");
        deserializedConfig.Provider.ModelName.Should().Be("anthropic.claude-3-5-sonnet-20241022-v2:0");

        // Assert - Bedrock-specific config
        var deserializedBedrockOpts = deserializedConfig.Provider.GetTypedProviderConfig<BedrockProviderConfig>();
        deserializedBedrockOpts.Should().NotBeNull();
        deserializedBedrockOpts!.Region.Should().Be("us-west-2");
        deserializedBedrockOpts.MaxTokens.Should().Be(8192);
        deserializedBedrockOpts.Temperature.Should().Be(0.8f);
        deserializedBedrockOpts.TopP.Should().Be(0.95f);
        deserializedBedrockOpts.StopSequences.Should().HaveCount(2);
        deserializedBedrockOpts.StopSequences.Should().Contain("STOP");
        deserializedBedrockOpts.StopSequences.Should().Contain("END");
        deserializedBedrockOpts.ToolChoice.Should().Be("auto");
        deserializedBedrockOpts.GuardrailIdentifier.Should().Be("my-guardrail");
        deserializedBedrockOpts.GuardrailVersion.Should().Be("1");
        deserializedBedrockOpts.EnablePromptCaching.Should().BeTrue();
        deserializedBedrockOpts.AccessKeyId.Should().Be("AKIAIOSFODNN7EXAMPLE");
        deserializedBedrockOpts.SecretAccessKey.Should().Be("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
    }

    [Fact]
    public void AgentConfig_SetTypedProviderConfig_ShouldUpdateProviderOptionsJson()
    {
        // Arrange
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "bedrock",
                ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
            }
        };

        // Act - Set typed config
        var bedrockOpts = new BedrockProviderConfig
        {
            Region = "us-east-1",
            MaxTokens = 4096,
            Temperature = 0.5f
        };
        config.Provider.SetTypedProviderConfig(bedrockOpts);

        // Assert - ProviderOptionsJson should be populated
        config.Provider.ProviderOptionsJson.Should().NotBeNullOrEmpty();
        config.Provider.ProviderOptionsJson.Should().Contain("region");
        config.Provider.ProviderOptionsJson.Should().Contain("us-east-1");
        config.Provider.ProviderOptionsJson.Should().Contain("maxTokens");
        config.Provider.ProviderOptionsJson.Should().Contain("4096");
        config.Provider.ProviderOptionsJson.Should().Contain("temperature");
        config.Provider.ProviderOptionsJson.Should().Contain("0.5");

        // Verify we can retrieve it back
        var retrieved = config.Provider.GetTypedProviderConfig<BedrockProviderConfig>();
        retrieved.Should().NotBeNull();
        retrieved!.Region.Should().Be("us-east-1");
        retrieved.MaxTokens.Should().Be(4096);
        retrieved.Temperature.Should().Be(0.5f);
    }

    #endregion

    #region AgentBuilder Extension Tests

    [Fact]
    public void WithBedrock_ShouldConfigureProvider()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithBedrock(
            model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
            region: "us-east-1",
            configure: opts =>
            {
                opts.MaxTokens = 4096;
                opts.Temperature = 0.7f;
            });

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.ProviderKey.Should().Be("bedrock");
        builder.Config.Provider.ModelName.Should().Be("anthropic.claude-3-5-sonnet-20241022-v2:0");

        var bedrockConfig = builder.Config.Provider.GetTypedProviderConfig<BedrockProviderConfig>();
        bedrockConfig.Should().NotBeNull();
        bedrockConfig!.Region.Should().Be("us-east-1");
        bedrockConfig.MaxTokens.Should().Be(4096);
        bedrockConfig.Temperature.Should().Be(0.7f);
    }

    [Fact]
    public void WithBedrock_WithCredentials_ShouldConfigureCorrectly()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithBedrock(
            model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
            region: "us-east-1",
            configure: opts =>
            {
                opts.AccessKeyId = "AKIAIOSFODNN7EXAMPLE";
                opts.SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
                opts.MaxTokens = 4096;
            });

        // Assert
        var bedrockConfig = builder.Config.Provider!.GetTypedProviderConfig<BedrockProviderConfig>();
        bedrockConfig.Should().NotBeNull();
        bedrockConfig!.AccessKeyId.Should().Be("AKIAIOSFODNN7EXAMPLE");
        bedrockConfig.SecretAccessKey.Should().Be("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
    }

    [Fact]
    public void WithBedrock_WithNullBuilder_ShouldThrowArgumentNullException()
    {
        // Arrange
        AgentBuilder? builder = null;

        // Act
        var act = () => builder!.WithBedrock(
            model: "anthropic.claude-3-5-sonnet-20241022-v2:0");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithBedrock_WithNullModel_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithBedrock(
            model: null!,
            region: "us-east-1");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model*");
    }

    [Fact]
    public void WithBedrock_WithInvalidTemperature_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithBedrock(
            model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
            region: "us-east-1",
            configure: opts => opts.Temperature = 1.5f);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature*");
    }

    [Fact]
    public void WithBedrock_WithGuardrailConfig_ShouldConfigureCorrectly()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithBedrock(
            model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
            region: "us-east-1",
            configure: opts =>
            {
                opts.GuardrailIdentifier = "my-guardrail";
                opts.GuardrailVersion = "1";
                opts.GuardrailTrace = "enabled";
            });

        // Assert
        var bedrockConfig = builder.Config.Provider!.GetTypedProviderConfig<BedrockProviderConfig>();
        bedrockConfig.Should().NotBeNull();
        bedrockConfig!.GuardrailIdentifier.Should().Be("my-guardrail");
        bedrockConfig.GuardrailVersion.Should().Be("1");
        bedrockConfig.GuardrailTrace.Should().Be("enabled");
    }

    [Fact]
    public void WithBedrock_WithGuardrailButNoVersion_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithBedrock(
            model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
            region: "us-east-1",
            configure: opts => opts.GuardrailIdentifier = "my-guardrail");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*GuardrailVersion*");
    }

    [Fact]
    public void WithBedrock_WithAccessKeyButNoSecret_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithBedrock(
            model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
            region: "us-east-1",
            configure: opts => opts.AccessKeyId = "AKIAIOSFODNN7EXAMPLE");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*SecretAccessKey*");
    }

    #endregion
}
