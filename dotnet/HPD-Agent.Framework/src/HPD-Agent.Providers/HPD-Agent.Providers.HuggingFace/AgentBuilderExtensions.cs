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
    /// <param name="apiKey">Optional API key. If not provided, will look for HUGGINGFACE_API_KEY environment variable</param>
    /// <param name="endpoint">Optional Hugging Face-compatible endpoint override.</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// API Key Resolution (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: HUGGINGFACE_API_KEY
    /// 4. appsettings.json: "huggingface:ApiKey" or "HuggingFace:ApiKey"
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithHuggingFace(
    ///         model: "meta-llama/Meta-Llama-3-8B-Instruct",
    ///         apiKey: "hf_...")
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
        string? apiKey = null,
        string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model repository ID is required for HuggingFace provider.", nameof(model));

        var chatConfig = new ClientProviderConfig
        {
            ProviderKey = "huggingface",
            ModelName = model,
            ApiKey = apiKey,
            Endpoint = endpoint
        };

        builder.Config.SetChatClientConfig(chatConfig);

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
        chatConfig.ChatDefaults ??= new ChatRunConfig();
        options.ApplyTo(chatConfig.ChatDefaults);

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
