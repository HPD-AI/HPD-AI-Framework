using System;
using System.Threading;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.AzureAIInference;

/// <summary>
/// Extension methods for AgentBuilder to configure Azure AI Inference as the AI provider.
/// </summary>
/// <remarks>
/// OBSOLETE: Azure.AI.Inference is being superseded by Azure.AI.Projects.
/// For Azure AI Foundry endpoints, use the Azure OpenAI provider instead.
/// See: https://github.com/microsoft/agents/tree/main/dotnet/src/Microsoft.Agents.AI.AzureAI
/// </remarks>
[Obsolete("Azure.AI.Inference is being superseded by Azure.AI.Projects. Use Azure OpenAI provider for Azure AI Foundry endpoints.")]
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use Azure AI Inference as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="endpoint">The Azure AI Inference endpoint URL (e.g., "https://your-resource.inference.ai.azure.com")</param>
    /// <param name="model">The model deployment name (e.g., "llama-3-8b")</param>
    /// <param name="apiKey">Optional API key. If not provided, will try to resolve from environment variables (AZURE_AI_INFERENCE_API_KEY) or appsettings.json</param>
    /// <param name="configure">Optional action to configure additional Azure AI Inference-specific options</param>
    /// <param name="clientFactory">Optional factory to wrap the chat client with middleware (logging, caching, etc.)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// Endpoint Resolution (in priority order):
    /// 1. Explicit endpoint parameter
    /// 2. Environment variable: AZURE_AI_INFERENCE_ENDPOINT
    /// 3. appsettings.json: "azureAIInference:Endpoint" or "AzureAIInference:Endpoint"
    /// </para>
    /// <para>
    /// API Key Resolution (in priority order):
    /// 1. Explicit apiKey parameter
    /// 2. Environment variable: AZURE_AI_INFERENCE_API_KEY
    /// 3. appsettings.json: "azureAIInference:ApiKey" or "AzureAIInference:ApiKey"
    /// </para>
    /// <para>
    /// This method creates an <see cref="AzureAIInferenceProviderConfig"/> that is:
    /// - Stored in <c>ProviderConfig.ProviderOptionsJson</c> for FFI/JSON serialization
    /// - Applied during <c>AzureAIInferenceProvider.CreateChatClient()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "azure-ai-inference",
    ///     "ModelName": "llama-3-8b",
    ///     "Endpoint": "https://your-resource.inference.ai.azure.com",
    ///     "ApiKey": "your-api-key",
    ///     "ProviderOptionsJson": "{\"maxTokens\":2048,\"temperature\":0.7,\"seed\":12345}"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// NOTE: The Azure AI Inference SDK is NOT Native AOT compatible (AotCompatOptOut=true).
    /// While the provider config serialization is AOT-ready, the SDK itself cannot be used
    /// in Native AOT scenarios. Use OpenAI or Azure OpenAI providers for AOT deployments.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: Provide endpoint and API key explicitly
    /// var agent = new AgentBuilder()
    ///     .WithAzureAIInference(
    ///         endpoint: "https://your-resource.inference.ai.azure.com",
    ///         model: "llama-3-8b",
    ///         apiKey: "your-api-key",
    ///         configure: opts =>
    ///         {
    ///             opts.MaxTokens = 2048;
    ///             opts.Temperature = 0.7f;
    ///             opts.Seed = 12345;
    ///         })
    ///     .Build();
    ///
    /// // Option 2: Auto-resolve from environment variables
    /// var agent = new AgentBuilder()
    ///     .WithAzureAIInference(
    ///         endpoint: "https://your-resource.inference.ai.azure.com",
    ///         model: "llama-3-8b",
    ///         configure: opts =>
    ///         {
    ///             opts.ResponseFormat = "json_object";
    ///             opts.ToolChoice = "auto";
    ///         })
    ///     .Build();
    ///
    /// // Option 3: With structured JSON output
    /// var agent = new AgentBuilder()
    ///     .WithAzureAIInference(
    ///         endpoint: "https://your-resource.inference.ai.azure.com",
    ///         model: "llama-3-8b",
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
    /// // Option 4: With middleware via ClientFactory
    /// var agent = new AgentBuilder()
    ///     .WithAzureAIInference(
    ///         endpoint: "https://your-resource.inference.ai.azure.com",
    ///         model: "llama-3-8b",
    ///         configure: opts => opts.MaxTokens = 2048,
    ///         clientFactory: client => new LoggingChatClient(client, logger))
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithAzureAIInference(
        this AgentBuilder builder,
        string endpoint,
        string model,
        string? apiKey = null,
        Action<AzureAIInferenceProviderConfig>? configure = null,
        Func<IChatClient, IChatClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required for Azure AI Inference provider.", nameof(endpoint));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required for Azure AI Inference provider.", nameof(model));

        // Note: API key resolution is deferred to CreateChatClient where ISecretResolver is available
        // This allows the builder to work with env vars, config, and auth storage

        // Create provider config
        var providerConfig = new AzureAIInferenceProviderConfig();

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate configuration
        ValidateProviderConfig(providerConfig, configure);

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "azure-ai-inference",
            Endpoint = endpoint,
            ApiKey = apiKey, // May be null - will be resolved by ISecretResolver
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

    private static void ValidateProviderConfig(AzureAIInferenceProviderConfig config, Action<AzureAIInferenceProviderConfig>? configure)
    {
        // Validate Temperature range
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 1))
        {
            throw new ArgumentException(
                "Temperature must be between 0 and 1.",
                nameof(configure));
        }

        // Validate TopP range
        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
        {
            throw new ArgumentException(
                "TopP (NucleusSamplingFactor) must be between 0 and 1.",
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

        // Validate ExtraParametersMode
        if (!string.IsNullOrEmpty(config.ExtraParametersMode))
        {
            var validModes = new[] { "pass-through", "error", "drop" };
            if (!Array.Exists(validModes, m => m == config.ExtraParametersMode))
            {
                throw new ArgumentException(
                    "ExtraParametersMode must be one of: pass-through, error, drop.",
                    nameof(configure));
            }
        }
    }
}
