using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Threading;
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.AzureAI;

/// <summary>
/// Azure AI Projects provider implementation using Azure.AI.Projects SDK.
/// Supports Azure AI Foundry endpoints with OAuth/Entra ID authentication.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses Microsoft's modern Azure AI stack:
/// - Azure.AI.Projects for project client management
/// - Azure.AI.OpenAI for chat completions
/// - Azure.Identity for DefaultAzureCredential (OAuth/Entra ID)
/// - Microsoft.Extensions.AI.OpenAI for IChatClient integration
/// </para>
/// <para>
/// Supports Azure AI Foundry/Projects endpoints: https://*.services.ai.azure.com/api/projects/*
/// Also supports traditional Azure OpenAI endpoints for backward compatibility.
/// </para>
/// <para>
/// Authentication methods:
/// 1. DefaultAzureCredential (recommended) - OAuth/Entra ID authentication
/// 2. API Key - For endpoints that support key-based authentication
/// </para>
/// </remarks>
internal class AzureAIProvider : IProviderFeatures
{
    public string ProviderKey => "azure-ai";
    public string DisplayName => "Azure AI (Projects)";

    public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null)
    {
        // Get secret resolver from services
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets == null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        // Resolve required endpoint using ISecretResolver (Azure requires endpoint)
        var endpointTask = secrets.RequireAsync("azure-ai:Endpoint", "Azure AI", config.Endpoint, CancellationToken.None);
        string endpoint = endpointTask.GetAwaiter().GetResult();

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For AzureAI, the ModelName (deployment name) must be configured.");
        }

        // Get typed config
        var azureConfig = config.GetTypedProviderConfig<AzureAIProviderConfig>();

        // Determine authentication method
        bool useOAuth = azureConfig?.UseDefaultAzureCredential ?? false;

        // Resolve API key using ISecretResolver (unless OAuth is requested)
        string? apiKey = null;
        if (!useOAuth)
        {
            var apiKeyTask = secrets.RequireAsync("azure-ai:ApiKey", "Azure AI", config.ApiKey, CancellationToken.None);
            apiKey = apiKeyTask.GetAwaiter().GetResult();
        }

        // Create chat client based on endpoint type
        IChatClient chatClient;
        Uri endpointUri = new Uri(endpoint);

        // Check if this is an Azure AI Projects endpoint
        if (endpoint.Contains("services.ai.azure.com") && endpoint.Contains("/api/projects/"))
        {
            // Azure AI Projects endpoint - only supports OAuth (DefaultAzureCredential)
            // For Azure AI Foundry, API keys are not supported - must use OAuth
            TokenCredential credential = string.IsNullOrEmpty(apiKey)
                ? new DefaultAzureCredential()
                : throw new InvalidOperationException(
                    "Azure AI Foundry/Projects endpoints require OAuth authentication. " +
                    "Set UseDefaultAzureCredential = true or omit the API key.");

            chatClient = CreateProjectsChatClient(endpointUri, modelName, credential);
        }
        else
        {
            // Traditional Azure OpenAI endpoint - supports both auth methods
            if (string.IsNullOrEmpty(apiKey))
            {
                // Use OAuth
                TokenCredential credential = new DefaultAzureCredential();
                chatClient = CreateAzureOpenAIChatClient(endpointUri, modelName, credential);
            }
            else
            {
                // Use API key
                chatClient = CreateAzureOpenAIChatClientWithKey(endpointUri, modelName, apiKey);
            }
        }

        // Apply client factory middleware if provided
        if (config.AdditionalProperties?.TryGetValue("ClientFactory", out var factoryObj) == true &&
            factoryObj is Func<IChatClient, IChatClient> clientFactory)
        {
            chatClient = clientFactory(chatClient);
        }

        return chatClient;
    }

    private static IChatClient CreateProjectsChatClient(Uri projectEndpoint, string modelName, TokenCredential credential)
    {
        // Create AIProjectClient
        var projectClient = new AIProjectClient(projectEndpoint, credential);

        // Get the Azure OpenAI connection from the project
        var connection = projectClient.GetConnection(typeof(AzureOpenAIClient).FullName!);

        if (!connection.TryGetLocatorAsUri(out Uri? openAIUri) || openAIUri is null)
        {
            throw new InvalidOperationException("Failed to get Azure OpenAI connection URI from AI Project.");
        }

        // Create Azure OpenAI client using the connection
        var azureOpenAIClient = new AzureOpenAIClient(new Uri($"https://{openAIUri.Host}"), credential);
        var chatClient = azureOpenAIClient.GetChatClient(modelName);

        // Convert to IChatClient using Microsoft.Extensions.AI
        return chatClient.AsIChatClient();
    }

    private static IChatClient CreateAzureOpenAIChatClient(Uri endpoint, string modelName, TokenCredential credential)
    {
        // Direct Azure OpenAI endpoint with OAuth
        var azureOpenAIClient = new AzureOpenAIClient(endpoint, credential);
        var chatClient = azureOpenAIClient.GetChatClient(modelName);

        // Convert to IChatClient using Microsoft.Extensions.AI
        return chatClient.AsIChatClient();
    }

    private static IChatClient CreateAzureOpenAIChatClientWithKey(Uri endpoint, string modelName, string apiKey)
    {
        // Direct Azure OpenAI endpoint with API key
        var azureOpenAIClient = new AzureOpenAIClient(endpoint, new AzureKeyCredential(apiKey));
        var chatClient = azureOpenAIClient.GetChatClient(modelName);

        // Convert to IChatClient using Microsoft.Extensions.AI
        return chatClient.AsIChatClient();
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new AzureAIErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            SupportsStreaming = true,
            SupportsFunctionCalling = true,
            SupportsVision = true,
            DocumentationUrl = "https://learn.microsoft.com/en-us/azure/ai-studio/"
        };
    }

    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name (deployment name) is required for Azure AI");

        // Note: Endpoint and API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.Endpoint))
        {
            errors.Add("Endpoint is required for Azure AI. " +
                      "Set it via the endpoint parameter, AZURE_AI_ENDPOINT environment variable, or configuration.");
        }

        // Validate Azure-specific config if present
        var azureConfig = config.GetTypedProviderConfig<AzureAIProviderConfig>();
        if (azureConfig != null)
        {
            // Validate Temperature range
            if (azureConfig.Temperature.HasValue && (azureConfig.Temperature.Value < 0 || azureConfig.Temperature.Value > 2))
            {
                errors.Add("Temperature must be between 0 and 2");
            }

            // Validate TopP range
            if (azureConfig.TopP.HasValue && (azureConfig.TopP.Value < 0 || azureConfig.TopP.Value > 1))
            {
                errors.Add("TopP must be between 0 and 1");
            }

            // Validate FrequencyPenalty range
            if (azureConfig.FrequencyPenalty.HasValue && (azureConfig.FrequencyPenalty.Value < -2 || azureConfig.FrequencyPenalty.Value > 2))
            {
                errors.Add("FrequencyPenalty must be between -2 and 2");
            }

            // Validate PresencePenalty range
            if (azureConfig.PresencePenalty.HasValue && (azureConfig.PresencePenalty.Value < -2 || azureConfig.PresencePenalty.Value > 2))
            {
                errors.Add("PresencePenalty must be between -2 and 2");
            }

            // Validate ResponseFormat
            if (!string.IsNullOrEmpty(azureConfig.ResponseFormat))
            {
                var validFormats = new[] { "text", "json_object", "json_schema" };
                if (!validFormats.Contains(azureConfig.ResponseFormat))
                {
                    errors.Add("ResponseFormat must be one of: text, json_object, json_schema");
                }

                // Validate json_schema requirements
                if (azureConfig.ResponseFormat == "json_schema")
                {
                    if (string.IsNullOrEmpty(azureConfig.JsonSchemaName))
                    {
                        errors.Add("JsonSchemaName is required when ResponseFormat is json_schema");
                    }
                    if (string.IsNullOrEmpty(azureConfig.JsonSchema))
                    {
                        errors.Add("JsonSchema is required when ResponseFormat is json_schema");
                    }
                }
            }

            // Validate ToolChoice
            if (!string.IsNullOrEmpty(azureConfig.ToolChoice))
            {
                var validChoices = new[] { "auto", "none", "required" };
                if (!validChoices.Contains(azureConfig.ToolChoice))
                {
                    errors.Add("ToolChoice must be one of: auto, none, required");
                }
            }
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }
}
