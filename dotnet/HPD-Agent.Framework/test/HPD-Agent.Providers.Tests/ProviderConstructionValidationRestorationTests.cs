using FluentAssertions;
using HPD.Agent.Providers.Bedrock;
using HPD.Agent.Providers.Ollama;
using HPD.Agent.Providers.OnnxRuntime;

namespace HPD.Agent.Providers.Tests;

public sealed class ProviderConstructionValidationRestorationTests
{
    [Fact]
    public void Bedrock_ValidateConfiguration_ShouldRequireCredentialPairingAndPositiveClientOptions()
    {
        var provider = new BedrockProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20240620-v1:0"
        };
        config.SetProviderConfig(new BedrockProviderConfig
        {
            AccessKeyId = "access-key",
            RequestTimeoutMs = 0,
            ConnectTimeoutMs = -1,
            MaxRetryAttempts = -1,
            ProxyPort = 70000
        });

        var result = provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("SecretAccessKey", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("RequestTimeoutMs", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("ConnectTimeoutMs", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("MaxRetryAttempts", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("ProxyPort", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Bedrock_ValidateConfiguration_ShouldRequireAccessKeyWhenSecretIsSpecified()
    {
        var provider = new BedrockProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "bedrock",
            ModelName = "anthropic.claude-3-5-sonnet-20240620-v1:0"
        };
        config.SetProviderConfig(new BedrockProviderConfig
        {
            SecretAccessKey = "secret-key"
        });

        var result = provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("AccessKeyId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OnnxRuntime_ValidateConfiguration_ShouldRequireExistingModelPath()
    {
        var provider = new OnnxRuntimeProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "onnx-runtime"
        };
        config.SetProviderConfig(new OnnxRuntimeProviderConfig
        {
            ModelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
        });

        var result = provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("Model path does not exist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OnnxRuntime_ValidateConfiguration_ShouldRejectInvalidProviderAndHardwareConstructionOptions()
    {
        var provider = new OnnxRuntimeProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "onnx-runtime"
        };
        config.SetProviderConfig(new OnnxRuntimeProviderConfig
        {
            ModelPath = Directory.GetCurrentDirectory(),
            Providers = [""],
            ConstructionOptions = new Dictionary<string, Dictionary<string, string>>
            {
                [""] = new()
                {
                    [""] = "value"
                }
            },
            HardwareDeviceType = "gpu"
        });

        var result = provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("empty provider names", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("empty provider name", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("empty option name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OnnxRuntime_ValidateConfiguration_ShouldRequireProvidersForHardwareDecoderOptions()
    {
        var provider = new OnnxRuntimeProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "onnx-runtime"
        };
        config.SetProviderConfig(new OnnxRuntimeProviderConfig
        {
            ModelPath = Directory.GetCurrentDirectory(),
            HardwareDeviceType = "gpu"
        });

        var result = provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("Hardware decoder options require Providers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ollama_ValidateConfiguration_ShouldRejectInvalidEndpointAndTimeout()
    {
        var provider = new OllamaProvider();
        var config = new ProviderClientConfig
        {
            ProviderKey = "ollama",
            ModelName = "qwen3",
            Endpoint = "not-a-uri"
        };
        config.SetProviderConfig(new OllamaProviderConfig
        {
            TimeoutMs = 0
        });

        var result = provider.ValidateConfiguration(config, ProviderClientFamily.Chat);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("Endpoint", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("TimeoutMs", StringComparison.OrdinalIgnoreCase));
    }
}
