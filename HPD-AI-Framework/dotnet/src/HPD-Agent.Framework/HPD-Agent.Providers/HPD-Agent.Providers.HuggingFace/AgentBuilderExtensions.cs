using System;
using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.HuggingFace;

/// <summary>
/// Extension methods for AgentBuilder to configure HuggingFace as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use HuggingFace Serverless Inference API as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="model">The model repository ID (e.g., "meta-llama/Meta-Llama-3-8B-Instruct", "mistralai/Mistral-7B-Instruct-v0.2")</param>
    /// <param name="apiKey">Optional API key (HF_TOKEN). If not provided, will look for HF_TOKEN or HUGGINGFACE_API_KEY environment variable</param>
    /// <param name="configure">Optional action to configure additional HuggingFace-specific options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// API Key Resolution (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: HF_TOKEN
    /// 3. Environment variable: HUGGINGFACE_API_KEY
    /// 4. appsettings.json: "huggingface:ApiKey" or "HuggingFace:ApiKey"
    /// </para>
    /// <para>
    /// This method creates a <see cref="HuggingFaceProviderConfig"/> that is:
    /// - Stored in <c>ProviderConfig.ProviderOptionsJson</c> for FFI/JSON serialization
    /// - Applied during <c>HuggingFaceProvider.CreateChatClient()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "huggingface",
    ///     "ModelName": "meta-llama/Meta-Llama-3-8B-Instruct",
    ///     "ApiKey": "hf_...",
    ///     "ProviderOptionsJson": "{\"maxNewTokens\":250,\"temperature\":0.7}"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Basic usage with API key
    /// var agent = new AgentBuilder()
    ///     .WithHuggingFace(
    ///         model: "meta-llama/Meta-Llama-3-8B-Instruct",
    ///         apiKey: "hf_...")
    ///     .Build();
    ///
    /// // Option 2: With configuration options
    /// var agent = new AgentBuilder()
    ///     .WithHuggingFace(
    ///         model: "mistralai/Mistral-7B-Instruct-v0.2",
    ///         apiKey: "hf_...",
    ///         configure: opts =>
    ///         {
    ///             opts.MaxNewTokens = 500;
    ///             opts.Temperature = 0.7;
    ///             opts.TopP = 0.95;
    ///             opts.RepetitionPenalty = 1.1;
    ///             opts.WaitForModel = true;
    ///         })
    ///     .Build();
    ///
    /// // Option 3: Auto-resolve API key from environment (HF_TOKEN)
    /// var agent = new AgentBuilder()
    ///     .WithHuggingFace(model: "meta-llama/Meta-Llama-3-8B-Instruct")
    ///     .Build();
    ///
    /// // Option 4: Advanced configuration
    /// var agent = new AgentBuilder()
    ///     .WithHuggingFace(
    ///         model: "bigcode/starcoder2-15b",
    ///         apiKey: "hf_...",
    ///         configure: opts =>
    ///         {
    ///             // Generation control
    ///             opts.MaxNewTokens = 1000;
    ///             opts.DoSample = true;
    ///             opts.NumReturnSequences = 1;
    ///             opts.ReturnFullText = false;
    ///
    ///             // Sampling parameters
    ///             opts.Temperature = 0.8;
    ///             opts.TopP = 0.9;
    ///             opts.TopK = 50;
    ///             opts.RepetitionPenalty = 1.2;
    ///
    ///             // Timing
    ///             opts.MaxTime = 30.0; // Max 30 seconds
    ///
    ///             // API options
    ///             opts.UseCache = false; // Disable caching for non-deterministic models
    ///             opts.WaitForModel = true; // Wait if model is loading
    ///         })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithHuggingFace(
        this AgentBuilder builder,
        string model,
        string? apiKey = null,
        Action<HuggingFaceProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model repository ID is required for HuggingFace provider.", nameof(model));

        // Note: API key resolution is now handled by ISecretResolver in the provider's CreateChatClient method
        // We store the explicit override (if provided) in config.ApiKey, and ISecretResolver will handle the resolution chain

        // Create provider config
        var providerConfig = new HuggingFaceProviderConfig();

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate configuration
        ValidateProviderConfig(providerConfig, configure);

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = model,
            ApiKey = apiKey // Store explicit override; ISecretResolver will handle resolution
        };

        // Store the typed config
        builder.Config.Provider.SetTypedProviderConfig(providerConfig);

        return builder;
    }

    private static void ValidateProviderConfig(HuggingFaceProviderConfig config, Action<HuggingFaceProviderConfig>? configure)
    {
        // Validate Temperature range (0 to 100, though typically 0-2)
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 100))
        {
            throw new ArgumentException(
                "Temperature must be between 0 and 100 (typically 0-2 for most use cases).",
                nameof(configure));
        }

        // Validate TopP range
        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
        {
            throw new ArgumentException(
                "TopP must be between 0 and 1.",
                nameof(configure));
        }

        // Validate TopK
        if (config.TopK.HasValue && config.TopK.Value < 0)
        {
            throw new ArgumentException(
                "TopK must be a positive integer.",
                nameof(configure));
        }

        // Validate RepetitionPenalty
        if (config.RepetitionPenalty.HasValue && config.RepetitionPenalty.Value < 0)
        {
            throw new ArgumentException(
                "RepetitionPenalty must be a positive number.",
                nameof(configure));
        }

        // Validate MaxNewTokens
        if (config.MaxNewTokens.HasValue && config.MaxNewTokens.Value < 1)
        {
            throw new ArgumentException(
                "MaxNewTokens must be at least 1.",
                nameof(configure));
        }

        // Validate NumReturnSequences
        if (config.NumReturnSequences.HasValue && config.NumReturnSequences.Value < 1)
        {
            throw new ArgumentException(
                "NumReturnSequences must be at least 1.",
                nameof(configure));
        }

        // Validate MaxTime
        if (config.MaxTime.HasValue && config.MaxTime.Value <= 0)
        {
            throw new ArgumentException(
                "MaxTime must be a positive number (seconds).",
                nameof(configure));
        }
    }
}
