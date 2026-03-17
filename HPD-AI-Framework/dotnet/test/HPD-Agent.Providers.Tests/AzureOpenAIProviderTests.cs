using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.OpenAI;
using Xunit;

namespace HPD.Agent.Tests.Providers;

public class AzureOpenAIProviderTests
{
    private readonly AzureOpenAIProvider _provider;

    public AzureOpenAIProviderTests()
    {
        _provider = new AzureOpenAIProvider();
    }

    #region Metadata Tests

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        // Act
        var metadata = _provider.GetMetadata();

        // Assert
        metadata.Should().NotBeNull();
        metadata.ProviderKey.Should().Be("azure-openai");
        metadata.DisplayName.Should().Be("Azure OpenAI (Traditional)");
        metadata.SupportsStreaming.Should().BeTrue();
        metadata.SupportsFunctionCalling.Should().BeTrue();
        metadata.SupportsVision.Should().BeTrue();
        metadata.DefaulTMetadataWindow.Should().Be(128000);
        metadata.DocumentationUrl.Should().Be("https://learn.microsoft.com/azure/ai-services/openai/");
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-openai",
            ModelName = "gpt-4",
            Endpoint = "https://test.openai.azure.com",
            ApiKey = "test-key"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithMissingEndpoint_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-openai",
            ModelName = "gpt-4",
            ApiKey = "test-key"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Endpoint") && e.Contains("Azure OpenAI"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingApiKey_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-openai",
            ModelName = "gpt-4",
            Endpoint = "https://test.openai.azure.com"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("Azure OpenAI"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingModelName_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-openai",
            Endpoint = "https://test.openai.azure.com",
            ApiKey = "test-key"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name") && e.Contains("deployment name"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-openai",
            ModelName = "gpt-4",
            Endpoint = "https://test.openai.azure.com",
            ApiKey = "test-key"
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
            ProviderKey = "azure-openai",
            ModelName = "gpt-4",
            Endpoint = "https://test.openai.azure.com",
            ApiKey = "test-key"
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
            ProviderKey = "azure-openai",
            ModelName = "gpt-4",
            Endpoint = "https://test.openai.azure.com",
            ApiKey = "test-key"
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
            ProviderKey = "azure-openai",
            ModelName = "gpt-4",
            Endpoint = "https://test.openai.azure.com",
            ApiKey = "test-key"
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
    public void ValidateConfiguration_WithValidProviderConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-openai",
            ModelName = "gpt-4",
            Endpoint = "https://test.openai.azure.com",
            ApiKey = "test-key"
        };

        var openAIConfig = new OpenAIProviderConfig
        {
            Temperature = 0.7f,
            MaxOutputTokenCount = 4096,
            TopP = 0.95f,
            FrequencyPenalty = 0.5f,
            PresencePenalty = 0.5f
        };
        config.SetTypedProviderConfig(openAIConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_WithMultipleErrors_ShouldReturnAllErrors()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "azure-openai"
            // Missing ModelName, Endpoint, and ApiKey
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(3);
        result.Errors.Should().Contain(e => e.Contains("Endpoint"));
        result.Errors.Should().Contain(e => e.Contains("API key"));
        result.Errors.Should().Contain(e => e.Contains("Model name"));
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
}
