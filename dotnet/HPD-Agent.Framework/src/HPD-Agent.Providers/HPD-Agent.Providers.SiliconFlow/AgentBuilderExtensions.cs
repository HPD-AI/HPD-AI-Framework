using HPD.Agent.Providers;
using HPD.Agent.Providers.SiliconFlow;
using System;

namespace HPD.Agent;

public static class SiliconFlowAgentBuilderExtensions
{
    public static AgentBuilder WithSiliconFlow(
        this AgentBuilder builder,
        string model = SiliconFlowProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for SiliconFlow provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "siliconflow",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "siliconflow:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new SiliconFlowProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures SiliconFlow with a literal runtime-only API key.</summary>
    public static AgentBuilder WithSiliconFlow(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithSiliconFlow(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
