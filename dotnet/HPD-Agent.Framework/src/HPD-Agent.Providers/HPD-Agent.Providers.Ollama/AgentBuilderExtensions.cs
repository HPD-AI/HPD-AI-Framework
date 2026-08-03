using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Ollama;

/// <summary>
/// Extension methods for AgentBuilder to configure Ollama.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Ollama as the chat provider.
    /// </summary>
    public static AgentBuilder WithOllama(
        this AgentBuilder builder,
        string model,
        string? endpoint = null,
        Action<OllamaProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model name is required for Ollama provider.", nameof(model));

        var providerConfig = new OllamaProviderConfig();
        configure?.Invoke(providerConfig);

        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "ollama",
            Endpoint = ResolveEndpoint(endpoint),
            ModelName = model
        };
        chatConfig.SetProviderConfig(providerConfig);

        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>
    /// Adds Ollama-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithOllamaChatRequestOptions(
        this AgentBuilder builder,
        OllamaChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var chatConfig = builder.Config.EnsureChatClientConfig();
        options.ApplyTo(chatConfig);

        return builder;
    }

    /// <summary>
    /// Adds Ollama-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithOllamaChatRequestOptions(
        this AgentBuilder builder,
        Action<OllamaChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OllamaChatRequestOptions();
        configure(options);
        return builder.WithOllamaChatRequestOptions(options);
    }

    private static string ResolveEndpoint(string? explicitEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(explicitEndpoint))
            return explicitEndpoint;

        return System.Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT")
            ?? System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
            ?? "http://localhost:11434";
    }
}
