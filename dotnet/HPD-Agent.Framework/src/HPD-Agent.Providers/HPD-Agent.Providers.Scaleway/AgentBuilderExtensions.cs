using HPD.Agent.Providers;
using HPD.Agent.Providers.Scaleway;
using System;

namespace HPD.Agent;

public static class ScalewayAgentBuilderExtensions
{
    public static AgentBuilder WithScaleway(
        this AgentBuilder builder,
        string model = ScalewayProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Scaleway Generative APIs provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "scaleway",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "scaleway:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new ScalewayProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures Scaleway with a literal runtime-only API key.</summary>
    public static AgentBuilder WithScaleway(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithScaleway(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
