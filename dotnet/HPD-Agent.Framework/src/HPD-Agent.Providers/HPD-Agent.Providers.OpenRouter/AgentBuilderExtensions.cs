using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.OpenRouter;

/// <summary>Provides OpenRouter configuration extensions.</summary>
public static class AgentBuilderExtensions
{
    /// <summary>Configures OpenRouter chat with a canonical authentication selection.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The OpenRouter model identifier.</param>
    /// <param name="authentication">The authentication selection, or the canonical secret reference when omitted.</param>
    /// <param name="endpoint">An optional OpenRouter-compatible endpoint.</param>
    /// <param name="configure">Optional OpenRouter client configuration.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithOpenRouter(
        this AgentBuilder builder,
        string model,
        ProviderAuthentication? authentication = null,
        string? endpoint = null,
        Action<OpenRouterProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var providerConfig = new OpenRouterProviderConfig();
        configure?.Invoke(providerConfig);
        builder.ProviderRegistry.Register(new OpenRouterProvider());
        builder.Config.SetChatClientConfig(new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "openrouter",
                Backend = "platform",
                Authentication = authentication ?? new ApiKeyProviderAuthentication
                {
                    SecretKey = "openrouter:ApiKey"
                }
            },
            Endpoint = endpoint,
            ModelName = model,
            ProviderConfig = providerConfig
        });
        return builder;
    }

    /// <summary>Configures OpenRouter with a literal runtime-only API key.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The OpenRouter model identifier.</param>
    /// <param name="apiKey">The API key copied immediately into HPD-owned clearable storage.</param>
    /// <param name="endpoint">An optional OpenRouter-compatible endpoint.</param>
    /// <param name="configure">Optional OpenRouter client configuration.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithOpenRouter(
        this AgentBuilder builder,
        string model,
        ReadOnlySpan<char> apiKey,
        string? endpoint = null,
        Action<OpenRouterProviderConfig>? configure = null) =>
        builder.WithOpenRouter(model, builder.RegisterExplicitApiKey(apiKey), endpoint, configure);
}
