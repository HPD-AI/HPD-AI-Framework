using HPD.Agent.Providers;
using HPD.Agent.Providers.Hyperbolic;
using System;

namespace HPD.Agent;

public static class HyperbolicAgentBuilderExtensions
{
    public static AgentBuilder WithHyperbolic(
        this AgentBuilder builder,
        string model = HyperbolicProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Hyperbolic provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "hyperbolic",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "hyperbolic:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new HyperbolicProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures Hyperbolic with a literal runtime-only API key.</summary>
    public static AgentBuilder WithHyperbolic(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithHyperbolic(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
