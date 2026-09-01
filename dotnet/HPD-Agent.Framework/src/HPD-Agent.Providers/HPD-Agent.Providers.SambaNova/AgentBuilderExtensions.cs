using HPD.Agent.Providers;
using HPD.Agent.Providers.SambaNova;
using System;

namespace HPD.Agent;

public static class SambaNovaAgentBuilderExtensions
{
    public static AgentBuilder WithSambaNova(
        this AgentBuilder builder,
        string model = SambaNovaProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for SambaNova provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "sambanova",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "sambanova:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new SambaNovaProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures SambaNova with a literal runtime-only API key.</summary>
    public static AgentBuilder WithSambaNova(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithSambaNova(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
