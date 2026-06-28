using System;
using HPD.Agent;
using HPD.Agent.Providers;

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
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// API Key Resolution (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: ANTHROPIC_API_KEY
    /// 3. appsettings.json: "anthropic:ApiKey" or "Anthropic:ApiKey"
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "anthropic",
    ///     "ModelName": "claude-sonnet-4-5-20250929",
    ///     "ApiKey": "sk-ant-..."
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Provide API key explicitly
    /// var agent = new AgentBuilder()
    ///     .WithAnthropic("claude-sonnet-4-5-20250929", apiKey: "sk-ant-...")
    ///     .Build();
    ///
    /// // Option 2: Auto-resolve from ANTHROPIC_API_KEY environment variable
    /// var agent = new AgentBuilder()
    ///     .WithAnthropic("claude-sonnet-4-5-20250929")
    ///     .Build();
    ///
    /// // Option 3: Configure Anthropic-specific request options as chat defaults
    /// var agent = new AgentBuilder()
    ///     .WithAnthropic("claude-sonnet-4-5-20250929")
    ///     .WithAnthropicChatRequestOptions(opts =>
    ///     {
    ///         opts.ServiceTier = AnthropicServiceTier.Auto;
    ///         opts.ThinkingBudgetTokens = 4096;
    ///         opts.CacheControl = new AnthropicCacheControlConfig
    ///         {
    ///             SystemMessages = AnthropicCacheTtl.OneHour,
    ///             LastUserMessage = AnthropicCacheTtl.FiveMinutes
    ///         };
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithAnthropic(
        this AgentBuilder builder,
        string model,
        string? apiKey = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Anthropic provider.", nameof(model));

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "anthropic",
            ApiKey = apiKey, // May be null - AgentBuilder.Build() will resolve via ISecretResolver
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>
    /// Adds Anthropic-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithAnthropicChatRequestOptions(
        this AgentBuilder builder,
        AnthropicChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var chatConfig = builder.Config.EnsureChatClientConfig();
        chatConfig.ChatDefaults ??= new ChatRunConfig();
        options.ApplyTo(chatConfig.ChatDefaults);

        return builder;
    }

    /// <summary>
    /// Adds Anthropic-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithAnthropicChatRequestOptions(
        this AgentBuilder builder,
        Action<AnthropicChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AnthropicChatRequestOptions();
        configure(options);
        return builder.WithAnthropicChatRequestOptions(options);
    }
}
