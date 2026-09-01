using System;
using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.HuggingFace;

/// <summary>
/// Extension methods for AgentBuilder to configure HuggingFace as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use HuggingFace Serverless Inference API as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="model">The model repository ID (e.g., "meta-llama/Meta-Llama-3-8B-Instruct", "mistralai/Mistral-7B-Instruct-v0.2")</param>
    /// <param name="endpoint">Optional Hugging Face-compatible endpoint override.</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>The canonical <c>huggingface:ApiKey</c> secret reference is resolved at acquisition time.</remarks>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithHuggingFace(
    ///         model: "meta-llama/Meta-Llama-3-8B-Instruct")
    ///     .Build();
    ///
    /// var agent = new AgentBuilder()
    ///     .WithHuggingFace(model: "mistralai/Mistral-7B-Instruct-v0.2")
    ///     .WithHuggingFaceChatRequestOptions(options => options.Logprobs = true)
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithHuggingFace(
        this AgentBuilder builder,
        string model,
        ProviderAuthentication? authentication = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model repository ID is required for HuggingFace provider.", nameof(model));

        var chatConfig = new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "huggingface",
                Authentication = authentication ?? new ApiKeyProviderAuthentication { SecretKey = "huggingface:ApiKey" }
            },
            ModelName = model,
            Endpoint = endpoint
        };

        builder.ProviderRegistry.Register(new HuggingFaceProvider());
        builder.Config.SetChatClientConfig(chatConfig);

        return builder;
    }

    /// <summary>Configures Hugging Face with a literal runtime-only API key.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The model repository identifier.</param>
    /// <param name="apiKey">The API key copied immediately into HPD-owned clearable storage.</param>
    /// <param name="endpoint">An optional inference endpoint override.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithHuggingFace(
        this AgentBuilder builder,
        string model,
        ReadOnlySpan<char> apiKey,
        string? endpoint = null) =>
        builder.WithHuggingFace(model, builder.RegisterExplicitApiKey(apiKey), endpoint);

    /// <summary>Configures Hugging Face inference through its documented public-client OAuth PKCE flow.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="model">The model repository identifier.</param>
    /// <param name="accountId">The host-local account label.</param>
    /// <param name="clientId">The registered Hugging Face public OAuth client identifier.</param>
    /// <param name="httpClient">The host-owned HTTP client used for OAuth token operations.</param>
    /// <param name="storeKey">The protected authorization-store registration key, or the explicit default.</param>
    /// <param name="endpoint">An optional inference endpoint override.</param>
    /// <param name="timeProvider">The time authority used for token expiry.</param>
    /// <returns>The same builder.</returns>
    public static AgentBuilder WithHuggingFaceOAuth(
        this AgentBuilder builder,
        string model,
        string accountId,
        string clientId,
        HttpClient httpClient,
        string? storeKey = null,
        string? endpoint = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(httpClient);

        builder.AddProviderAuthenticationStrategy(
            new HuggingFaceOAuthStrategy(clientId, httpClient, timeProvider));
        builder.ProviderRegistry.Register(new HuggingFaceProvider());
        builder.Config.SetChatClientConfig(new ChatClientConfig
        {
            Provider = new ProviderReference
            {
                Key = "huggingface",
                Backend = "platform",
                Authentication = new OAuthProviderAuthentication
                {
                    AccountId = accountId,
                    StoreKey = storeKey,
                    Scopes = ["inference-api"]
                }
            },
            ModelName = model,
            Endpoint = endpoint
        });
        return builder;
    }

    /// <summary>
    /// Adds Hugging Face-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithHuggingFaceChatRequestOptions(
        this AgentBuilder builder,
        HuggingFaceChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        var chatConfig = builder.Config.EnsureChatClientConfig();
        options.ApplyTo(chatConfig);

        return builder;
    }

    /// <summary>
    /// Adds Hugging Face-specific runtime chat request options to the chat defaults.
    /// </summary>
    public static AgentBuilder WithHuggingFaceChatRequestOptions(
        this AgentBuilder builder,
        Action<HuggingFaceChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new HuggingFaceChatRequestOptions();
        configure(options);
        return builder.WithHuggingFaceChatRequestOptions(options);
    }
}
