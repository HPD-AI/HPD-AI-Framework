using HPD.Agent.Providers;
using HPD.Agent.Providers.Venice;
using System;

namespace HPD.Agent;

public static class VeniceAgentBuilderExtensions
{
    public static AgentBuilder WithVenice(
        this AgentBuilder builder,
        string model = VeniceProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for Venice.ai provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "venice",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "venice:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new VeniceProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures Venice with a literal runtime-only API key.</summary>
    public static AgentBuilder WithVenice(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithVenice(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
