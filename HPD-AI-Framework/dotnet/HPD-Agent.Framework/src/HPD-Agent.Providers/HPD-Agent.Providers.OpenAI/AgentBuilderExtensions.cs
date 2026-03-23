using System;
using HPD.Agent;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// Extension methods for AgentBuilder to configure OpenAI as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use OpenAI as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="model">The model name (e.g., "gpt-4o", "gpt-4-turbo", "gpt-3.5-turbo")</param>
    /// <param name="apiKey">OpenAI API key. If not provided, will use OPENAI_API_KEY environment variable</param>
    /// <param name="configure">Optional action to configure additional OpenAI-specific options</param>
    /// <param name="clientFactory">Optional factory to wrap the chat client with middleware (logging, caching, etc.)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// API Key Resolution (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: OPENAI_API_KEY
    /// 3. appsettings.json: "openAI:ApiKey" or "OpenAI:ApiKey"
    /// </para>
    /// <para>
    /// This method creates an <see cref="OpenAIProviderConfig"/> that is:
    /// - Stored in <c>ProviderConfig.ProviderOptionsJson</c> for FFI/JSON serialization
    /// - Applied during <c>OpenAIProvider.CreateChatClient()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "openai",
    ///     "ModelName": "gpt-4o",
    ///     "ApiKey": "sk-...",
    ///     "ProviderOptionsJson": "{\"maxOutputTokenCount\":4096,\"temperature\":0.7}"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Simple usage with API key
    /// var agent = new AgentBuilder()
    ///     .WithOpenAI(
    ///         model: "gpt-4o",
    ///         apiKey: "sk-...")
    ///     .Build();
    ///
    /// // Option 2: With configuration options
    /// var agent = new AgentBuilder()
    ///     .WithOpenAI(
    ///         model: "gpt-4o",
    ///         apiKey: "sk-...",
    ///         configure: opts =>
    ///         {
    ///             opts.MaxOutputTokenCount = 4096;
    ///             opts.Temperature = 0.7f;
    ///             opts.TopP = 0.95f;
    ///         })
    ///     .Build();
    ///
    /// // Option 3: With structured JSON output
    /// var agent = new AgentBuilder()
    ///     .WithOpenAI(
    ///         model: "gpt-4o",
    ///         apiKey: "sk-...",
    ///         configure: opts =>
    ///         {
    ///             opts.ResponseFormat = "json_schema";
    ///             opts.JsonSchemaName = "UserInfo";
    ///             opts.JsonSchema = @"{
    ///                 ""type"": ""object"",
    ///                 ""properties"": {
    ///                     ""name"": { ""type"": ""string"" },
    ///                     ""age"": { ""type"": ""number"" }
    ///                 }
    ///             }";
    ///             opts.JsonSchemaIsStrict = true;
    ///         })
    ///     .Build();
    ///
    /// // Option 4: With reasoning model (o1)
    /// var agent = new AgentBuilder()
    ///     .WithOpenAI(
    ///         model: "o1-preview",
    ///         apiKey: "sk-...",
    ///         configure: opts =>
    ///         {
    ///             opts.ReasoningEffortLevel = "high";
    ///         })
    ///     .Build();
    ///
    /// // Option 5: With audio output (gpt-4o-audio-preview)
    /// var agent = new AgentBuilder()
    ///     .WithOpenAI(
    ///         model: "gpt-4o-audio-preview",
    ///         apiKey: "sk-...",
    ///         configure: opts =>
    ///         {
    ///             opts.ResponseModalities = "text,audio";
    ///             opts.AudioVoice = "alloy";
    ///             opts.AudioFormat = "mp3";
    ///         })
    ///     .Build();
    ///
    /// // Option 6: Auto-resolve from environment variables
    /// // Set OPENAI_API_KEY environment variable
    /// var agent = new AgentBuilder()
    ///     .WithOpenAI(model: "gpt-4o")
    ///     .Build();
    ///
    /// // Option 7: With client middleware
    /// var agent = new AgentBuilder()
    ///     .WithOpenAI(
    ///         model: "gpt-4o",
    ///         apiKey: "sk-...",
    ///         configure: opts => opts.MaxOutputTokenCount = 4096,
    ///         clientFactory: client => new LoggingChatClient(client, logger))
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithOpenAI(
        this AgentBuilder builder,
        string model,
        string? apiKey = null,
        Action<OpenAIProviderConfig>? configure = null,
        Func<IChatClient, IChatClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for OpenAI provider.", nameof(model));

        // Note: API key resolution is now handled by ISecretResolver in the provider's CreateChatClient method
        // We store the explicit override (if provided) in config.ApiKey, and ISecretResolver will handle the resolution chain

        // Create provider config
        var providerConfig = new OpenAIProviderConfig();

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate configuration
        ValidateProviderConfig(providerConfig, configure);

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "openai",
            ApiKey = apiKey, // Store explicit override; ISecretResolver will handle resolution
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

    /// <summary>
    /// Configures the agent to use Azure OpenAI as the AI provider.
    /// Note: For modern Azure AI Projects/Foundry, use the AzureAI provider instead.
    /// This is for traditional Azure OpenAI endpoints only.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="endpoint">The Azure OpenAI endpoint URL (e.g., "https://your-resource.openai.azure.com")</param>
    /// <param name="model">The deployment name</param>
    /// <param name="apiKey">Azure OpenAI API key. If not provided, will use AZURE_OPENAI_API_KEY environment variable</param>
    /// <param name="configure">Optional action to configure additional options</param>
    /// <param name="clientFactory">Optional factory to wrap the chat client with middleware</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithAzureOpenAI(
    ///         endpoint: "https://my-resource.openai.azure.com",
    ///         model: "gpt-4",
    ///         apiKey: "your-api-key",
    ///         configure: opts =>
    ///         {
    ///             opts.MaxOutputTokenCount = 4096;
    ///             opts.Temperature = 0.7f;
    ///         })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithAzureOpenAI(
        this AgentBuilder builder,
        string endpoint,
        string model,
        string? apiKey = null,
        Action<OpenAIProviderConfig>? configure = null,
        Func<IChatClient, IChatClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required for Azure OpenAI provider.", nameof(endpoint));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Azure OpenAI provider.", nameof(model));

        // Note: API key resolution is now handled by ISecretResolver in the provider's CreateChatClient method
        // We store the explicit override (if provided) in config.ApiKey, and ISecretResolver will handle the resolution chain

        // Create provider config
        var providerConfig = new OpenAIProviderConfig();

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate configuration
        ValidateProviderConfig(providerConfig, configure);

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "azure-openai",
            Endpoint = endpoint,
            ApiKey = apiKey, // Store explicit override; ISecretResolver will handle resolution
            ModelName = model
        };

        // Store the typed config
        builder.Config.Provider.SetTypedProviderConfig(providerConfig);

        // Store the client factory if provided
        if (clientFactory != null)
        {
            builder.Config.Provider.AdditionalProperties ??= new System.Collections.Generic.Dictionary<string, object>();
            builder.Config.Provider.AdditionalProperties["ClientFactory"] = clientFactory;
        }

        return builder;
    }

    private static void ValidateProviderConfig(OpenAIProviderConfig config, Action<OpenAIProviderConfig>? configure)
    {
        // Validate Temperature range
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

        // Validate FrequencyPenalty range
        if (config.FrequencyPenalty.HasValue && (config.FrequencyPenalty.Value < -2 || config.FrequencyPenalty.Value > 2))
        {
            throw new ArgumentException(
                "FrequencyPenalty must be between -2 and 2.",
                nameof(configure));
        }

        // Validate PresencePenalty range
        if (config.PresencePenalty.HasValue && (config.PresencePenalty.Value < -2 || config.PresencePenalty.Value > 2))
        {
            throw new ArgumentException(
                "PresencePenalty must be between -2 and 2.",
                nameof(configure));
        }

        // Validate StopSequences count
        if (config.StopSequences != null && config.StopSequences.Count > 4)
        {
            throw new ArgumentException(
                "Maximum of 4 stop sequences allowed.",
                nameof(configure));
        }

        // Validate TopLogProbabilityCount range
        if (config.TopLogProbabilityCount.HasValue && (config.TopLogProbabilityCount.Value < 0 || config.TopLogProbabilityCount.Value > 20))
        {
            throw new ArgumentException(
                "TopLogProbabilityCount must be between 0 and 20.",
                nameof(configure));
        }

        // Validate ResponseFormat
        if (!string.IsNullOrEmpty(config.ResponseFormat))
        {
            var validFormats = new[] { "text", "json_object", "json_schema" };
            if (!Array.Exists(validFormats, f => f == config.ResponseFormat))
            {
                throw new ArgumentException(
                    "ResponseFormat must be one of: text, json_object, json_schema.",
                    nameof(configure));
            }

            // Validate json_schema requirements
            if (config.ResponseFormat == "json_schema")
            {
                if (string.IsNullOrEmpty(config.JsonSchemaName))
                {
                    throw new ArgumentException(
                        "JsonSchemaName is required when ResponseFormat is json_schema.",
                        nameof(configure));
                }
                if (string.IsNullOrEmpty(config.JsonSchema))
                {
                    throw new ArgumentException(
                        "JsonSchema is required when ResponseFormat is json_schema.",
                        nameof(configure));
                }
            }
        }

        // Validate ToolChoice
        if (!string.IsNullOrEmpty(config.ToolChoice))
        {
            var validChoices = new[] { "auto", "none", "required" };
            if (!Array.Exists(validChoices, c => c == config.ToolChoice))
            {
                throw new ArgumentException(
                    "ToolChoice must be one of: auto, none, required.",
                    nameof(configure));
            }
        }

        // Validate ReasoningEffortLevel
        if (!string.IsNullOrEmpty(config.ReasoningEffortLevel))
        {
            var validLevels = new[] { "low", "medium", "high", "minimal" };
            if (!Array.Exists(validLevels, l => l == config.ReasoningEffortLevel))
            {
                throw new ArgumentException(
                    "ReasoningEffortLevel must be one of: low, medium, high, minimal.",
                    nameof(configure));
            }
        }

        // Validate AudioVoice
        if (!string.IsNullOrEmpty(config.AudioVoice))
        {
            var validVoices = new[] { "alloy", "ash", "ballad", "coral", "echo", "sage", "shimmer", "verse" };
            if (!Array.Exists(validVoices, v => v == config.AudioVoice))
            {
                throw new ArgumentException(
                    "AudioVoice must be one of: alloy, ash, ballad, coral, echo, sage, shimmer, verse.",
                    nameof(configure));
            }
        }

        // Validate AudioFormat
        if (!string.IsNullOrEmpty(config.AudioFormat))
        {
            var validFormats = new[] { "wav", "mp3", "flac", "opus", "pcm16" };
            if (!Array.Exists(validFormats, f => f == config.AudioFormat))
            {
                throw new ArgumentException(
                    "AudioFormat must be one of: wav, mp3, flac, opus, pcm16.",
                    nameof(configure));
            }
        }

        // Validate ServiceTier
        if (!string.IsNullOrEmpty(config.ServiceTier))
        {
            var validTiers = new[] { "auto", "default" };
            if (!Array.Exists(validTiers, t => t == config.ServiceTier))
            {
                throw new ArgumentException(
                    "ServiceTier must be one of: auto, default.",
                    nameof(configure));
            }
        }
    }
}
