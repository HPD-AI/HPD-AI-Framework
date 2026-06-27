using System;
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
        Action<CohereProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Cohere provider.", nameof(model));

        var providerConfig = new CohereProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "cohere",
            ApiKey = apiKey,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
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

        var embeddingConfig = new ClientProviderConfig
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
