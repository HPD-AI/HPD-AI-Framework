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
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Together AI provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "together",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "together:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new TogetherProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures Together chat with a literal runtime-only API key.</summary>
    public static AgentBuilder WithTogether(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithTogether(model, builder.RegisterExplicitApiKey(apiKey), endpoint);

    /// <summary>
    /// Adds Together-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithTogetherChatRequestOptions(
        this AgentBuilder builder,
        TogetherChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var chatConfig = builder.Config.EnsureChatClientConfig();
        options.ApplyTo(chatConfig);

        return builder;
    }

    /// <summary>
    /// Adds Together-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithTogetherChatRequestOptions(
        this AgentBuilder builder,
        Action<TogetherChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TogetherChatRequestOptions();
        configure(options);
        return builder.WithTogetherChatRequestOptions(options);
    }

    /// <summary>
    /// Configures the agent to use Together AI as the embedding provider.
    /// </summary>
    public static AgentBuilder WithTogetherEmbeddings(
        this AgentBuilder builder,
        string model = "BAAI/bge-base-en-v1.5",
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Together AI embeddings.", nameof(model));

        var embeddingConfig = new EmbeddingsClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "together",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "together:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new TogetherProvider());
        builder.Config.SetClientConfig(ProviderClientFamily.Embeddings, embeddingConfig);
        return builder;
    }
    /// <summary>Configures Together embeddings with a literal runtime-only API key.</summary>
    public static AgentBuilder WithTogetherEmbeddings(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithTogetherEmbeddings(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
