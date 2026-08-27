using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Groq;

/// <summary>
/// Extension methods for AgentBuilder to configure Groq.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Groq as the chat provider.
    /// </summary>
    public static AgentBuilder WithGroq(
        this AgentBuilder builder,
        string model = "llama-3.3-70b-versatile",
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Groq provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "groq",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "groq:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        builder.ProviderRegistry.Register(new GroqProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>Configures Groq with a literal runtime-only API key.</summary>
    public static AgentBuilder WithGroq(this AgentBuilder builder, string model, ReadOnlySpan<char> apiKey, string? endpoint = null) =>
        builder.WithGroq(model, builder.RegisterExplicitApiKey(apiKey), endpoint);
}
