using FluentAssertions;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.OnnxRuntime;
using Xunit;

namespace HPD.Agent.Tests.Providers;

public class OnnxRuntimeProviderTests
{
    private readonly OnnxRuntimeProvider _provider;

    public OnnxRuntimeProviderTests()
    {
        _provider = new OnnxRuntimeProvider();
    }

    #region Metadata Tests

    [Fact]
    public void Provider_ShouldHaveCorrectMetadata()
    {
        // Act
        var metadata = _provider.GetMetadata();

        // Assert
        metadata.Should().NotBeNull();
        metadata.ProviderKey.Should().Be("onnx-runtime");
        metadata.DisplayName.Should().Be("ONNX Runtime GenAI");
        metadata.SupportsStreaming.Should().BeTrue();
        metadata.SupportsFunctionCalling.Should().BeFalse(); // ONNX Runtime GenAI doesn't have built-in function calling yet
        metadata.SupportsVision.Should().BeTrue(); // Phi Vision and other multi-modal models are supported
        metadata.DocumentationUrl.Should().Be("https://onnxruntime.ai/docs/genai/");
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void ValidateConfiguration_WithMissingModelPath_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Should().Contain("ModelPath is required");
    }

    [Fact]
    public void ValidateConfiguration_WithNonExistentModelPath_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = "/non/existent/path"
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Should().Contain("does not exist");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ValidateConfiguration_WithInvalidMaxLength_ShouldFail(int maxLength)
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".", // Current directory exists
            MaxLength = maxLength
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxLength must be greater than 0"));
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidMinLength_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".",
            MinLength = -1
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MinLength must be greater than or equal to 0"));
    }

    [Fact]
    public void ValidateConfiguration_WithMinLengthGreaterThanMaxLength_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".",
            MinLength = 100,
            MaxLength = 50
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MinLength cannot be greater than MaxLength"));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(2.1f)]
    public void ValidateConfiguration_WithInvalidTemperature_ShouldFail(float temperature)
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".",
            Temperature = temperature
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Temperature must be between 0 and 2"));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void ValidateConfiguration_WithInvalidTopP_ShouldFail(float topP)
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".",
            TopP = topP
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopP must be between 0 and 1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateConfiguration_WithInvalidTopK_ShouldFail(int topK)
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".",
            TopK = topK
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TopK must be greater than 0"));
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-1.0f)]
    public void ValidateConfiguration_WithInvalidRepetitionPenalty_ShouldFail(float penalty)
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".",
            RepetitionPenalty = penalty
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("RepetitionPenalty must be greater than 0"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateConfiguration_WithInvalidNumBeams_ShouldFail(int numBeams)
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".",
            NumBeams = numBeams
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NumBeams must be greater than 0"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateConfiguration_WithInvalidNumReturnSequences_ShouldFail(int numReturnSequences)
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".",
            NumReturnSequences = numReturnSequences
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NumReturnSequences must be greater than 0"));
    }

    [Fact]
    public void ValidateConfiguration_WithNumReturnSequencesGreaterThanNumBeams_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".",
            NumBeams = 3,
            NumReturnSequences = 5
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NumReturnSequences cannot be greater than NumBeams"));
    }

    [Fact]
    public void ValidateConfiguration_WithNonExistentAdapterPath_ShouldFail()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".",
            AdapterPath = "/non/existent/adapter.onnx_adapter"
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Adapter file not found"));
    }

    [Fact]
    public void ValidateConfiguration_WithValidConfig_ShouldSucceed()
    {
        // Arrange
        var config = new ProviderConfig
        {
            ProviderKey = "onnx-runtime"
        };

        var onnxConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = ".", // Current directory exists
            MaxLength = 2048,
            MinLength = 10,
            Temperature = 0.7f,
            TopP = 0.9f,
            TopK = 50,
            RepetitionPenalty = 1.1f,
            DoSample = true,
            NumBeams = 4,
            NumReturnSequences = 2
        };
        config.SetTypedProviderConfig(onnxConfig);

        // Act
        var result = _provider.ValidateConfiguration(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region AgentBuilder Extension Tests

    [Fact]
    public void WithOnnxRuntime_WithNullBuilder_ShouldThrowArgumentNullException()
    {
        // Arrange
        AgentBuilder? builder = null;

        // Act
        Action act = () => builder!.WithOnnxRuntime(".");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithOnnxRuntime_WithInvalidModelPath_ShouldThrowArgumentException(string? modelPath)
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        Action act = () => builder.WithOnnxRuntime(modelPath!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Model path is required*");
    }

    [Fact]
    public void WithOnnxRuntime_WithNonExistentModelPath_ShouldThrowDirectoryNotFoundException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        Action act = () => builder.WithOnnxRuntime("/non/existent/path");

        // Assert
        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public void WithOnnxRuntime_WithValidPath_ShouldConfigureProvider()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        builder.WithOnnxRuntime(".", configure: opts =>
        {
            opts.MaxLength = 2048;
            opts.Temperature = 0.7f;
        });

        // Assert
        builder.Config.Provider.Should().NotBeNull();
        builder.Config.Provider!.ProviderKey.Should().Be("onnx-runtime");

        var config = builder.Config.Provider.GetTypedProviderConfig<OnnxRuntimeProviderConfig>();
        config.Should().NotBeNull();
        config!.ModelPath.Should().Be(".");
        config.MaxLength.Should().Be(2048);
        config.Temperature.Should().Be(0.7f);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(2.1f)]
    public void WithOnnxRuntime_WithInvalidTemperature_ShouldThrowArgumentException(float temperature)
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        Action act = () => builder.WithOnnxRuntime(".", configure: opts =>
        {
            opts.Temperature = temperature;
        });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Temperature must be between 0 and 2*");
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void WithOnnxRuntime_WithInvalidTopP_ShouldThrowArgumentException(float topP)
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        Action act = () => builder.WithOnnxRuntime(".", configure: opts =>
        {
            opts.TopP = topP;
        });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*TopP must be between 0 and 1*");
    }

    [Fact]
    public void WithOnnxRuntime_WithInvalidTopK_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        Action act = () => builder.WithOnnxRuntime(".", configure: opts =>
        {
            opts.TopK = 0;
        });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*TopK must be greater than 0*");
    }

    [Fact]
    public void WithOnnxRuntime_WithMinLengthGreaterThanMaxLength_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        Action act = () => builder.WithOnnxRuntime(".", configure: opts =>
        {
            opts.MinLength = 100;
            opts.MaxLength = 50;
        });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MinLength cannot be greater than MaxLength*");
    }

    [Fact]
    public void WithOnnxRuntime_WithNumReturnSequencesGreaterThanNumBeams_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        Action act = () => builder.WithOnnxRuntime(".", configure: opts =>
        {
            opts.NumBeams = 3;
            opts.NumReturnSequences = 5;
        });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*NumReturnSequences cannot be greater than NumBeams*");
    }

    [Fact]
    public void WithOnnxRuntime_WithGuidanceTypeButNoData_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        Action act = () => builder.WithOnnxRuntime(".", configure: opts =>
        {
            opts.GuidanceType = "json";
            // Missing GuidanceData
        });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*GuidanceData is required when GuidanceType is specified*");
    }

    [Fact]
    public void WithOnnxRuntime_WithGuidanceDataButNoType_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        Action act = () => builder.WithOnnxRuntime(".", configure: opts =>
        {
            opts.GuidanceData = "{}";
            // Missing GuidanceType
        });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*GuidanceType is required when GuidanceData is specified*");
    }

    [Fact]
    public void WithOnnxRuntime_WithAdapterNameButNoPath_ShouldThrowArgumentException()
    {
        // Arrange
        var builder = new AgentBuilder();

        // Act
        Action act = () => builder.WithOnnxRuntime(".", configure: opts =>
        {
            opts.AdapterName = "test_adapter";
            // Missing AdapterPath
        });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*AdapterPath is required when AdapterName is specified*");
    }

    #endregion

    #region OnnxRuntimeProviderConfig Tests

    [Fact]
    public void OnnxRuntimeProviderConfig_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var config = new OnnxRuntimeProviderConfig();

        // Assert
        config.ModelPath.Should().BeNull();
        config.MaxLength.Should().BeNull();
        config.MinLength.Should().BeNull();
        config.BatchSize.Should().BeNull();
        config.DoSample.Should().BeNull();
        config.Temperature.Should().BeNull();
        config.TopK.Should().BeNull();
        config.TopP.Should().BeNull();
        config.RepetitionPenalty.Should().BeNull();
        config.NumBeams.Should().BeNull();
        config.NumReturnSequences.Should().BeNull();
        config.EarlyStopping.Should().BeNull();
        config.LengthPenalty.Should().BeNull();
        config.RandomSeed.Should().BeNull();
        config.StopSequences.Should().BeNull();
        config.EnableCaching.Should().BeFalse();
        config.GuidanceType.Should().BeNull();
        config.GuidanceData.Should().BeNull();
        config.GuidanceEnableFFTokens.Should().BeFalse();
        config.Providers.Should().BeNull();
        config.ProviderOptions.Should().BeNull();
        config.AdapterPath.Should().BeNull();
        config.AdapterName.Should().BeNull();
    }

    [Fact]
    public void OnnxRuntimeProviderConfig_SetAllProperties_ShouldSucceed()
    {
        // Arrange & Act
        var config = new OnnxRuntimeProviderConfig
        {
            ModelPath = "/path/to/model",
            MaxLength = 2048,
            MinLength = 10,
            BatchSize = 4,
            DoSample = true,
            Temperature = 0.8f,
            TopK = 50,
            TopP = 0.9f,
            RepetitionPenalty = 1.1f,
            NumBeams = 4,
            NumReturnSequences = 2,
            EarlyStopping = true,
            LengthPenalty = 1.2f,
            NoRepeatNgramSize = 3,
            DiversityPenalty = 0.5f,
            PastPresentShareBuffer = true,
            ChunkSize = 512,
            RandomSeed = 42,
            StopSequences = new List<string> { "<|end|>", "<|user|>" },
            EnableCaching = true,
            GuidanceType = "json",
            GuidanceData = "{}",
            GuidanceEnableFFTokens = true,
            Providers = new List<string> { "cuda", "cpu" },
            ProviderOptions = new Dictionary<string, Dictionary<string, string>>
            {
                ["cuda"] = new Dictionary<string, string> { ["device_id"] = "0" }
            },
            HardwareDeviceType = "gpu",
            HardwareDeviceId = 0,
            HardwareVendorId = 1234,
            AdapterPath = "/path/to/adapter.onnx_adapter",
            AdapterName = "test_adapter"
        };

        // Assert
        config.ModelPath.Should().Be("/path/to/model");
        config.MaxLength.Should().Be(2048);
        config.MinLength.Should().Be(10);
        config.BatchSize.Should().Be(4);
        config.DoSample.Should().BeTrue();
        config.Temperature.Should().Be(0.8f);
        config.TopK.Should().Be(50);
        config.TopP.Should().Be(0.9f);
        config.RepetitionPenalty.Should().Be(1.1f);
        config.NumBeams.Should().Be(4);
        config.NumReturnSequences.Should().Be(2);
        config.EarlyStopping.Should().BeTrue();
        config.LengthPenalty.Should().Be(1.2f);
        config.NoRepeatNgramSize.Should().Be(3);
        config.DiversityPenalty.Should().Be(0.5f);
        config.PastPresentShareBuffer.Should().BeTrue();
        config.ChunkSize.Should().Be(512);
        config.RandomSeed.Should().Be(42);
        config.StopSequences.Should().HaveCount(2);
        config.EnableCaching.Should().BeTrue();
        config.GuidanceType.Should().Be("json");
        config.GuidanceData.Should().Be("{}");
        config.GuidanceEnableFFTokens.Should().BeTrue();
        config.Providers.Should().HaveCount(2);
        config.ProviderOptions.Should().ContainKey("cuda");
        config.HardwareDeviceType.Should().Be("gpu");
        config.HardwareDeviceId.Should().Be(0);
        config.HardwareVendorId.Should().Be(1234);
        config.AdapterPath.Should().Be("/path/to/adapter.onnx_adapter");
        config.AdapterName.Should().Be("test_adapter");
    }

    #endregion

    #region Error Handler Tests

    [Fact]
    public void ErrorHandler_WithNonOnnxException_ShouldReturnNull()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = handler.ParseError(exception);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ErrorHandler_ShouldNotRetryClientErrors()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.ClientError,
            Message = "Invalid configuration"
        };

        // Act
        var retryDelay = handler.GetRetryDelay(details, 1, TimeSpan.FromSeconds(1), 2.0, TimeSpan.FromSeconds(10));

        // Assert
        retryDelay.Should().BeNull();
    }

    [Fact]
    public void ErrorHandler_ShouldRetryTransientErrors()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.Transient,
            Message = "Out of memory"
        };

        // Act
        var retryDelay = handler.GetRetryDelay(details, 1, TimeSpan.FromSeconds(1), 2.0, TimeSpan.FromSeconds(10));

        // Assert
        retryDelay.Should().NotBeNull();
        retryDelay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void ErrorHandler_ShouldRespectMaxRetryDelay()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.Transient,
            Message = "Out of memory"
        };

        // Act
        var retryDelay = handler.GetRetryDelay(details, 10, TimeSpan.FromSeconds(1), 2.0, TimeSpan.FromSeconds(5));

        // Assert
        retryDelay.Should().NotBeNull();
        retryDelay.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ErrorHandler_ClientErrorsShouldRequireSpecialHandling()
    {
        // Arrange
        var handler = _provider.CreateErrorHandler();
        var details = new ProviderErrorDetails
        {
            Category = ErrorCategory.ClientError,
            Message = "Invalid configuration"
        };

        // Act
        var requiresSpecialHandling = handler.RequiresSpecialHandling(details);

        // Assert
        requiresSpecialHandling.Should().BeTrue();
    }

    #endregion
}
