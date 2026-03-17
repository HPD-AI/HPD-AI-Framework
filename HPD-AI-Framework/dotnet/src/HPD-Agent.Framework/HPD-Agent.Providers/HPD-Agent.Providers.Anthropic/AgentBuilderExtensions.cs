using System;
using HPD.Agent;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Anthropic;

/// <summary>
/// Extension methods for AgentBuilder to configure Anthropic (Claude) as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Anthropic (Claude) as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="model">The model to use (e.g., "claude-sonnet-4-5-20250929")</param>
    /// <param name="apiKey">Optional API key. If not provided, will try to resolve from environment variables (ANTHROPIC_API_KEY) or appsettings.json</param>
    /// <param name="configure">Optional action to configure additional Anthropic-specific options</param>
    /// <param name="clientFactory">Optional factory to wrap the chat client with middleware (logging, caching, etc.)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// API Key Resolution (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: ANTHROPIC_API_KEY
    /// 3. appsettings.json: "anthropic:ApiKey" or "Anthropic:ApiKey"
    /// </para>
    /// <para>
    /// This method creates an <see cref="AnthropicProviderConfig"/> that is:
    /// - Stored in <c>ProviderConfig.ProviderOptionsJson</c> for FFI/JSON serialization
    /// - Applied during <c>AnthropicProvider.CreateChatClient()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "anthropic",
    ///     "ModelName": "claude-sonnet-4-5-20250929",
    ///     "ApiKey": "sk-ant-...",
    ///     "ProviderOptionsJson": "{\"thinkingBudgetTokens\":4096,\"serviceTier\":\"auto\",\"enablePromptCaching\":true}"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Provide API key explicitly
    /// var agent = new AgentBuilder()
    ///     .WithAnthropic("claude-sonnet-4-5-20250929", apiKey: "sk-ant-...", opts =>
    ///     {
    ///         opts.MaxTokens = 4096;
    ///         opts.Temperature = 0.7;
    ///         opts.EnablePromptCaching = true;
    ///     })
    ///     .Build();
    ///
    /// // Option 2: Auto-resolve from ANTHROPIC_API_KEY environment variable
    /// var agent = new AgentBuilder()
    ///     .WithAnthropic("claude-sonnet-4-5-20250929", configure: opts =>
    ///     {
    ///         opts.ThinkingBudgetTokens = 4096;
    ///     })
    ///     .Build();
    ///
    /// // Option 3: With middleware via ClientFactory
    /// var agent = new AgentBuilder()
    ///     .WithAnthropic("claude-sonnet-4-5-20250929",
    ///         configure: opts => opts.MaxTokens = 4096,
    ///         clientFactory: client => new LoggingChatClient(client, logger))
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithAnthropic(
        this AgentBuilder builder,
        string model,
        string? apiKey = null,
        Action<AnthropicProviderConfig>? configure = null,
        Func<IChatClient, IChatClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Anthropic provider.", nameof(model));

        // Create provider config
        // Note: API key resolution is deferred to Build() time via ISecretResolver
        var providerConfig = new AnthropicProviderConfig();

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate prompt caching TTL if enabled
        if (providerConfig.EnablePromptCaching && providerConfig.PromptCacheTTLMinutes.HasValue)
        {
            if (providerConfig.PromptCacheTTLMinutes < 1 || providerConfig.PromptCacheTTLMinutes > 60)
            {
                throw new ArgumentException(
                    "PromptCacheTTLMinutes must be between 1 and 60 minutes.",
                    nameof(configure));
            }
        }

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "anthropic",
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
}
