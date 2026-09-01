using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace HPD.Agent.Providers.OnnxRuntime;

/// <summary>
/// ONNX Runtime GenAI provider implementation for local model inference.
/// </summary>
[HpdProvider("onnx-runtime", "ONNX Runtime GenAI", DocumentationUrl = "https://onnxruntime.ai/docs/genai/")]
[HpdProviderBackend("local", ProviderAuthenticationKind.Anonymous, IsDefaultBackend = true, IsDefaultAuthentication = true)]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(OnnxRuntimeProviderConfig), typeof(OnnxRuntimeJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.OperationOptions, typeof(OnnxRuntimeChatRequestOptions), typeof(OnnxRuntimeJsonContext))]
[HpdProviderSecretAlias("onnx-runtime:ModelPath", "ONNX_MODEL_PATH", "ONNX_RUNTIME_MODEL_PATH")]
internal class OnnxRuntimeProvider : IProvider, IProviderClientFactory<IChatClient>, IProviderSecretAliasProvider
{
    public string ProviderKey => "onnx-runtime";
    public string DisplayName => "ONNX Runtime GenAI";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("onnx-runtime:ModelPath", new[] { "ONNX_MODEL_PATH", "ONNX_RUNTIME_MODEL_PATH" }),
        };

    public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    public ValueTask<ProviderClientConstruction<IChatClient>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        ProviderClientConstructionUtilities.RequireAnonymous(context.CredentialBinding);
        var onnxConfig = ReadConfig(context.EffectiveConfig);
        var modelPath = onnxConfig?.ModelPath;

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
            PromptFormatter = onnxConfig?.PromptFormatter
        };

        IChatClient finalClient = CreateOnnxChatClient(modelPath, onnxConfig, clientOptions);
        if (onnxConfig?.EnableStructuredToolCalling == true)
        {
            finalClient = new StructuredToolCallingOnnxRuntimeChatClient(finalClient);
        }

        return ValueTask.FromResult(new ProviderClientConstruction<IChatClient>
        {
            Client = finalClient,
            Owner = ProviderClientConstructionUtilities.Own(finalClient)
        });
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

    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();
        if (config.Family != ProviderClientFamily.Chat)
            errors.Add("ONNX Runtime supports only chat.");
        var onnxConfig = ReadConfig(config);
        var modelPath = onnxConfig?.ModelPath;

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

        if (config.ExecutionProviderOptions is not null)
        {
            foreach (var provider in config.ExecutionProviderOptions)
            {
                if (string.IsNullOrWhiteSpace(provider.Key))
                    errors.Add("ExecutionProviderOptions cannot contain an empty provider name.");

                foreach (var option in provider.Value)
                {
                    if (string.IsNullOrWhiteSpace(option.Key))
                        errors.Add($"ExecutionProviderOptions for '{provider.Key}' cannot contain an empty option name.");
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

            if (config?.ExecutionProviderOptions is not null)
            {
                foreach (var provider in config.ExecutionProviderOptions)
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
           config?.ExecutionProviderOptions is { Count: > 0 } ||
           config?.HardwareDeviceType is not null ||
           config?.HardwareDeviceId is not null ||
           config?.HardwareVendorId is not null;

    private static OnnxRuntimeProviderConfig? ReadConfig(EffectiveProviderClientConfig config) =>
        config.ProviderConfiguration.CanonicalPayload.IsEmpty
            ? null
            : JsonSerializer.Deserialize(
                config.ProviderConfiguration.CanonicalPayload.AsSpan(),
                OnnxRuntimeJsonContext.Default.OnnxRuntimeProviderConfig);
}
