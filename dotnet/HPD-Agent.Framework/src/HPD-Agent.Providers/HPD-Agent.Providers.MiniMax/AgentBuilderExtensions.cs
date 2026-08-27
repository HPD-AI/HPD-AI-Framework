using HPD.Agent.Providers;
using HPD.Agent.Providers.MiniMax;
using System;

namespace HPD.Agent;

public static class MiniMaxAgentBuilderExtensions
{
    public static AgentBuilder WithMiniMax(
        this AgentBuilder builder,
        string model = MiniMaxProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for MiniMax provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "minimax",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "minimax:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new MiniMaxProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures MiniMax with a literal runtime-only API key.</summary>
    public static AgentBuilder WithMiniMax(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithMiniMax(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
