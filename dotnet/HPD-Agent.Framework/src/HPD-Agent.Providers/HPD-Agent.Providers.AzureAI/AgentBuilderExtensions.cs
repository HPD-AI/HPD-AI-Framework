using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.AzureAI;

/// <summary>Provides Azure AI configuration extensions.</summary>
public static class AgentBuilderExtensions
{
    /// <summary>Configures Azure AI chat with an atomic authentication selection.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="endpoint">The Azure AI Projects or Azure OpenAI endpoint.</param>
    /// <param name="model">The Azure deployment name.</param>
    /// <param name="authentication">The API-key secret reference or registered external identity.</param>
    /// <param name="configure">An optional Azure SDK configuration callback.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithAzureAI(
        this AgentBuilder builder,
        string endpoint,
        string model,
        ProviderAuthentication authentication,
        Action<AzureAIProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required for Azure AI.", nameof(endpoint));
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Azure AI.", nameof(model));
        ArgumentNullException.ThrowIfNull(authentication);

        var providerConfig = new AzureAIProviderConfig();
        configure?.Invoke(providerConfig);
        builder.ProviderRegistry.Register(new AzureAIProvider());
        builder.Config.SetChatClientConfig(new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "azure-ai",
                Backend = "azure",
                Authentication = authentication
            },
            Endpoint = endpoint,
            ModelName = model,
            ProviderConfig = providerConfig
        });
        return builder;
    }

    /// <summary>Configures Azure AI with a literal runtime-only API key.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="endpoint">The Azure AI endpoint.</param>
    /// <param name="model">The Azure deployment name.</param>
    /// <param name="apiKey">The API key copied immediately into HPD-owned clearable storage.</param>
    /// <param name="configure">An optional Azure SDK configuration callback.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithAzureAI(
        this AgentBuilder builder,
        string endpoint,
        string model,
        ReadOnlySpan<char> apiKey,
        Action<AzureAIProviderConfig>? configure = null) =>
        builder.WithAzureAI(endpoint, model, builder.RegisterExplicitApiKey(apiKey), configure);
}
