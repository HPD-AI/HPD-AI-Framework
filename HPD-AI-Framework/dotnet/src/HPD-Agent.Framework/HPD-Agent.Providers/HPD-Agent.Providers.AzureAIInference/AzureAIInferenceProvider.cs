using System;
using System.Collections.Generic;
using System.Threading;
using Azure;
using Azure.AI.Inference;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.AzureAIInference;

/// <summary>
/// Azure AI Inference provider implementation.
/// </summary>
/// <remarks>
/// <para>
/// OBSOLETE: This provider uses Azure.AI.Inference which is being superseded by Azure.AI.Projects.
/// Microsoft's official agent framework now uses:
/// - Azure.AI.Projects (https://www.nuget.org/packages/Azure.AI.Projects)
/// - Azure.AI.Projects.OpenAI (https://www.nuget.org/packages/Azure.AI.Projects.OpenAI)
/// - Microsoft.Extensions.AI.OpenAI (https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI)
/// </para>
/// <para>
/// This provider is maintained for backward compatibility but will be deprecated in a future version.
/// For Azure AI Foundry endpoints (*.services.ai.azure.com/api/projects/*), use the Azure OpenAI provider instead.
/// </para>
/// <para>
/// See: https://github.com/microsoft/agents/tree/main/dotnet/src/Microsoft.Agents.AI.AzureAI
/// </para>
/// </remarks>
[Obsolete("This provider uses Azure.AI.Inference which is being superseded by Azure.AI.Projects. Use Azure OpenAI provider for Azure AI Foundry endpoints. This will be deprecated in a future version.")]
internal class AzureAIInferenceProvider : IProviderFeatures
{
    public string ProviderKey => "azure-ai-inference";
    public string DisplayName => "Azure AI Inference";

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

        // Resolve required endpoint using ISecretResolver (Azure AI Inference requires endpoint)
        var endpointTask = secrets.RequireAsync("azure-ai-inference:Endpoint", "Azure AI Inference", config.Endpoint, CancellationToken.None);
        string endpoint = endpointTask.GetAwaiter().GetResult();

        // Resolve required API key using ISecretResolver
        var apiKeyTask = secrets.RequireAsync("azure-ai-inference:ApiKey", "Azure AI Inference", config.ApiKey, CancellationToken.None);
        string apiKey = apiKeyTask.GetAwaiter().GetResult();

        var client = new ChatCompletionsClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        IChatClient chatClient = client.AsIChatClient(config.ModelName);

        // Note: Most configuration (temperature, topP, maxTokens, etc.) is applied
        // via ChatOptions when calling CompleteAsync/CompleteChatAsync.
        // The AzureAIInferenceProviderConfig is stored and can be accessed to build ChatOptions
        // for advanced features like ResponseFormat, Seed, FrequencyPenalty, etc.

        // Apply client factory middleware if provided
        if (config.AdditionalProperties?.TryGetValue("ClientFactory", out var factoryObj) == true &&
            factoryObj is Func<IChatClient, IChatClient> clientFactory)
        {
            chatClient = clientFactory(chatClient);
        }

        return chatClient;
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new AzureAIInferenceErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            SupportsStreaming = true,
            SupportsFunctionCalling = true,
            SupportsVision = false,
            DocumentationUrl = "https://learn.microsoft.com/en-us/azure/ai-studio/how-to/deploy-models-inference"
        };
    }

    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required for Azure AI Inference");

        // Note: Endpoint and API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.Endpoint))
        {
            errors.Add("Endpoint is required for Azure AI Inference. " +
                      "Set it via the endpoint parameter, AZURE_AI_INFERENCE_ENDPOINT environment variable, or configuration.");
        }

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            errors.Add("API key is required for Azure AI Inference. " +
                      "Set it via the apiKey parameter, AZURE_AI_INFERENCE_API_KEY environment variable, or configuration.");
        }

        // Validate provider-specific config if present
        var azureConfig = config.GetTypedProviderConfig<AzureAIInferenceProviderConfig>();
        if (azureConfig != null)
        {
            // Validate Temperature range
            if (azureConfig.Temperature.HasValue && (azureConfig.Temperature.Value < 0 || azureConfig.Temperature.Value > 1))
            {
                errors.Add("Temperature must be between 0 and 1");
            }

            // Validate TopP range
            if (azureConfig.TopP.HasValue && (azureConfig.TopP.Value < 0 || azureConfig.TopP.Value > 1))
            {
                errors.Add("TopP (NucleusSamplingFactor) must be between 0 and 1");
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

            // Validate ExtraParametersMode
            if (!string.IsNullOrEmpty(azureConfig.ExtraParametersMode))
            {
                var validModes = new[] { "pass-through", "error", "drop" };
                if (!validModes.Contains(azureConfig.ExtraParametersMode))
                {
                    errors.Add("ExtraParametersMode must be one of: pass-through, error, drop");
                }
            }
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }
}
