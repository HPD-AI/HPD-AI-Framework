using System;
using System.Collections.Generic;
using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.DashScope;

/// <summary>
/// Extension methods for AgentBuilder to configure DashScope as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use DashScope as the chat provider.
    /// </summary>
    public static AgentBuilder WithDashScope(
        this AgentBuilder builder,
        string model = "qwen-plus",
        string? apiKey = null,
        string? endpoint = null,
        Action<DashScopeProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for DashScope provider.", nameof(model));

        var providerConfig = new DashScopeProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "dashscope",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.ProviderConfig = providerConfig;

        return builder;
    }

    /// <summary>
    /// Configures the agent to use DashScope as the embedding provider.
    /// </summary>
    public static AgentBuilder WithDashScopeEmbeddings(
        this AgentBuilder builder,
        string model = "text-embedding-v4",
        string? apiKey = null,
        string? endpoint = null,
        Action<DashScopeProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for DashScope embeddings.", nameof(model));

        var providerConfig = new DashScopeProviderConfig
        {
            EmbeddingModelId = model
        };
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var embeddingConfig = new EmbeddingsClientConfig
        {
            ProviderKey = "dashscope",
            ApiKey = apiKey,
            Endpoint = endpoint,
            ModelName = model
        };

        builder.Config.SetClientConfig(ProviderClientFamily.Embeddings, embeddingConfig);
        embeddingConfig.ProviderConfig = providerConfig;

        return builder;
    }

    /// <summary>
    /// Adds DashScope-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithDashScopeChatRequestOptions(
        this AgentBuilder builder,
        DashScopeChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var chatConfig = builder.Config.EnsureChatClientConfig();
        options.ApplyTo(chatConfig);

        return builder;
    }

    /// <summary>
    /// Adds DashScope-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithDashScopeChatRequestOptions(
        this AgentBuilder builder,
        Action<DashScopeChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new DashScopeChatRequestOptions();
        configure(options);
        return builder.WithDashScopeChatRequestOptions(options);
    }

    private static void ValidateProviderConfig(DashScopeProviderConfig config, Action<DashScopeProviderConfig>? configure)
    {
        var errors = new List<string>();
        DashScopeProvider.ValidateProviderOptions(config, errors);

        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(configure));
    }
}
