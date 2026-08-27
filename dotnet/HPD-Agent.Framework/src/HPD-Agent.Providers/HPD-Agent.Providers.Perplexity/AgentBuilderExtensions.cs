using HPD.Agent.Providers;
using HPD.Agent.Providers.Perplexity;
using System;

namespace HPD.Agent;

public static class PerplexityAgentBuilderExtensions
{
    public static AgentBuilder WithPerplexity(
        this AgentBuilder builder,
        string model = PerplexityProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Perplexity provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "perplexity",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "perplexity:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new PerplexityProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures Perplexity with a literal runtime-only API key.</summary>
    public static AgentBuilder WithPerplexity(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithPerplexity(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
