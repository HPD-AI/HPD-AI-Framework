using HPD.Agent.Providers;
using HPD.Agent.Providers.DeepSeek;
using System;

namespace HPD.Agent;

public static class DeepSeekAgentBuilderExtensions
{
    public static AgentBuilder WithDeepSeek(
        this AgentBuilder builder,
        string model = DeepSeekProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required for DeepSeek provider.", nameof(model));
        }

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "deepseek",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "deepseek:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new DeepSeekProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>Configures DeepSeek with a literal runtime-only API key.</summary>
    public static AgentBuilder WithDeepSeek(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) =>
        builder.WithDeepSeek(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
