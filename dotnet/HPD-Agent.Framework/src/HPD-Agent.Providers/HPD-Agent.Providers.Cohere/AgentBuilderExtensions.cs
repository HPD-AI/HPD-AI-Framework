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
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Cohere provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "cohere",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "cohere:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new CohereProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>Configures Cohere chat with a literal runtime-only API key.</summary>
    public static AgentBuilder WithCohere(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) =>
        builder.WithCohere(model, builder.RegisterExplicitApiKey(apiKey), endpoint);

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
        options.ApplyTo(chatConfig);

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
        ProviderAuthentication? authentication = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Cohere embeddings.", nameof(model));

        var embeddingConfig = new EmbeddingsClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "cohere",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "cohere:ApiKey" }
            },
            ModelName = model
        };

        builder.ProviderRegistry.Register(new CohereProvider());
        builder.Config.SetClientConfig(ProviderClientFamily.Embeddings, embeddingConfig);
        return builder;
    }

    /// <summary>Configures Cohere embeddings with a literal runtime-only API key.</summary>
    public static AgentBuilder WithCohereEmbeddings(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey) =>
        builder.WithCohereEmbeddings(model, builder.RegisterExplicitApiKey(apiKey));
}
