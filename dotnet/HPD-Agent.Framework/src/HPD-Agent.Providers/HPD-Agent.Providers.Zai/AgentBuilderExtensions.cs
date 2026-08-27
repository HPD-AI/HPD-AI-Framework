using HPD.Agent.Providers;
using HPD.Agent.Providers.Zai;
using System;

namespace HPD.Agent;

public static class ZaiAgentBuilderExtensions
{
    public static AgentBuilder WithZai(
        this AgentBuilder builder,
        string model = ZaiProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Z.AI provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "zai",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "zai:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new ZaiProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures Z.ai with a literal runtime-only API key.</summary>
    public static AgentBuilder WithZai(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithZai(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
