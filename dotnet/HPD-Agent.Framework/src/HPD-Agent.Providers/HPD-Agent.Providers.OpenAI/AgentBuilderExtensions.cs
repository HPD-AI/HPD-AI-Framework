using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// Extension methods for AgentBuilder to configure OpenAI providers.
/// </summary>
public static class AgentBuilderExtensions
{
    private static readonly HttpClient ExperimentalCodexOAuthHttpClient = new();
    /// <summary>
    /// Configures the agent to use OpenAI as the chat provider.
    /// </summary>
    public static AgentBuilder WithOpenAI(
        this AgentBuilder builder,
        string model,
        ProviderAuthentication? authentication,
        Action<OpenAIProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for OpenAI provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "openai",
                Backend = "platform",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "openai:ApiKey" }
            },
            ModelName = model
        };

        var providerConfig = new OpenAIProviderConfig();
        configure?.Invoke(providerConfig);
        chatConfig.ProviderConfig = providerConfig;

        builder.ProviderRegistry.Register(new OpenAIProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>Configures OpenAI with a literal runtime-only API key.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The OpenAI model identifier.</param>
    /// <param name="apiKey">The API key copied immediately into HPD-owned clearable storage.</param>
    /// <param name="configure">Optional provider configuration.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>The literal is represented only by a non-serializable runtime registration.</remarks>
    public static AgentBuilder WithOpenAI(
        this AgentBuilder builder,
        string model,
        ReadOnlySpan<char> apiKey,
        Action<OpenAIProviderConfig>? configure = null) =>
        builder.WithOpenAI(model, builder.RegisterExplicitApiKey(apiKey), configure);

    /// <summary>Configures OpenAI using its canonical <c>openai:ApiKey</c> secret reference.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The OpenAI model identifier.</param>
    /// <param name="configure">Optional provider configuration.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithOpenAI(
        this AgentBuilder builder,
        string model,
        Action<OpenAIProviderConfig>? configure = null) =>
        builder.WithOpenAI(model, authentication: null, configure);

    /// <summary>
    /// Selects the ChatGPT/Codex OAuth backend using a host-owned account reference.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The Codex model identifier.</param>
    /// <param name="accountId">The host-defined durable account label.</param>
    /// <param name="storeKey">The protected authorization-store registration key, or the explicit default.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// This method only authors the portable account selection. It never opens a browser or
    /// embeds credentials. Account connection is performed through the host account-management
    /// API. The backend fails closed until a reviewed OpenAI OAuth protocol profile is installed.
    /// </remarks>
    public static AgentBuilder WithOpenAICodex(
        this AgentBuilder builder,
        string model,
        string accountId,
        string? storeKey = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        builder.ProviderRegistry.Register(new OpenAICodexProvider());
        builder.Config.SetChatClientConfig(new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "openai",
                Backend = "codex",
                Authentication = new OAuthProviderAuthentication
                {
                    AccountId = accountId,
                    StoreKey = storeKey
                }
            },
            ModelName = model
        });
        return builder;
    }

    /// <summary>
    /// Selects and enables HPD's experimental OpenCode-compatible ChatGPT/Codex OAuth profile.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The Codex model identifier.</param>
    /// <param name="accountId">The host-defined durable account label.</param>
    /// <param name="httpClient">The host-owned client used for OAuth protocol operations.</param>
    /// <param name="experimentalOptions">Optional overrides for the informally discovered protocol profile.</param>
    /// <param name="storeKey">The protected authorization-store registration key, or the explicit default.</param>
    /// <param name="timeProvider">The token and transaction time authority.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// This integration is not based on an official OpenAI developer contract. It may stop working
    /// without notice. HPD still owns transaction protection, durable sessions, refresh coordination,
    /// account isolation, and request-time credential signing.
    /// </remarks>
    public static AgentBuilder WithOpenAICodex(
        this AgentBuilder builder,
        string model,
        string accountId,
        HttpClient httpClient,
        OpenAICodexExperimentalOptions? experimentalOptions = null,
        string? storeKey = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        builder.AddProviderAuthenticationStrategy(
            new OpenAICodexExperimentalOAuthStrategy(httpClient, experimentalOptions, timeProvider));
        builder.WithOpenAICodex(model, accountId, storeKey);
        builder.ProviderRegistry.Register(new OpenAICodexProvider(
            (experimentalOptions ?? new OpenAICodexExperimentalOptions()).ResponsesEndpoint));
        return builder;
    }

    /// <summary>Enables the experimental Codex OAuth profile with HPD's shared protocol client.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The Codex model identifier.</param>
    /// <param name="accountId">The host-defined durable account label.</param>
    /// <param name="experimentalOptions">The explicit experimental protocol opt-in and optional overrides.</param>
    /// <param name="storeKey">The protected authorization-store registration key, or the explicit default.</param>
    /// <param name="timeProvider">The token and transaction time authority.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithOpenAICodex(
        this AgentBuilder builder,
        string model,
        string accountId,
        OpenAICodexExperimentalOptions experimentalOptions,
        string? storeKey = null,
        TimeProvider? timeProvider = null) =>
        builder.WithOpenAICodex(
            model, accountId, ExperimentalCodexOAuthHttpClient,
            experimentalOptions ?? throw new ArgumentNullException(nameof(experimentalOptions)), storeKey, timeProvider);

    /// <summary>
    /// Configures the agent to use traditional Azure OpenAI endpoints.
    /// </summary>
    public static AgentBuilder WithAzureOpenAI(
        this AgentBuilder builder,
        string endpoint,
        string model,
        ProviderAuthentication? authentication,
        Action<AzureOpenAIProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required for Azure OpenAI provider.", nameof(endpoint));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Azure OpenAI provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "azure-openai",
                Backend = "azure",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "azure-openai:ApiKey" }
            },
            Endpoint = endpoint,
            ModelName = model
        };

        var providerConfig = new AzureOpenAIProviderConfig();
        configure?.Invoke(providerConfig);
        chatConfig.ProviderConfig = providerConfig;

        builder.ProviderRegistry.Register(new AzureOpenAIProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>Configures Azure OpenAI with a literal runtime-only API key.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="endpoint">The Azure OpenAI endpoint.</param>
    /// <param name="model">The deployment or model identifier.</param>
    /// <param name="apiKey">The API key copied immediately into HPD-owned clearable storage.</param>
    /// <param name="configure">Optional provider configuration.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithAzureOpenAI(
        this AgentBuilder builder,
        string endpoint,
        string model,
        ReadOnlySpan<char> apiKey,
        Action<AzureOpenAIProviderConfig>? configure = null) =>
        builder.WithAzureOpenAI(endpoint, model, builder.RegisterExplicitApiKey(apiKey), configure);

    /// <summary>Configures Azure OpenAI using its canonical API-key secret reference.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="endpoint">The Azure OpenAI endpoint.</param>
    /// <param name="model">The deployment or model identifier.</param>
    /// <param name="configure">Optional provider configuration.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithAzureOpenAI(
        this AgentBuilder builder,
        string endpoint,
        string model,
        Action<AzureOpenAIProviderConfig>? configure = null) =>
        builder.WithAzureOpenAI(endpoint, model, authentication: null, configure);
}
