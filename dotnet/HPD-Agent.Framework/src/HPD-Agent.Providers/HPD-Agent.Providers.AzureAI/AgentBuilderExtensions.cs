using System;
using HPD.Agent;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.AzureAI;

/// <summary>
/// Extension methods for AgentBuilder to configure Azure AI as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Azure AI Projects as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="endpoint">The Azure AI endpoint URL. Supports:
    /// - Azure AI Foundry/Projects: "https://account.services.ai.azure.com/api/projects/project-name"
    /// - Azure OpenAI: "https://your-resource.openai.azure.com"</param>
    /// <param name="model">The model deployment name (e.g., "gpt-4", "gpt-4o")</param>
    /// <param name="apiKey">Optional API key. If not provided, AuthMode.Auto uses DefaultAzureCredential.</param>
    /// <param name="configure">Optional action to configure Azure AI provider-native options</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// Endpoint Resolution (in priority order):
    /// 1. Explicit endpoint parameter
    /// 2. Environment variable: AZURE_AI_ENDPOINT
    /// 3. appsettings.json: "azureAI:Endpoint" or "AzureAI:Endpoint"
    /// </para>
    /// <para>
    /// API Key Resolution for AuthMode.Auto/ApiKey (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: AZURE_AI_API_KEY
    /// 3. appsettings.json: "azureAI:ApiKey" or "AzureAI:ApiKey"
    /// 4. DefaultAzureCredential (OAuth/Entra ID) - used by AuthMode.Auto if no API key is found
    /// </para>
    /// <para>
    /// Authentication Methods:
    /// - API Key: Provide apiKey parameter or set AZURE_AI_API_KEY environment variable
    /// - OAuth/Entra ID: Set AuthMode = DefaultAzureCredential, or use AuthMode.Auto and omit API key
    /// </para>
    /// <para>
    /// This method creates an <see cref="AzureAIProviderConfig"/> that is:
    /// - Stored in <c>ProviderClientConfig.ConstructionOptions</c> as a structured JSON/YAML object
    /// - Applied during <c>AzureAIProvider.CreateChatClientAsync()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "providerKey": "azure-ai",
    ///   "modelName": "gpt-4",
    ///   "endpoint": "https://your-project.services.ai.azure.com",
    ///   "apiKey": "your-api-key",
    ///   "constructionOptions": { "authMode": "DefaultAzureCredential" }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Direct Azure OpenAI-compatible endpoint with API key
    /// var agent = new AgentBuilder()
    ///     .WithAzureAI(
    ///         endpoint: "https://my-resource.openai.azure.com/",
    ///         model: "gpt-4",
    ///         apiKey: "your-api-key")
    ///     .Build();
    ///
    /// // Option 2: Azure AI Foundry with OAuth/Entra ID (recommended)
    /// var agent = new AgentBuilder()
    ///     .WithAzureAI(
    ///         endpoint: "https://my-account.services.ai.azure.com/api/projects/my-project",
    ///         model: "gpt-4",
    ///         configure: opts =>
    ///         {
    ///             opts.AuthMode = AzureAIAuthMode.DefaultAzureCredential;
    ///             opts.ProjectServiceVersion = AzureAIProjectServiceVersion.V1;
    ///             opts.OpenAIServiceVersion = AzureAIOpenAIServiceVersion.V2025_04_01_Preview;
    ///         })
    ///     .Build();
    ///
    /// // Option 3: Auto-resolve from environment variables
    /// // Set AZURE_AI_ENDPOINT and optionally AZURE_AI_API_KEY
    /// var agent = new AgentBuilder()
    ///     .WithAzureAI(
    ///         endpoint: System.Environment.GetEnvironmentVariable("AZURE_AI_ENDPOINT")!,
    ///         model: "gpt-4")
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithAzureAI(
        this AgentBuilder builder,
        string endpoint,
        string model,
        string? apiKey = null,
        Action<AzureAIProviderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required for Azure AI provider.", nameof(endpoint));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Azure AI provider.", nameof(model));

        // Note: API key resolution is deferred to CreateChatClient where ISecretResolver is available
        // This allows the builder to work with env vars, config, and auth storage

        // Create provider config
        var providerConfig = new AzureAIProviderConfig();

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Build provider config
        var chatConfig = new ChatClientConfig
        {
            ProviderKey = "azure-ai",
            Endpoint = endpoint,
            ApiKey = apiKey, // May be null - will be resolved by ISecretResolver or use DefaultAzureCredential
            ModelName = model
        };

        builder.Config.SetChatClientConfig(chatConfig);

        // Store the typed config
        chatConfig.SetProviderConfig(providerConfig);

        return builder;
    }

}
