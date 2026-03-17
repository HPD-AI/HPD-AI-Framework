using System;
using HPD.Agent;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Mistral;

/// <summary>
/// Extension methods for AgentBuilder to configure Mistral as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Mistral AI as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="model">The model ID to use (e.g., "mistral-large-latest", "mistral-small-latest", "open-mixtral-8x7b")</param>
    /// <param name="apiKey">Optional API key. If not provided, will use MISTRAL_API_KEY environment variable</param>
    /// <param name="configure">Optional action to configure additional Mistral-specific options</param>
    /// <param name="clientFactory">Optional factory to wrap the chat client with middleware (logging, caching, etc.)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// API Key Resolution (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: MISTRAL_API_KEY
    /// 3. appsettings.json: "mistral:ApiKey" or "Mistral:ApiKey"
    /// </para>
    /// <para>
    /// This method creates a <see cref="MistralProviderConfig"/> that is:
    /// - Stored in <c>ProviderConfig.ProviderOptionsJson</c> for FFI/JSON serialization
    /// - Applied during <c>MistralProvider.CreateChatClient()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "mistral",
    ///     "ModelName": "mistral-large-latest",
    ///     "ApiKey": "your-api-key",
    ///     "ProviderOptionsJson": "{\"maxTokens\":4096,\"temperature\":0.7,\"safePrompt\":true}"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Simple configuration with API key
    /// var agent = new AgentBuilder()
    ///     .WithMistral(
    ///         model: "mistral-large-latest",
    ///         apiKey: "your-api-key")
    ///     .Build();
    ///
    /// // Option 2: With custom configuration
    /// var agent = new AgentBuilder()
    ///     .WithMistral(
    ///         model: "mistral-large-latest",
    ///         apiKey: "your-api-key",
    ///         configure: opts =>
    ///         {
    ///             opts.MaxTokens = 4096;
    ///             opts.Temperature = 0.7m;
    ///             opts.SafePrompt = true;
    ///             opts.ParallelToolCalls = true;
    ///         })
    ///     .Build();
    ///
    /// // Option 3: With JSON output mode
    /// var agent = new AgentBuilder()
    ///     .WithMistral(
    ///         model: "mistral-large-latest",
    ///         apiKey: "your-api-key",
    ///         configure: opts =>
    ///         {
    ///             opts.ResponseFormat = "json_object";
    ///             opts.Temperature = 0.3m;
    ///         })
    ///     .Build();
    ///
    /// // Option 4: With deterministic generation
    /// var agent = new AgentBuilder()
    ///     .WithMistral(
    ///         model: "mistral-small-latest",
    ///         apiKey: "your-api-key",
    ///         configure: opts =>
    ///         {
    ///             opts.RandomSeed = 12345;
    ///             opts.Temperature = 0m;
    ///         })
    ///     .Build();
    ///
    /// // Option 5: With middleware via ClientFactory
    /// var agent = new AgentBuilder()
    ///     .WithMistral(
    ///         model: "mistral-large-latest",
    ///         apiKey: "your-api-key",
    ///         configure: opts => opts.MaxTokens = 4096,
    ///         clientFactory: client => new LoggingChatClient(client, logger))
    ///     .Build();
    ///
    /// // Option 6: Auto-resolve API key from environment
    /// // Set MISTRAL_API_KEY environment variable first
    /// var agent = new AgentBuilder()
    ///     .WithMistral(model: "mistral-large-latest")
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithMistral(
        this AgentBuilder builder,
        string model,
        string? apiKey = null,
        Action<MistralProviderConfig>? configure = null,
        Func<IChatClient, IChatClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Mistral provider.", nameof(model));

        // Create provider config
        // Note: API key resolution is deferred to Build() time via ISecretResolver
        var providerConfig = new MistralProviderConfig();

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate configuration
        ValidateProviderConfig(providerConfig, configure);

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "mistral",
            ApiKey = apiKey, // May be null - AgentBuilder.Build() will resolve via ISecretResolver
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

    private static void ValidateProviderConfig(MistralProviderConfig config, Action<MistralProviderConfig>? configure)
    {
        // Validate Temperature range (0.0 - 1.0 for Mistral)
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 1))
        {
            throw new ArgumentException(
                "Temperature must be between 0.0 and 1.0 for Mistral.",
                nameof(configure));
        }

        // Validate TopP range (0.0 - 1.0)
        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
        {
            throw new ArgumentException(
                "TopP must be between 0.0 and 1.0.",
                nameof(configure));
        }

        // Validate ResponseFormat
        if (!string.IsNullOrEmpty(config.ResponseFormat))
        {
            var validFormats = new[] { "text", "json_object" };
            if (!Array.Exists(validFormats, f => f == config.ResponseFormat))
            {
                throw new ArgumentException(
                    "ResponseFormat must be one of: text, json_object.",
                    nameof(configure));
            }
        }

        // Validate ToolChoice
        if (!string.IsNullOrEmpty(config.ToolChoice))
        {
            var validChoices = new[] { "auto", "any", "none" };
            if (!Array.Exists(validChoices, c => c == config.ToolChoice))
            {
                throw new ArgumentException(
                    "ToolChoice must be one of: auto, any, none.",
                    nameof(configure));
            }
        }
    }
}
