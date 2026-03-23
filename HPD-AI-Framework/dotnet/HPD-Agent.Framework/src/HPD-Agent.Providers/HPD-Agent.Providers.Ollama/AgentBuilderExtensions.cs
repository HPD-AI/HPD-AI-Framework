using System;
using HPD.Agent;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Ollama;

/// <summary>
/// Extension methods for AgentBuilder to configure Ollama as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Ollama as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="model">The model name (e.g., "llama3:8b", "mistral", "qwen3:4b")</param>
    /// <param name="endpoint">Optional Ollama endpoint URL. Defaults to http://localhost:11434</param>
    /// <param name="configure">Optional action to configure additional Ollama-specific options</param>
    /// <param name="clientFactory">Optional factory to wrap the chat client with middleware (logging, caching, etc.)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// Endpoint Resolution (in priority order):
    /// 1. Explicit endpoint parameter
    /// 2. Environment variable: OLLAMA_ENDPOINT or OLLAMA_HOST
    /// 3. Default: http://localhost:11434
    /// </para>
    /// <para>
    /// This method creates an <see cref="OllamaProviderConfig"/> that is:
    /// - Stored in <c>ProviderConfig.ProviderOptionsJson</c> for FFI/JSON serialization
    /// - Applied during <c>OllamaProvider.CreateChatClient()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "ollama",
    ///     "ModelName": "llama3:8b",
    ///     "Endpoint": "http://localhost:11434",
    ///     "ProviderOptionsJson": "{\"temperature\":0.7,\"numPredict\":2048}"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Basic usage with defaults
    /// var agent = new AgentBuilder()
    ///     .WithOllama(model: "llama3:8b")
    ///     .Build();
    ///
    /// // Option 2: Custom endpoint
    /// var agent = new AgentBuilder()
    ///     .WithOllama(
    ///         model: "mistral",
    ///         endpoint: "http://my-server:11434")
    ///     .Build();
    ///
    /// // Option 3: With configuration options
    /// var agent = new AgentBuilder()
    ///     .WithOllama(
    ///         model: "llama3:8b",
    ///         configure: opts =>
    ///         {
    ///             opts.Temperature = 0.7f;
    ///             opts.NumPredict = 2048;
    ///             opts.NumCtx = 4096;
    ///             opts.TopP = 0.9f;
    ///             opts.TopK = 40;
    ///         })
    ///     .Build();
    ///
    /// // Option 4: Deterministic generation
    /// var agent = new AgentBuilder()
    ///     .WithOllama(
    ///         model: "llama3:8b",
    ///         configure: opts =>
    ///         {
    ///             opts.Seed = 12345;
    ///             opts.Temperature = 0.0f;
    ///         })
    ///     .Build();
    ///
    /// // Option 5: JSON output
    /// var agent = new AgentBuilder()
    ///     .WithOllama(
    ///         model: "llama3:8b",
    ///         configure: opts =>
    ///         {
    ///             opts.Format = "json";
    ///         })
    ///     .Build();
    ///
    /// // Option 6: Reasoning model with thinking enabled
    /// var agent = new AgentBuilder()
    ///     .WithOllama(
    ///         model: "deepseek-r1:8b",
    ///         configure: opts =>
    ///         {
    ///             opts.Think = true; // or "high", "medium", "low"
    ///         })
    ///     .Build();
    ///
    /// // Option 7: Performance tuning for GPU
    /// var agent = new AgentBuilder()
    ///     .WithOllama(
    ///         model: "llama3:70b",
    ///         configure: opts =>
    ///         {
    ///             opts.NumGpu = 2;
    ///             opts.MainGpu = 0;
    ///             opts.NumThread = 8;
    ///         })
    ///     .Build();
    ///
    /// // Option 8: With middleware via ClientFactory
    /// var agent = new AgentBuilder()
    ///     .WithOllama(
    ///         model: "llama3:8b",
    ///         configure: opts => opts.Temperature = 0.7f,
    ///         clientFactory: client => new LoggingChatClient(client, logger))
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithOllama(
        this AgentBuilder builder,
        string model,
        string? endpoint = null,
        Action<OllamaProviderConfig>? configure = null,
        Func<IChatClient, IChatClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model name is required for Ollama provider.", nameof(model));

        // Resolve endpoint from multiple sources
        var resolvedEndpoint = ResolveEndpoint(endpoint);

        // Create provider config
        var providerConfig = new OllamaProviderConfig();

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate configuration
        ValidateProviderConfig(providerConfig, configure);

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "ollama",
            Endpoint = resolvedEndpoint,
            ModelName = model
        };

        // Store the typed config
        builder.Config.Provider.SetTypedProviderConfig(providerConfig);

        // Store the client factory if provided
        if (clientFactory != null)
        {
            // Store in AdditionalProperties for the provider to retrieve during CreateChatClient
            builder.Config.Provider.AdditionalProperties ??= new System.Collections.Generic.Dictionary<string, object>();
            builder.Config.Provider.AdditionalProperties["ClientFactory"] = clientFactory;
        }

        return builder;
    }

    private static string ResolveEndpoint(string? explicitEndpoint)
    {
        // Priority 1: Explicit parameter
        if (!string.IsNullOrWhiteSpace(explicitEndpoint))
            return explicitEndpoint;

        // Priority 2: Environment variables
        var envEndpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("OLLAMA_HOST");

        if (!string.IsNullOrWhiteSpace(envEndpoint))
            return envEndpoint;

        // Priority 3: Default
        return "http://localhost:11434";
    }

    private static void ValidateProviderConfig(OllamaProviderConfig config, Action<OllamaProviderConfig>? configure)
    {
        // Validate Temperature range (Ollama typically uses 0.0-2.0 like OpenAI)
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 2))
        {
            throw new ArgumentException(
                "Temperature must be between 0 and 2.",
                nameof(configure));
        }

        // Validate TopP range
        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
        {
            throw new ArgumentException(
                "TopP must be between 0 and 1.",
                nameof(configure));
        }

        // Validate MinP range
        if (config.MinP.HasValue && (config.MinP.Value < 0 || config.MinP.Value > 1))
        {
            throw new ArgumentException(
                "MinP must be between 0 and 1.",
                nameof(configure));
        }

        // Validate TypicalP range
        if (config.TypicalP.HasValue && (config.TypicalP.Value < 0 || config.TypicalP.Value > 1))
        {
            throw new ArgumentException(
                "TypicalP must be between 0 and 1.",
                nameof(configure));
        }

        // Validate TfsZ range
        if (config.TfsZ.HasValue && config.TfsZ.Value < 0)
        {
            throw new ArgumentException(
                "TfsZ must be greater than or equal to 0.",
                nameof(configure));
        }

        // Validate RepeatPenalty range
        if (config.RepeatPenalty.HasValue && config.RepeatPenalty.Value < 0)
        {
            throw new ArgumentException(
                "RepeatPenalty must be greater than or equal to 0.",
                nameof(configure));
        }

        // Validate PresencePenalty range (Ollama uses 0.0-2.0 like OpenAI)
        if (config.PresencePenalty.HasValue && (config.PresencePenalty.Value < 0 || config.PresencePenalty.Value > 2))
        {
            throw new ArgumentException(
                "PresencePenalty must be between 0 and 2.",
                nameof(configure));
        }

        // Validate FrequencyPenalty range (Ollama uses 0.0-2.0 like OpenAI)
        if (config.FrequencyPenalty.HasValue && (config.FrequencyPenalty.Value < 0 || config.FrequencyPenalty.Value > 2))
        {
            throw new ArgumentException(
                "FrequencyPenalty must be between 0 and 2.",
                nameof(configure));
        }

        // Validate MiroStat values
        if (config.MiroStat.HasValue && (config.MiroStat.Value < 0 || config.MiroStat.Value > 2))
        {
            throw new ArgumentException(
                "MiroStat must be 0 (disabled), 1 (Mirostat), or 2 (Mirostat 2.0).",
                nameof(configure));
        }

        // Validate MiroStatEta range
        if (config.MiroStatEta.HasValue && config.MiroStatEta.Value < 0)
        {
            throw new ArgumentException(
                "MiroStatEta must be greater than or equal to 0.",
                nameof(configure));
        }

        // Validate MiroStatTau range
        if (config.MiroStatTau.HasValue && config.MiroStatTau.Value < 0)
        {
            throw new ArgumentException(
                "MiroStatTau must be greater than or equal to 0.",
                nameof(configure));
        }

        // Validate Format
        if (!string.IsNullOrEmpty(config.Format))
        {
            // Ollama accepts "json" or a JSON schema object
            // For simplicity, we just check if it's not empty
            if (string.IsNullOrWhiteSpace(config.Format))
            {
                throw new ArgumentException(
                    "Format must be 'json' or a valid JSON schema.",
                    nameof(configure));
            }
        }

        // Validate numeric ranges
        if (config.NumPredict.HasValue && config.NumPredict.Value < -2)
        {
            throw new ArgumentException(
                "NumPredict must be greater than or equal to -2 (-2 = fill context, -1 = infinite, 0+ = specific count).",
                nameof(configure));
        }

        if (config.NumCtx.HasValue && config.NumCtx.Value < 1)
        {
            throw new ArgumentException(
                "NumCtx must be greater than 0.",
                nameof(configure));
        }

        if (config.TopK.HasValue && config.TopK.Value < 1)
        {
            throw new ArgumentException(
                "TopK must be greater than 0.",
                nameof(configure));
        }
    }
}
