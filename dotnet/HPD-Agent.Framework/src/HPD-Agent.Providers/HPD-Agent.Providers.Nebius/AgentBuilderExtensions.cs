using HPD.Agent.Providers;
using HPD.Agent.Providers.Nebius;
using System;

namespace HPD.Agent;

public static class NebiusAgentBuilderExtensions
{
    public static AgentBuilder WithNebius(
        this AgentBuilder builder,
        string model = NebiusProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Nebius Token Factory provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "nebius",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "nebius:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new NebiusProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures Nebius with a literal runtime-only API key.</summary>
    public static AgentBuilder WithNebius(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithNebius(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
