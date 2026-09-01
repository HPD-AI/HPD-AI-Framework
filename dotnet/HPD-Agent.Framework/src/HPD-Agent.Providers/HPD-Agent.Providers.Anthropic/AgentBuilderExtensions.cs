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
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// The generated provider manifest selects the portable <c>anthropic:ApiKey</c> secret reference.
    /// Hosts resolve that reference through their configured secret resolver chain.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Resolve anthropic:ApiKey from ANTHROPIC_API_KEY or another host secret source.
    /// var agent = new AgentBuilder()
    ///     .WithAnthropic("claude-sonnet-4-5-20250929")
    ///     .Build();
    ///
    /// // Configure Anthropic-specific request options as chat defaults.
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
        ProviderAuthentication? authentication = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Anthropic provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "anthropic",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "anthropic:ApiKey" }
            },
            ModelName = model
        };

        builder.ProviderRegistry.Register(new AnthropicProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>Configures Anthropic with a literal runtime-only API key.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The Anthropic model identifier.</param>
    /// <param name="apiKey">The API key copied immediately into HPD-owned clearable storage.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithAnthropic(
        this AgentBuilder builder,
        string model,
        ReadOnlySpan<char> apiKey) =>
        builder.WithAnthropic(model, builder.RegisterExplicitApiKey(apiKey));

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
        options.ApplyTo(chatConfig);

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
