using System;
using System.Collections.Generic;
using System.IO;
using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.OnnxRuntime;

/// <summary>
/// Extension methods for AgentBuilder to configure ONNX Runtime GenAI as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use ONNX Runtime GenAI for local model inference.
    /// </summary>
    public static AgentBuilder WithOnnxRuntime(
        this AgentBuilder builder,
        string modelPath,
        Action<OnnxRuntimeProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("Model path is required for ONNX Runtime provider.", nameof(modelPath));

        if (!Directory.Exists(modelPath))
            throw new DirectoryNotFoundException($"Model path does not exist: {modelPath}");

        var providerConfig = new OnnxRuntimeProviderConfig
        {
            ModelPath = modelPath
        };

        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "onnx-runtime",
            ModelName = Path.GetFileName(modelPath)
        };

        builder.ProviderRegistry.Register(new OnnxRuntimeProvider());
        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.ProviderConfig = providerConfig;

        return builder;
    }

    /// <summary>
    /// Adds ONNX Runtime GenAI-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithOnnxRuntimeChatRequestOptions(
        this AgentBuilder builder,
        OnnxRuntimeChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var chatConfig = builder.Config.EnsureChatClientConfig();
        options.ApplyTo(chatConfig);

        return builder;
    }

    /// <summary>
    /// Adds ONNX Runtime GenAI-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithOnnxRuntimeChatRequestOptions(
        this AgentBuilder builder,
        Action<OnnxRuntimeChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OnnxRuntimeChatRequestOptions();
        configure(options);
        return builder.WithOnnxRuntimeChatRequestOptions(options);
    }

    private static void ValidateProviderConfig(
        OnnxRuntimeProviderConfig config,
        Action<OnnxRuntimeProviderConfig>? configure)
    {
        if (string.IsNullOrWhiteSpace(config.ModelPath))
        {
            throw new ArgumentException(
                "ModelPath is required for ONNX Runtime provider.",
                nameof(configure));
        }

        var errors = new List<string>();
        OnnxRuntimeProvider.ValidateProviderOptions(config, errors);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
    }
}
