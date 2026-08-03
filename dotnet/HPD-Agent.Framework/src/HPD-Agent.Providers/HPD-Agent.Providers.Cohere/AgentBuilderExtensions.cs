using System.Collections.Generic;
using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Cohere;

/// <summary>
/// Extension methods for AgentBuilder to configure Cohere as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Cohere as the chat provider.
    /// </summary>
    public static AgentBuilder WithCohere(
        this AgentBuilder builder,
        string model = "command-r-plus",
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Cohere provider.", nameof(model));

        var chatConfig = new ProviderClientConfig
        {
            ProviderKey = "cohere",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>
    /// Adds Cohere-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithCohereChatRequestOptions(
        this AgentBuilder builder,
        CohereChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var chatConfig = builder.Config.EnsureChatClientConfig();
        chatConfig.ChatDefaults ??= new ChatRunConfig();
        options.ApplyTo(chatConfig.ChatDefaults);

        return builder;
    }

    /// <summary>
    /// Adds Cohere-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithCohereChatRequestOptions(
        this AgentBuilder builder,
        Action<CohereChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new CohereChatRequestOptions();
        configure(options);
        return builder.WithCohereChatRequestOptions(options);
    }

    /// <summary>
    /// Configures the agent to use Cohere as the embedding provider.
    /// </summary>
    public static AgentBuilder WithCohereEmbeddings(
        this AgentBuilder builder,
        string model = "embed-english-v3.0",
        string? apiKey = null,
        Action<CohereProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Cohere embeddings.", nameof(model));

        var providerConfig = new CohereProviderConfig
        {
            EmbeddingModelId = model
        };
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var embeddingConfig = new ProviderClientConfig
        {
            ProviderKey = "cohere",
            ApiKey = apiKey,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.Embeddings, embeddingConfig);
        embeddingConfig.SetProviderConfig(providerConfig, ProviderClientFamily.Embeddings);

        return builder;
    }

    private static void ValidateProviderConfig(CohereProviderConfig config, Action<CohereProviderConfig>? configure)
    {
        var errors = new List<string>();
        CohereProvider.ValidateProviderOptions(config, errors);

        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
    }
}
