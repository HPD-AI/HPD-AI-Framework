using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.GoogleAI;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Providers.Tests;

public class GoogleAIProviderTests
{
    private readonly GoogleAIProvider _provider;

    public GoogleAIProviderTests()
    {
        _provider = new GoogleAIProvider();
    }

    #region Metadata Tests

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        // Act
        var metadata = _provider.GetMetadata();

        // Assert
        metadata.Should().NotBeNull();
        metadata.ProviderKey.Should().Be("google-ai");
        metadata.DisplayName.Should().Be("Google AI (Gemini)");
        metadata.SupportsStreaming.Should().BeTrue();
        metadata.SupportsFunctionCalling.Should().BeTrue();
        metadata.SupportsVision.Should().BeTrue();
        metadata.DocumentationUrl.Should().Be("https://ai.google.dev/docs");
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
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
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("API key") && e.Contains("required"));
    }

    [Fact]
    public void ValidateConfiguration_WithMissingModelName_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ApiKey = "test-api-key"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name") && e.Contains("required"));
    }

    #endregion

    #region Sampling Parameter Validation Tests

    [Fact]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
        };

        var googleConfig = new GoogleAIProviderConfig
        {
            Temperature = 2.5 // Invalid: must be <= 2.0
        };
        config.SetTypedProviderConfig(googleConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
        };

        var googleConfig = new GoogleAIProviderConfig
        {
            Temperature = -0.1 // Invalid: must be >= 0
        };
        config.SetTypedProviderConfig(googleConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 2"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidTemperatureRange_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
        };

        var googleConfig = new GoogleAIProviderConfig
        {
            Temperature = 1.5 // Valid: 0-2 range
        };
        config.SetTypedProviderConfig(googleConfig);

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
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
        };

        var googleConfig = new GoogleAIProviderConfig
        {
            TopP = 1.5 // Invalid: must be <= 1.0
        };
        config.SetTypedProviderConfig(googleConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopP must be between 0 and 1"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeTopP_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
        };

        var googleConfig = new GoogleAIProviderConfig
        {
            TopP = -0.1 // Invalid: must be >= 0
        };
        config.SetTypedProviderConfig(googleConfig);

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
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
        };

        var googleConfig = new GoogleAIProviderConfig
        {
            TopK = -5 // Invalid: must be positive
        };
        config.SetTypedProviderConfig(googleConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopK must be a positive integer"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidCandidateCount_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
        };

        var googleConfig = new GoogleAIProviderConfig
        {
            CandidateCount = 5 // Invalid: currently only 1 is supported
        };
        config.SetTypedProviderConfig(googleConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("CandidateCount currently only supports a value of 1"));
    }

    #endregion

    #region Response Format Validation Tests

    [Fact]
    public void ValidateConfiguration_WithResponseSchemaButWrongMimeType_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
        };

        var googleConfig = new GoogleAIProviderConfig
        {
            ResponseSchema = "{\"type\":\"object\"}",
            ResponseMimeType = "text/plain" // Invalid: must be application/json when ResponseSchema is set
        };
        config.SetTypedProviderConfig(googleConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ResponseMimeType must be 'application/json'"));
    }

    [Fact]
    public void ValidateConfiguration_WithBothResponseSchemaAndResponseJsonSchema_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
        };

        var googleConfig = new GoogleAIProviderConfig
        {
            ResponseSchema = "{\"type\":\"object\"}",
            ResponseJsonSchema = "{\"type\":\"object\"}" // Invalid: can't have both
        };
        config.SetTypedProviderConfig(googleConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ResponseSchema and ResponseJsonSchema cannot both be set"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidResponseSchema_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "google-ai",
            ModelName = "gemini-2.0-flash",
            ApiKey = "test-api-key"
        };

        var googleConfig = new GoogleAIProviderConfig
        {
            ResponseSchema = "{\"type\":\"object\"}",
            ResponseMimeType = "application/json"
        };
        config.SetTypedProviderConfig(googleConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region AgentBuilder Extension Tests

    [Fact]
    public void WithGoogleAI_WithValidParameters_ShouldConfigureBuilder()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts =>
            {
                opts.Temperature = 0.7;
                opts.MaxOutputTokens = 8192;
            });

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.ProviderKey.Should().Be("google-ai");
        builder.Config.Provider.ApiKey.Should().Be("test-api-key");
        builder.Config.Provider.ModelName.Should().Be("gemini-2.0-flash");

        var googleConfig = builder.Config.Provider.GetTypedProviderConfig<GoogleAIProviderConfig>();
        googleConfig.Should().NotBeNull();
        googleConfig!.Temperature.Should().Be(0.7);
        googleConfig.MaxOutputTokens.Should().Be(8192);
    }

    [Fact]
    public void WithGoogleAI_WithInvalidTemperature_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts => opts.Temperature = 3.0); // Invalid

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature must be between 0 and 2*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidTopP_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts => opts.TopP = 1.5); // Invalid

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*TopP must be between 0 and 1*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidTopK_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts => opts.TopK = -10); // Invalid

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*TopK must be a positive integer*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidCandidateCount_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts => opts.CandidateCount = 5); // Invalid

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*CandidateCount currently only supports a value of 1*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidResponseMimeType_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts => opts.ResponseMimeType = "invalid/type"); // Invalid

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ResponseMimeType must be one of*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidFunctionCallingMode_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts => opts.FunctionCallingMode = "INVALID_MODE"); // Invalid

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*FunctionCallingMode must be one of: AUTO, ANY, NONE*");
    }

    [Fact]
    public void WithGoogleAI_WithAllowedFunctionNamesButWrongMode_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts =>
            {
                opts.FunctionCallingMode = "AUTO";
                opts.AllowedFunctionNames = new List<string> { "function1" };
            });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*AllowedFunctionNames can only be set when FunctionCallingMode is ANY*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidThinkingLevel_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-3-flash",
            configure: opts => opts.ThinkingLevel = "INVALID_LEVEL"); // Invalid

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ThinkingLevel must be one of*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidMediaResolution_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts => opts.MediaResolution = "INVALID_RESOLUTION"); // Invalid

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MediaResolution must be one of*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidImageOutputMimeType_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.5-flash-image",
            configure: opts => opts.ImageOutputMimeType = "image/webp"); // Invalid

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ImageOutputMimeType must be one of: image/png, image/jpeg*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidImageCompressionQuality_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.5-flash-image",
            configure: opts => opts.ImageCompressionQuality = 150); // Invalid: must be 0-100

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ImageCompressionQuality must be between 0 and 100*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidSafetySettingCategory_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts =>
            {
                opts.SafetySettings = new List<SafetySettingConfig>
                {
                    new() { Category = "INVALID_CATEGORY", Threshold = "BLOCK_MEDIUM_AND_ABOVE" }
                };
            });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid safety setting category*");
    }

    [Fact]
    public void WithGoogleAI_WithInvalidSafetySettingThreshold_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts =>
            {
                opts.SafetySettings = new List<SafetySettingConfig>
                {
                    new() { Category = "HARM_CATEGORY_HARASSMENT", Threshold = "INVALID_THRESHOLD" }
                };
            });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid safety setting threshold*");
    }

    [Fact]
    public void WithGoogleAI_WithValidSafetySettings_ShouldSucceed()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-2.0-flash",
            configure: opts =>
            {
                opts.SafetySettings = new List<SafetySettingConfig>
                {
                    new() { Category = "HARM_CATEGORY_HARASSMENT", Threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                    new() { Category = "HARM_CATEGORY_HATE_SPEECH", Threshold = "BLOCK_ONLY_HIGH" }
                };
            });

        // Assert
        var googleConfig = builder.Config.Provider!.GetTypedProviderConfig<GoogleAIProviderConfig>();
        googleConfig.Should().NotBeNull();
        googleConfig!.SafetySettings.Should().HaveCount(2);
        googleConfig.SafetySettings![0].Category.Should().Be("HARM_CATEGORY_HARASSMENT");
        googleConfig.SafetySettings[0].Threshold.Should().Be("BLOCK_MEDIUM_AND_ABOVE");
    }

    [Fact]
    public void WithGoogleAI_WithThinkingConfig_ShouldSucceed()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: "gemini-3-flash",
            configure: opts =>
            {
                opts.IncludeThoughts = true;
                opts.ThinkingBudget = 5000;
                opts.ThinkingLevel = "HIGH";
            });

        // Assert
        var googleConfig = builder.Config.Provider!.GetTypedProviderConfig<GoogleAIProviderConfig>();
        googleConfig.Should().NotBeNull();
        googleConfig!.IncludeThoughts.Should().BeTrue();
        googleConfig.ThinkingBudget.Should().Be(5000);
        googleConfig.ThinkingLevel.Should().Be("HIGH");
    }

    [Fact]
    public void WithGoogleAI_WithEmptyModel_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        var act = () => builder.WithGoogleAI(
            apiKey: "test-api-key",
            model: ""); // Invalid

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model is required*");
    }

    #endregion

    #region Error Handler Tests

    [Fact]
    public void ErrorHandler_ShouldClassify400AsClientError()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var exception = new System.Net.Http.HttpRequestException("Bad request", null, System.Net.HttpStatusCode.BadRequest);

        // Act
        var details = errorHandler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(400);
        details.Category.Should().Be(ErrorCategory.ClientError);
    }

    [Fact]
    public void ErrorHandler_ShouldClassify401AsAuthError()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var exception = new System.Net.Http.HttpRequestException("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized);

        // Act
        var details = errorHandler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(401);
        details.Category.Should().Be(ErrorCategory.AuthError);
    }

    [Fact]
    public void ErrorHandler_ShouldClassify403AsAuthError()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var exception = new System.Net.Http.HttpRequestException("Forbidden", null, System.Net.HttpStatusCode.Forbidden);

        // Act
        var details = errorHandler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(403);
        details.Category.Should().Be(ErrorCategory.AuthError);
    }

    [Fact]
    public void ErrorHandler_ShouldClassify429AsRateLimitRetryable()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var exception = new System.Net.Http.HttpRequestException("Too many requests", null, System.Net.HttpStatusCode.TooManyRequests);

        // Act
        var details = errorHandler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(429);
        details.Category.Should().Be(ErrorCategory.RateLimitRetryable);
    }

    [Fact]
    public void ErrorHandler_ShouldClassify500AsServerError()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var exception = new System.Net.Http.HttpRequestException("Internal server error", null, System.Net.HttpStatusCode.InternalServerError);

        // Act
        var details = errorHandler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(500);
        details.Category.Should().Be(ErrorCategory.ServerError);
    }

    [Fact]
    public void ErrorHandler_ShouldClassify503AsTransient()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var exception = new System.Net.Http.HttpRequestException("Service unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable);

        // Act
        var details = errorHandler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(503);
        details.Category.Should().Be(ErrorCategory.Transient);
    }

    [Fact]
    public void ErrorHandler_ShouldProvideRetryDelayForRateLimitError()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var errorDetails = new ProviderErrorDetails
        {
            StatusCode = 429,
            Category = ErrorCategory.RateLimitRetryable,
            Message = "Rate limit exceeded"
        };

        // Act
        var retryDelay = errorHandler.GetRetryDelay(
            errorDetails,
            attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromMinutes(5));

        // Assert
        retryDelay.Should().NotBeNull();
        retryDelay!.Value.TotalSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ErrorHandler_ShouldRequireSpecialHandlingForAuthError()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var errorDetails = new ProviderErrorDetails
        {
            StatusCode = 401,
            Category = ErrorCategory.AuthError,
            Message = "Invalid API key"
        };

        // Act
        var requiresSpecialHandling = errorHandler.RequiresSpecialHandling(errorDetails);

        // Assert
        requiresSpecialHandling.Should().BeTrue();
    }

    #endregion

    #region Provider Key Tests

    [Fact]
    public void Provider_ShouldHaveCorrectProviderKey()
    {
        // Assert
        _provider.ProviderKey.Should().Be("google-ai");
    }

    [Fact]
    public void Provider_ShouldHaveCorrectDisplayName()
    {
        // Assert
        _provider.DisplayName.Should().Be("Google AI (Gemini)");
    }

    #endregion
}
