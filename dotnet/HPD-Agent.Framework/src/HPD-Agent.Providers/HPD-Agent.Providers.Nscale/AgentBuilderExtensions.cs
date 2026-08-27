using HPD.Agent.Providers;
using HPD.Agent.Providers.Nscale;
using System;

namespace HPD.Agent;

public static class NscaleAgentBuilderExtensions
{
    public static AgentBuilder WithNscale(
        this AgentBuilder builder,
        string model = NscaleProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Nscale provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "nscale",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "nscale:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new NscaleProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures Nscale with a literal runtime-only API key.</summary>
    public static AgentBuilder WithNscale(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithNscale(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
