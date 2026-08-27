using HPD.Agent;
using HPD.Agent.Providers;
using System;

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
    /// <param name="model">The model ID to use (e.g., "mistral-large-latest", "mistral-small-latest", "open-mixtral-8x7b")</param>    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// The Mistral provider targets net10.0 because the generated Mistral SDK package targets net10.0.
    /// </para>
    /// <para>
    /// API Key Resolution (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: MISTRAL_API_KEY
    /// 3. appsettings.json: "mistral:ApiKey" or "Mistral:ApiKey"
    /// </para>
    /// <para>
    /// Runtime chat behavior is configured through <see cref="AgentConfig.Clients"/>
    /// and per-run <see cref="AgentRunConfig.Clients"/>.
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
    /// // Option 2: Auto-resolve API key from environment
    /// // Set MISTRAL_API_KEY environment variable first
    /// var agent = new AgentBuilder()
    ///     .WithMistral(model: "mistral-large-latest")
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithMistral(
        this AgentBuilder builder,
        string model,
        ProviderAuthentication? authentication = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Mistral provider.", nameof(model));

        // Build provider config
        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "mistral",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "mistral:ApiKey" }
            },
            ModelName = model
        };

        builder.ProviderRegistry.Register(new MistralProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>Configures Mistral with a literal runtime-only API key.</summary>
    public static AgentBuilder WithMistral(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey) =>
        builder.WithMistral(model, builder.RegisterExplicitApiKey(apiKey));

    /// <summary>
    /// Applies Mistral-specific per-request defaults to the configured chat client.
    /// </summary>
    public static AgentBuilder WithMistralChatRequestOptions(
        this AgentBuilder builder,
        MistralChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var chatConfig = builder.Config.EnsureChatClientConfig();
        options.ApplyTo(chatConfig);
        return builder;
    }

    /// <summary>
    /// Applies Mistral-specific per-request defaults to the configured chat client.
    /// </summary>
    public static AgentBuilder WithMistralChatRequestOptions(
        this AgentBuilder builder,
        Action<MistralChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new MistralChatRequestOptions();
        configure(options);
        return builder.WithMistralChatRequestOptions(options);
    }
}
