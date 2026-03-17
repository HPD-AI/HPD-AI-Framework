using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.OpenAI;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Providers;

public class OpenAIProviderTests
{
    private readonly OpenAIProvider _provider;

    public OpenAIProviderTests()
    {
        _provider = new OpenAIProvider();
    }

    #region Metadata Tests

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        // Act
        var metadata = _provider.GetMetadata();

        // Assert
        metadata.Should().NotBeNull();
        metadata.ProviderKey.Should().Be("openai");
        metadata.DisplayName.Should().Be("OpenAI");
        metadata.SupportsStreaming.Should().BeTrue();
        metadata.SupportsFunctionCalling.Should().BeTrue();
        metadata.SupportsVision.Should().BeTrue();
        metadata.SupportsAudio.Should().BeTrue();
        metadata.DefaulTMetadataWindow.Should().Be(128000);
        metadata.DocumentationUrl.Should().Be("https://platform.openai.com/docs");
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
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
            ProviderKey = "openai",
            ModelName = "gpt-4o"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("OpenAI"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingModelName_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ApiKey = "sk-test123"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name is required"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            Temperature = 2.5f // Invalid: must be <= 2.0
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTopP_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            TopP = 1.5f // Invalid: must be <= 1.0
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopP must be between 0 and 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidFrequencyPenalty_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            FrequencyPenalty = 3.0f // Invalid: must be <= 2.0
        };
        config.SetTypedProviderConfig(openAIConfig);

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
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            PresencePenalty = -3.0f // Invalid: must be >= -2.0
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("PresencePenalty must be between -2 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithTooManyStopSequences_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            StopSequences = new List<string> { "one", "two", "three", "four", "five" } // Max 4 allowed
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Maximum of 4 stop sequences allowed"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTopLogProbabilityCount_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            TopLogProbabilityCount = 25 // Invalid: must be <= 20
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopLogProbabilityCount must be between 0 and 20"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidResponseFormat_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            ResponseFormat = "invalid_format"
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ResponseFormat must be one of: text, json_object, json_schema"));
    }

