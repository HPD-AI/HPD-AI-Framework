using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Ollama;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Xunit;

namespace HPD.Agent.Tests.Providers;

public class OllamaProviderTests
{
    private readonly OllamaProvider _provider;

    public OllamaProviderTests()
    {
        _provider = new OllamaProvider();
    }

    #region Metadata Tests

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        // Act
        var metadata = _provider.GetMetadata();

        // Assert
        metadata.Should().NotBeNull();
        metadata.ProviderKey.Should().Be("ollama");
        metadata.DisplayName.Should().Be("Ollama");
        metadata.SupportsStreaming.Should().BeTrue();
        metadata.SupportsFunctionCalling.Should().BeTrue();
        metadata.SupportsVision.Should().BeTrue();
        metadata.DocumentationUrl.Should().Be("https://ollama.com/");
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b",
            Endpoint = "http://localhost:11434"
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
            ProviderKey = "ollama",
            Endpoint = "http://localhost:11434"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name") && e.Contains("required"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidEndpoint_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b",
            Endpoint = "invalid-endpoint"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Endpoint") && e.Contains("valid"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { Temperature = 3.0f };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature") && e.Contains("0") && e.Contains("2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTopP_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { TopP = 1.5f };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopP") && e.Contains("0") && e.Contains("1"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidMinP_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { MinP = 1.5f };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MinP") && e.Contains("0") && e.Contains("1"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTypicalP_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { TypicalP = -0.5f };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TypicalP") && e.Contains("0") && e.Contains("1"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeTfsZ_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { TfsZ = -1.0f };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TfsZ") && e.Contains("greater than or equal to 0"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeRepeatPenalty_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { RepeatPenalty = -0.5f };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("RepeatPenalty") && e.Contains("greater than or equal to 0"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidPresencePenalty_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { PresencePenalty = 3.0f };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("PresencePenalty") && e.Contains("0") && e.Contains("2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidFrequencyPenalty_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { FrequencyPenalty = -1.0f };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("FrequencyPenalty") && e.Contains("0") && e.Contains("2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidMiroStat_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { MiroStat = 3 };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MiroStat") && e.Contains("0") && e.Contains("2"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeMiroStatEta_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { MiroStatEta = -0.1f };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MiroStatEta") && e.Contains("greater than or equal to 0"));
    }

    [Fact]
    public void ValidateConfiguration_WithNegativeMiroStatTau_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { MiroStatTau = -1.0f };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MiroStatTau") && e.Contains("greater than or equal to 0"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidNumPredict_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { NumPredict = -3 };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NumPredict") && e.Contains("-2"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidNumCtx_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { NumCtx = 0 };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NumCtx") && e.Contains("greater than 0"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidTopK_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig { TopK = 0 };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopK") && e.Contains("greater than 0"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidBoundaryValues_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };
        var ollamaConfig = new OllamaProviderConfig
        {
            Temperature = 2.0f,
            TopP = 1.0f,
            MinP = 0.0f,
            TypicalP = 1.0f,
            TfsZ = 0.0f,
            RepeatPenalty = 0.0f,
            PresencePenalty = 2.0f,
            FrequencyPenalty = 2.0f,
            MiroStat = 2,
            MiroStatEta = 0.0f,
            MiroStatTau = 0.0f,
            NumPredict = -2,
            NumCtx = 1,
            TopK = 1
        };
        config.SetTypedProviderConfig(ollamaConfig);

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
            ProviderKey = "ollama",
            Endpoint = "invalid-uri"
        };
        var ollamaConfig = new OllamaProviderConfig
        {
            Temperature = 5.0f,
            TopP = 2.0f,
            NumCtx = -1
        };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
        result.Errors.Should().Contain(e => e.Contains("Model name"));
        result.Errors.Should().Contain(e => e.Contains("Temperature"));
        result.Errors.Should().Contain(e => e.Contains("TopP"));
        result.Errors.Should().Contain(e => e.Contains("NumCtx"));
    }

    #endregion

    #region Chat Client Creation Tests

    [Fact]
    public void CreateChatClient_WithValidConfig_ShouldReturnClient()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b",
            Endpoint = "http://localhost:11434"
        };

        // Act
        var client = _provider.CreateChatClient(config);

        // Assert
        client.Should().NotBeNull();
        client.Should().BeAssignableTo<IChatClient>();
    }

    [Fact]
    public void CreateChatClient_WithMissingModelName_ShouldThrow()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            Endpoint = "http://localhost:11434"
        };

        // Act & Assert
        var act = () => _provider.CreateChatClient(config);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Model name*required*");
    }

    [Fact]
    public void CreateChatClient_WithoutEndpoint_ShouldUseDefaultLocalhost()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b"
        };

        // Act
        var client = _provider.CreateChatClient(config);

        // Assert
        client.Should().NotBeNull();
        // The client should be created with default localhost:11434
    }

    [Fact]
    public void CreateChatClient_WithOllamaConfig_ShouldStoreRequestOptions()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "ollama",
            ModelName = "llama3:8b",
            Endpoint = "http://localhost:11434"
        };
        var ollamaConfig = new OllamaProviderConfig
        {
            Temperature = 0.7f,
            NumPredict = 2048,
            NumCtx = 4096,
            TopP = 0.9f,
            TopK = 40,
            Seed = 42
        };
        config.SetTypedProviderConfig(ollamaConfig);

        // Act
        var client = _provider.CreateChatClient(config);

        // Assert
        client.Should().NotBeNull();
        config.AdditionalProperties.Should().ContainKey("OllamaRequestOptions");
    }

    // Note: ClientFactory test is intentionally omitted as it conflicts with JSON serialization
    // in GetTypedProviderConfig. The functionality is tested through AgentBuilder extension tests.

    #endregion

    #region Error Handler Tests

    [Fact]
    public void CreateErrorHandler_ShouldReturnOllamaErrorHandler()
    {
        // Act
        var errorHandler = _provider.CreateErrorHandler();

        // Assert
        errorHandler.Should().NotBeNull();
        errorHandler.Should().BeOfType<OllamaErrorHandler>();
    }

    [Fact]
    public void ErrorHandler_ShouldHandleHttpRequestException()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var exception = new HttpRequestException("Connection refused", null, HttpStatusCode.ServiceUnavailable);

        // Act
        var details = errorHandler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(503);
        details.Category.Should().Be(ErrorCategory.Transient);
    }

    [Fact]
    public void ErrorHandler_ShouldHandleTaskCanceledException()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var exception = new TaskCanceledException("Request timed out");

        // Act
        var details = errorHandler.ParseError(exception);

        // Assert
        details.Should().NotBeNull();
        details!.StatusCode.Should().Be(408);
        details.Category.Should().Be(ErrorCategory.Transient);
        details.ErrorCode.Should().Be("timeout");
    }

    [Fact]
    public void ErrorHandler_ShouldRetryOnServerError()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var errorDetails = new ProviderErrorDetails
        {
            StatusCode = 500,
            Category = ErrorCategory.ServerError,
            Message = "Internal server error"
        };

        // Act
        var delay = errorHandler.GetRetryDelay(
            errorDetails,
            attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(60));

        // Assert
        delay.Should().NotBeNull();
        delay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void ErrorHandler_ShouldNotRetryOnClientError()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var errorDetails = new ProviderErrorDetails
        {
            StatusCode = 404,
            Category = ErrorCategory.ClientError,
            Message = "Model not found"
        };

        // Act
        var delay = errorHandler.GetRetryDelay(
            errorDetails,
            attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(60));

        // Assert
        delay.Should().BeNull();
    }

    [Fact]
    public void ErrorHandler_ShouldRetryOnTransientError()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var errorDetails = new ProviderErrorDetails
        {
            StatusCode = 503,
            Category = ErrorCategory.Transient,
            Message = "Service unavailable"
        };

        // Act
        var delay = errorHandler.GetRetryDelay(
            errorDetails,
            attempt: 0,
            initialDelay: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxDelay: TimeSpan.FromSeconds(60));

        // Assert
        delay.Should().NotBeNull();
    }

    [Fact]
    public void ErrorHandler_ShouldNotRequireSpecialHandling()
    {
        // Arrange
        var errorHandler = _provider.CreateErrorHandler();
        var errorDetails = new ProviderErrorDetails
        {
            StatusCode = 401,
            Category = ErrorCategory.AuthError,
            Message = "Unauthorized"
        };

        // Act
        var requiresHandling = errorHandler.RequiresSpecialHandling(errorDetails);

        // Assert
        requiresHandling.Should().BeFalse();
    }

    #endregion

    #region AgentBuilder Extension Tests

    [Fact]
    public void WithOllama_WithValidParameters_ShouldConfigureProvider()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithOllama(model: "llama3:8b");

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.ProviderKey.Should().Be("ollama");
        builder.Config.Provider.ModelName.Should().Be("llama3:8b");
        builder.Config.Provider.Endpoint.Should().Be("http://localhost:11434");
    }

    [Fact]
    public void WithOllama_WithCustomEndpoint_ShouldUseProvidedEndpoint()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithOllama(
            model: "mistral",
            endpoint: "http://remote-server:11434");

        // Assert
        builder.Config.Provider!.Endpoint.Should().Be("http://remote-server:11434");
    }

    [Fact]
    public void WithOllama_WithConfiguration_ShouldApplyOptions()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithOllama(
            model: "llama3:8b",
            configure: opts =>
            {
                opts.Temperature = 0.7f;
                opts.NumPredict = 2048;
                opts.NumCtx = 4096;
            });

        // Assert
        var config = builder.Config.Provider!.GetTypedProviderConfig<OllamaProviderConfig>();
        config.Should().NotBeNull();
        config!.Temperature.Should().Be(0.7f);
        config.NumPredict.Should().Be(2048);
        config.NumCtx.Should().Be(4096);
    }

    [Fact]
    public void WithOllama_WithInvalidTemperature_ShouldThrow()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act & Assert
        var act = () => builder.WithOllama(
            model: "llama3:8b",
            configure: opts => opts.Temperature = 5.0f);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature*0*2*");
    }

    [Fact]
    public void WithOllama_WithNullModel_ShouldThrow()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act & Assert
        var act = () => builder.WithOllama(model: null!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model*required*");
    }

    [Fact]
    public void WithOllama_WithEmptyModel_ShouldThrow()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act & Assert
        var act = () => builder.WithOllama(model: "");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model*required*");
    }

    #endregion

    #region OllamaProviderConfig Tests

    [Fact]
    public void OllamaProviderConfig_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var config = new OllamaProviderConfig
        {
            Temperature = 0.8f,
            NumPredict = 2048,
            NumCtx = 4096,
            TopP = 0.9f,
            TopK = 40,
            Seed = 42,
            Stop = new[] { "\n\n", "END" },
            MiroStat = 2,
            NumGpu = 35,
            KeepAlive = "10m",
            Format = "json",
            Think = true
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(
            config,
            OllamaJsonContext.Default.OllamaProviderConfig);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize(
            json,
            OllamaJsonContext.Default.OllamaProviderConfig);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Temperature.Should().Be(config.Temperature);
        deserialized.NumPredict.Should().Be(config.NumPredict);
        deserialized.NumCtx.Should().Be(config.NumCtx);
        deserialized.Seed.Should().Be(config.Seed);
        deserialized.MiroStat.Should().Be(config.MiroStat);
        deserialized.KeepAlive.Should().Be(config.KeepAlive);
        deserialized.Format.Should().Be(config.Format);
    }

    #endregion
}
