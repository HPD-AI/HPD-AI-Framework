using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Xai;

/// <summary>
/// Extension methods for AgentBuilder to configure xAI.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use xAI as the chat provider.
    /// </summary>
    public static AgentBuilder WithXai(
        this AgentBuilder builder,
        string model = XaiProvider.DefaultChatModel,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for xAI provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "xai",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "xai:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new XaiProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }
    /// <summary>Configures xAI with a literal runtime-only API key.</summary>
    public static AgentBuilder WithXai(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) => builder.WithXai(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
