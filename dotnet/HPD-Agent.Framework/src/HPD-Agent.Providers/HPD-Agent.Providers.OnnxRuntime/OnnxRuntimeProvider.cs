using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace HPD.Agent.Providers.OnnxRuntime;

/// <summary>
/// ONNX Runtime GenAI provider implementation for local model inference.
/// </summary>
[HpdProvider("onnx-runtime", "ONNX Runtime GenAI", DocumentationUrl = "https://onnxruntime.ai/docs/genai/")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(OnnxRuntimeProviderConfig), typeof(OnnxRuntimeJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.OperationOptions, typeof(OnnxRuntimeChatRequestOptions), typeof(OnnxRuntimeJsonContext))]
[HpdProviderSecretAlias("onnx-runtime:ModelPath", "ONNX_MODEL_PATH", "ONNX_RUNTIME_MODEL_PATH")]
internal class OnnxRuntimeProvider : IChatClientProvider
{
    public string ProviderKey => "onnx-runtime";
    public string DisplayName => "ONNX Runtime GenAI";

    public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var onnxConfig = config.ProviderConfig as OnnxRuntimeProviderConfig;
        var secrets = services?.GetService<ISecretResolver>();

        var modelPath = onnxConfig?.ModelPath
            ?? ResolveOptionalSecret(secrets, "onnx-runtime:ModelPath")
            ?? global::System.Environment.GetEnvironmentVariable("ONNX_MODEL_PATH");

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new InvalidOperationException(
                "For the OnnxRuntime provider, the ModelPath must be configured. " +
                "Set it via WithOnnxRuntime(modelPath) or the ONNX_MODEL_PATH environment variable.");
        }

        if (!Directory.Exists(modelPath))
            throw new DirectoryNotFoundException($"Model path does not exist: {modelPath}");

        var clientOptions = new OnnxRuntimeGenAIChatClientOptions
        {
            EnableCaching = onnxConfig?.EnableCaching ?? false,
            PromptFormatter = config.PromptFormatter
        };

        IChatClient finalClient = CreateOnnxChatClient(modelPath, onnxConfig, clientOptions);
        if (onnxConfig?.EnableStructuredToolCalling == true)
        {
            finalClient = new StructuredToolCallingOnnxRuntimeChatClient(finalClient);
        }

        return finalClient;
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new OnnxRuntimeErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://onnxruntime.ai/docs/genai/"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = false,
                        ["SupportsVision"] = true
                    }
                }
            }
        };
    }

    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();
        var onnxConfig = config.ProviderConfig as OnnxRuntimeProviderConfig;
        var modelPath = onnxConfig?.ModelPath ?? global::System.Environment.GetEnvironmentVariable("ONNX_MODEL_PATH");

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            errors.Add("ModelPath is required. Configure it via WithOnnxRuntime(modelPath) or the ONNX_MODEL_PATH environment variable.");
        }
        else if (!Directory.Exists(modelPath))
        {
            errors.Add($"Model path does not exist: {modelPath}");
        }

        if (onnxConfig is not null)
        {
            ValidateProviderOptions(onnxConfig, errors);
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    internal static void ValidateProviderOptions(OnnxRuntimeProviderConfig config, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(errors);

        if (config.Providers is { Count: 0 })
            errors.Add("Providers cannot be empty.");

        if (config.Providers?.Any(string.IsNullOrWhiteSpace) == true)
            errors.Add("Providers cannot contain empty provider names.");

        if (config.ConstructionOptions is not null)
        {
            foreach (var provider in config.ConstructionOptions)
            {
                if (string.IsNullOrWhiteSpace(provider.Key))
                    errors.Add("ConstructionOptions cannot contain an empty provider name.");

                foreach (var option in provider.Value)
                {
                    if (string.IsNullOrWhiteSpace(option.Key))
                        errors.Add($"ConstructionOptions for '{provider.Key}' cannot contain an empty option name.");
                }
            }
        }

        if ((config.HardwareDeviceType is not null ||
             config.HardwareDeviceId is not null ||
             config.HardwareVendorId is not null) &&
            config.Providers is not { Count: > 0 })
        {
            errors.Add("Hardware decoder options require Providers so HPD knows which ONNX Runtime providers to configure.");
        }
    }

    private static OnnxRuntimeGenAIChatClient CreateOnnxChatClient(
        string modelPath,
        OnnxRuntimeProviderConfig? config,
        OnnxRuntimeGenAIChatClientOptions clientOptions)
    {
        if (!RequiresConfig(config))
            return new OnnxRuntimeGenAIChatClient(modelPath, clientOptions);

        var modelConfig = new Config(modelPath);
        var disposeConfig = true;
        try
        {
            if (config?.Providers is { Count: > 0 } providers)
            {
                modelConfig.ClearProviders();
                foreach (var provider in providers)
                {
                    modelConfig.AppendProvider(provider);
                }
            }

            if (config?.ConstructionOptions is not null)
            {
                foreach (var provider in config.ConstructionOptions)
                {
                    foreach (var option in provider.Value)
                    {
                        modelConfig.SetProviderOption(provider.Key, option.Key, option.Value);
                    }
                }
            }

            if (config?.Providers is { Count: > 0 } hardwareProviders)
            {
                foreach (var provider in hardwareProviders)
                {
                    if (!string.IsNullOrWhiteSpace(config.HardwareDeviceType))
                        modelConfig.SetDecoderProviderOptionsHardwareDeviceType(provider, config.HardwareDeviceType);

                    if (config.HardwareDeviceId is { } hardwareDeviceId)
                        modelConfig.SetDecoderProviderOptionsHardwareDeviceId(provider, hardwareDeviceId);

                    if (config.HardwareVendorId is { } hardwareVendorId)
                        modelConfig.SetDecoderProviderOptionsHardwareVendorId(provider, hardwareVendorId);
                }
            }

            var client = new OnnxRuntimeGenAIChatClient(modelConfig, ownsConfig: true, clientOptions);
            disposeConfig = false;
            return client;
        }
        finally
        {
            if (disposeConfig)
                modelConfig.Dispose();
        }
    }

    private static bool RequiresConfig(OnnxRuntimeProviderConfig? config)
        => config?.Providers is { Count: > 0 } ||
           config?.ConstructionOptions is { Count: > 0 } ||
           config?.HardwareDeviceType is not null ||
           config?.HardwareDeviceId is not null ||
           config?.HardwareVendorId is not null;

    private static string? ResolveOptionalSecret(ISecretResolver? secrets, string key)
        => secrets?.ResolveAsync(key, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult()
            ?.Value;
}
