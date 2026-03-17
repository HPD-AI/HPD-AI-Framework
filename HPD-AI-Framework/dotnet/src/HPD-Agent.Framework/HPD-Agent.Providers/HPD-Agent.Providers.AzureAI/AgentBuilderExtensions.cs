using System;
using System.Threading;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

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
    /// <param name="apiKey">Optional API key. If not provided, will use DefaultAzureCredential (OAuth/Entra ID)</param>
    /// <param name="configure">Optional action to configure additional Azure AI-specific options</param>
    /// <param name="clientFactory">Optional factory to wrap the chat client with middleware (logging, caching, etc.)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// Endpoint Resolution (in priority order):
    /// 1. Explicit endpoint parameter
    /// 2. Environment variable: AZURE_AI_ENDPOINT
    /// 3. appsettings.json: "azureAI:Endpoint" or "AzureAI:Endpoint"
    /// </para>
    /// <para>
    /// API Key Resolution (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: AZURE_AI_API_KEY
    /// 3. appsettings.json: "azureAI:ApiKey" or "AzureAI:ApiKey"
    /// 4. DefaultAzureCredential (OAuth/Entra ID) - used if no API key found
    /// </para>
    /// <para>
    /// Authentication Methods:
    /// - API Key: Provide apiKey parameter or set AZURE_AI_API_KEY environment variable
    /// - OAuth/Entra ID: Set UseDefaultAzureCredential = true in configure, or omit API key entirely
    /// </para>
    /// <para>
    /// This method creates an <see cref="AzureAIProviderConfig"/> that is:
    /// - Stored in <c>ProviderConfig.ProviderOptionsJson</c> for FFI/JSON serialization
    /// - Applied during <c>AzureAIProvider.CreateChatClient()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "azure-ai",
    ///     "ModelName": "gpt-4",
    ///     "Endpoint": "https://your-project.services.ai.azure.com",
    ///     "ApiKey": "your-api-key",
    ///     "ProviderOptionsJson": "{\"maxTokens\":4096,\"temperature\":0.7}"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Azure AI Foundry with API key
    /// var agent = new AgentBuilder()
    ///     .WithAzureAI(
    ///         endpoint: "https://my-account.services.ai.azure.com/api/projects/my-project",
    ///         model: "gpt-4",
    ///         apiKey: "your-api-key",
    ///         configure: opts =>
    ///         {
    ///             opts.MaxTokens = 4096;
    ///             opts.Temperature = 0.7f;
    ///         })
    ///     .Build();
    ///
    /// // Option 2: Azure AI Foundry with OAuth/Entra ID (recommended)
    /// var agent = new AgentBuilder()
    ///     .WithAzureAI(
    ///         endpoint: "https://my-account.services.ai.azure.com/api/projects/my-project",
    ///         model: "gpt-4",
    ///         configure: opts =>
    ///         {
    ///             opts.UseDefaultAzureCredential = true;
    ///             opts.MaxTokens = 4096;
    ///             opts.Temperature = 0.7f;
    ///         })
    ///     .Build();
    ///
    /// // Option 3: Traditional Azure OpenAI endpoint
    /// var agent = new AgentBuilder()
    ///     .WithAzureAI(
    ///         endpoint: "https://my-resource.openai.azure.com",
    ///         model: "gpt-4",
    ///         apiKey: "your-api-key",
    ///         configure: opts =>
    ///         {
    ///             opts.ResponseFormat = "json_object";
    ///             opts.ToolChoice = "auto";
    ///         })
    ///     .Build();
    ///
    /// // Option 4: With structured JSON output
    /// var agent = new AgentBuilder()
    ///     .WithAzureAI(
    ///         endpoint: "https://my-resource.openai.azure.com",
    ///         model: "gpt-4",
    ///         apiKey: "your-api-key",
    ///         configure: opts =>
    ///         {
    ///             opts.ResponseFormat = "json_schema";
    ///             opts.JsonSchemaName = "UserInfo";
    ///             opts.JsonSchema = @"{
    ///                 ""type"": ""object"",
    ///                 ""properties"": {
    ///                     ""name"": { ""type"": ""string"" },
    ///                     ""age"": { ""type"": ""number"" }
    ///                 }
    ///             }";
    ///             opts.JsonSchemaIsStrict = true;
    ///         })
    ///     .Build();
    ///
    /// // Option 5: With middleware via ClientFactory
    /// var agent = new AgentBuilder()
    ///     .WithAzureAI(
    ///         endpoint: "https://my-resource.openai.azure.com",
    ///         model: "gpt-4",
    ///         configure: opts => opts.MaxTokens = 4096,
    ///         clientFactory: client => new LoggingChatClient(client, logger))
    ///     .Build();
    ///
    /// // Option 6: Auto-resolve from environment variables
    /// // Set AZURE_AI_ENDPOINT and optionally AZURE_AI_API_KEY
    /// var agent = new AgentBuilder()
    ///     .WithAzureAI(
    ///         endpoint: Environment.GetEnvironmentVariable("AZURE_AI_ENDPOINT")!,
    ///         model: "gpt-4")
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithAzureAI(
        this AgentBuilder builder,
        string endpoint,
        string model,
        string? apiKey = null,
        Action<AzureAIProviderConfig>? configure = null,
        Func<IChatClient, IChatClient>? clientFactory = null)
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

        // Validate configuration
        ValidateProviderConfig(providerConfig, configure);

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "azure-ai",
            Endpoint = endpoint,
            ApiKey = apiKey, // May be null - will be resolved by ISecretResolver or use DefaultAzureCredential
            ModelName = model
        };

        // Store the typed config
        builder.Config.Provider.SetTypedProviderConfig(providerConfig);

        // Store the client factory if provided
        if (clientFactory != null)
        {
            // Store in AdditionalProperties for the provider to retrieve during CreateChatClient
            builder.Config.Provider.AdditionalProperties ??= new System.Collections.Generic.Dictionary<string, object>();
            builder.Config.Provider.AdditionalProperties["ClientFactory"] = clientFactory;
        }

        return builder;
    }

    private static void ValidateProviderConfig(AzureAIProviderConfig config, Action<AzureAIProviderConfig>? configure)
    {
        // Validate Temperature range
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 2))
        {
            throw new ArgumentException(
                "Temperature must be between 0 and 2.",
                nameof(configure));
        }

        // Validate TopP range
        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
        {
            throw new ArgumentException(
                "TopP must be between 0 and 1.",
                nameof(configure));
        }

        // Validate FrequencyPenalty range
        if (config.FrequencyPenalty.HasValue && (config.FrequencyPenalty.Value < -2 || config.FrequencyPenalty.Value > 2))
        {
            throw new ArgumentException(
                "FrequencyPenalty must be between -2 and 2.",
                nameof(configure));
        }

        // Validate PresencePenalty range
        if (config.PresencePenalty.HasValue && (config.PresencePenalty.Value < -2 || config.PresencePenalty.Value > 2))
        {
            throw new ArgumentException(
                "PresencePenalty must be between -2 and 2.",
                nameof(configure));
        }

        // Validate ResponseFormat
        if (!string.IsNullOrEmpty(config.ResponseFormat))
        {
            var validFormats = new[] { "text", "json_object", "json_schema" };
            if (!Array.Exists(validFormats, f => f == config.ResponseFormat))
            {
                throw new ArgumentException(
                    "ResponseFormat must be one of: text, json_object, json_schema.",
                    nameof(configure));
            }

            // Validate json_schema requirements
            if (config.ResponseFormat == "json_schema")
            {
                if (string.IsNullOrEmpty(config.JsonSchemaName))
                {
                    throw new ArgumentException(
                        "JsonSchemaName is required when ResponseFormat is json_schema.",
                        nameof(configure));
                }
                if (string.IsNullOrEmpty(config.JsonSchema))
                {
                    throw new ArgumentException(
                        "JsonSchema is required when ResponseFormat is json_schema.",
                        nameof(configure));
                }
            }
        }

        // Validate ToolChoice
        if (!string.IsNullOrEmpty(config.ToolChoice))
        {
            var validChoices = new[] { "auto", "none", "required" };
            if (!Array.Exists(validChoices, c => c == config.ToolChoice))
            {
                throw new ArgumentException(
                    "ToolChoice must be one of: auto, none, required.",
                    nameof(configure));
            }
        }
    }
}
