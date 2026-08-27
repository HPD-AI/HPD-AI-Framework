using HPD.Agent.Providers;
using HPD.Agent.Providers.Cerebras;
using System;

namespace HPD.Agent;

public static class CerebrasAgentBuilderExtensions
{
    public static AgentBuilder WithCerebras(
        this AgentBuilder builder,
        string model = CerebrasProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Cerebras provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "cerebras",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "cerebras:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new CerebrasProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>Configures Cerebras with a literal runtime-only API key.</summary>
    public static AgentBuilder WithCerebras(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) =>
        builder.WithCerebras(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
