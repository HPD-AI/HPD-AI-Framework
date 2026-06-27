using System;
using System.Collections.Generic;
using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Together;

/// <summary>
/// Extension methods for AgentBuilder to configure Together AI.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Together AI as the chat provider.
    /// </summary>
    public static AgentBuilder WithTogether(
        this AgentBuilder builder,
        string model = "meta-llama/Llama-3.3-70B-Instruct-Turbo",
        string? apiKey = null,
        string? endpoint = null,
        Action<TogetherProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Together AI provider.", nameof(model));

        var providerConfig = new TogetherProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "together",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

    /// <summary>
    /// Configures the agent to use Together AI as the embedding provider.
    /// </summary>
    public static AgentBuilder WithTogetherEmbeddings(
        this AgentBuilder builder,
        string model = "BAAI/bge-base-en-v1.5",
        string? apiKey = null,
        string? endpoint = null,
        Action<TogetherProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Together AI embeddings.", nameof(model));

        var providerConfig = new TogetherProviderConfig
        {
            EmbeddingModelId = model
        };
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var embeddingConfig = new ClientProviderConfig
        {
            ProviderKey = "together",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.Embeddings, embeddingConfig);
        embeddingConfig.SetProviderConfig(providerConfig, ProviderClientFamily.Embeddings);

        return builder;
    }

    private static void ValidateProviderConfig(TogetherProviderConfig config, Action<TogetherProviderConfig>? configure)
    {
        var errors = new List<string>();
        TogetherProvider.ValidateProviderOptions(config, errors);

        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
    }
}