    [Fact]
    public void ValidateConfiguration_WithJsonSchemaFormat_WithoutSchemaName_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            ResponseFormat = "json_schema",
            JsonSchema = "{\"type\":\"object\"}"
            // Missing JsonSchemaName
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("JsonSchemaName is required"));
    }

    [Fact]
    public void ValidateConfiguration_WithJsonSchemaFormat_WithoutSchema_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            ResponseFormat = "json_schema",
            JsonSchemaName = "TestSchema"
            // Missing JsonSchema
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("JsonSchema is required"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidJsonSchema_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            ResponseFormat = "json_schema",
            JsonSchemaName = "TestSchema",
            JsonSchema = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidToolChoice_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            ToolChoice = "invalid_choice"
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ToolChoice must be one of: auto, none, required"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidReasoningEffortLevel_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "o1-preview",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            ReasoningEffortLevel = "ultra" // Invalid
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ReasoningEffortLevel must be one of: low, medium, high, minimal"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidAudioVoice_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o-audio-preview",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            AudioVoice = "invalid_voice"
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("AudioVoice must be one of"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidAudioFormat_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o-audio-preview",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            AudioFormat = "aac" // Invalid
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("AudioFormat must be one of"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidServiceTier_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            ServiceTier = "premium" // Invalid
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ServiceTier must be one of: auto, default"));
    }

    [Fact]
    public void ValidateConfiguration_WithMultipleErrors_ShouldReturnAllErrors()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "openai",
            ModelName = "gpt-4o",
            ApiKey = "sk-test123"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            Temperature = 3.0f, // Invalid
            TopP = 1.5f, // Invalid
            FrequencyPenalty = 5.0f // Invalid
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(3);
        result.Errors.Should().Contain(e => e.Contains("Temperature"));
        result.Errors.Should().Contain(e => e.Contains("TopP"));
        result.Errors.Should().Contain(e => e.Contains("FrequencyPenalty"));
    }

    #endregion

    #region Error Handler Tests

    [Fact]
    public void CreateErrorHandler_ShouldReturnOpenAIErrorHandler()
    {
        // Act
        var errorHandler = _provider.CreateErrorHandler();

        // Assert
        errorHandler.Should().NotBeNull();
        errorHandler.Should().BeOfType<OpenAIErrorHandler>();
    }

    #endregion

    #region AgentBuilder Extension Tests

    [Fact]
    public void WithOpenAI_ShouldConfigureProvider()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithOpenAI(
            model: "gpt-4o",
            apiKey: "sk-test123",
            configure: opts =>
            {
                opts.Temperature = 0.7f;
                opts.MaxOutputTokenCount = 4096;
            });

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider.ProviderKey.Should().Be("openai");
        builder.Config.Provider.ModelName.Should().Be("gpt-4o");
        builder.Config.Provider.ApiKey.Should().Be("sk-test123");

        var typedConfig = builder.Config.Provider.GetTypedProviderConfig<OpenAIProviderConfig>();
        typedConfig.Should().NotBeNull();
        typedConfig!.Temperature.Should().Be(0.7f);
        typedConfig.MaxOutputTokenCount.Should().Be(4096);
    }

    [Fact]
    public void WithOpenAI_WithNullModel_ShouldThrow()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithOpenAI(model: null!, apiKey: "sk-test123");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model is required*");
    }

    [Fact]
    public void WithOpenAI_WithEmptyModel_ShouldThrow()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithOpenAI(model: "", apiKey: "sk-test123");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model is required*");
    }

    [Fact]
    public void WithOpenAI_WithInvalidTemperature_ShouldThrow()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithOpenAI(
            model: "gpt-4o",
            apiKey: "sk-test123",
            configure: opts => opts.Temperature = 3.0f);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature must be between 0 and 2*");
    }

    [Fact]
    public void WithAzureOpenAI_ShouldConfigureProvider()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithAzureOpenAI(
            endpoint: "https://test.openai.azure.com",
            model: "gpt-4",
            apiKey: "test-key",
            configure: opts =>
            {
                opts.Temperature = 0.5f;
                opts.MaxOutputTokenCount = 2048;
            });

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider.ProviderKey.Should().Be("azure-openai");
        builder.Config.Provider.ModelName.Should().Be("gpt-4");
        builder.Config.Provider.Endpoint.Should().Be("https://test.openai.azure.com");
        builder.Config.Provider.ApiKey.Should().Be("test-key");

        var typedConfig = builder.Config.Provider.GetTypedProviderConfig<OpenAIProviderConfig>();
        typedConfig.Should().NotBeNull();
        typedConfig!.Temperature.Should().Be(0.5f);
        typedConfig.MaxOutputTokenCount.Should().Be(2048);
    }

    [Fact]
    public void WithAzureOpenAI_WithNullEndpoint_ShouldThrow()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureOpenAI(
            endpoint: null!,
            model: "gpt-4",
            apiKey: "test-key");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Endpoint is required*");
    }

    [Fact]
    public void WithAzureOpenAI_WithNullModel_ShouldThrow()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithAzureOpenAI(
            endpoint: "https://test.openai.azure.com",
            model: null!,
            apiKey: "test-key");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model is required*");
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void OpenAIProviderConfig_ShouldSerializeToJson()
    {
        // Arrange
        var config = new OpenAIProviderConfig
        {
            Temperature = 0.7f,
            MaxOutputTokenCount = 4096,
            TopP = 0.95f,
            FrequencyPenalty = 0.5f,
            PresencePenalty = 0.5f,
            ResponseFormat = "json_schema",
            JsonSchemaName = "TestSchema",
            JsonSchema = "{\"type\":\"object\"}",
            ToolChoice = "auto",
            AllowParallelToolCalls = true,
            Seed = 12345
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(config, OpenAIJsonContext.Default.OpenAIProviderConfig);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("temperature");
        json.Should().Contain("0.7");
        json.Should().Contain("maxOutputTokenCount");
        json.Should().Contain("4096");
    }

    [Fact]
    public void OpenAIProviderConfig_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = @"{
            ""temperature"": 0.7,
            ""maxOutputTokenCount"": 4096,
            ""topP"": 0.95,
            ""responseFormat"": ""json_schema"",
            ""jsonSchemaName"": ""TestSchema"",
            ""jsonSchema"": ""{\""type\"":\""object\""}""
        }";

        // Act
        var config = System.Text.Json.JsonSerializer.Deserialize(json, OpenAIJsonContext.Default.OpenAIProviderConfig);

        // Assert
        config.Should().NotBeNull();
        config!.Temperature.Should().Be(0.7f);
        config.MaxOutputTokenCount.Should().Be(4096);
        config.TopP.Should().Be(0.95f);
        config.ResponseFormat.Should().Be("json_schema");
        config.JsonSchemaName.Should().Be("TestSchema");
        config.JsonSchema.Should().Be("{\"type\":\"object\"}");
    }

    #endregion
}
