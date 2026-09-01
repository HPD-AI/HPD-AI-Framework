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
        ProviderAuthentication? authentication = null,
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
            Provider = new ProviderReference
            {
                Key = "dashscope",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "dashscope:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new DashScopeProvider());
        builder.Config.SetChatClientConfig(chatConfig);
        chatConfig.ProviderConfig = providerConfig;

        return builder;
    }
    /// <summary>Configures DashScope chat with a literal runtime-only API key.</summary>
    public static AgentBuilder WithDashScope(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null, Action<DashScopeProviderConfig>? configure = null) => builder.WithDashScope(model, builder.RegisterExplicitApiKey(apiKey), endpoint, configure);

    /// <summary>
    /// Configures the agent to use DashScope as the embedding provider.
    /// </summary>
    public static AgentBuilder WithDashScopeEmbeddings(
        this AgentBuilder builder,
        string model = "text-embedding-v4",
        ProviderAuthentication? authentication = null,
        string? endpoint = null,
        int? dimensions = null,
        Action<DashScopeProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for DashScope embeddings.", nameof(model));

        if (dimensions is <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "Dimensions must be greater than zero.");

        var providerConfig = new DashScopeProviderConfig();
        configure?.Invoke(providerConfig);
        ValidateProviderConfig(providerConfig, configure);

        var embeddingConfig = new EmbeddingsClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "dashscope",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "dashscope:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model,
            Dimensions = dimensions
        };

        builder.ProviderRegistry.Register(new DashScopeProvider());
        builder.Config.SetClientConfig(ProviderClientFamily.Embeddings, embeddingConfig);
        embeddingConfig.ProviderConfig = providerConfig;

        return builder;
    }
    /// <summary>Configures DashScope embeddings with a literal runtime-only API key.</summary>
    public static AgentBuilder WithDashScopeEmbeddings(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null, int? dimensions = null, Action<DashScopeProviderConfig>? configure = null) => builder.WithDashScopeEmbeddings(model, builder.RegisterExplicitApiKey(apiKey), endpoint, dimensions, configure);

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
