#pragma warning disable OPENAI001 // ResponsesClient is experimental

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Azure.AI.OpenAI;
using Azure;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// OpenAI provider implementation using the official OpenAI .NET SDK.
/// Uses the newer Responses API (ResponsesClient) for enhanced capabilities including:
/// - Background mode for long-running responses
/// - Continuation tokens for resuming responses
/// - MCP tool support
/// - Code interpreter integration
/// - Image generation tools
/// - Native reasoning content support
/// Supports both OpenAI and Azure OpenAI endpoints.
/// </summary>
internal class OpenAIProvider : IProviderFeatures
{
    public string ProviderKey => "openai";
    public string DisplayName => "OpenAI";

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

        // Resolve API key using ISecretResolver
        var apiKeyTask = secrets.RequireAsync("openai:ApiKey", "OpenAI", config.ApiKey, CancellationToken.None);
        string apiKey = apiKeyTask.GetAwaiter().GetResult();

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For OpenAI, the ModelName must be configured.");
        }

        // Resolve optional endpoint using ISecretResolver
        var endpointTask = secrets.ResolveOrDefaultAsync("openai:Endpoint", config.Endpoint, CancellationToken.None);
        var endpoint = endpointTask.GetAwaiter().GetResult();
        var hasCustomEndpoint = !string.IsNullOrEmpty(endpoint);
        var hasCustomHeaders = config.CustomHeaders?.Count > 0;

        IChatClient client;

        // Create OpenAI client options
        var options = new global::OpenAI.OpenAIClientOptions();

        if (hasCustomEndpoint || hasCustomHeaders)
        {
            // Custom endpoint - use ResponsesClient with custom HttpClient
            var httpClient = new System.Net.Http.HttpClient();

            if (config.CustomHeaders != null)
            {
                foreach (var header in config.CustomHeaders)
                {
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            if (hasCustomEndpoint)
            {
                options.Endpoint = new Uri(endpoint!);
            }
            options.Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(httpClient);
        }

        // Create the OpenAI client and get the ResponsesClient
        var openAIClient = new global::OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);
        var responsesClient = openAIClient.GetResponsesClient(modelName);
        client = responsesClient.AsIChatClient();

        // Apply client factory middleware if provided
        if (config.AdditionalProperties?.TryGetValue("ClientFactory", out var factoryObj) == true &&
            factoryObj is Func<IChatClient, IChatClient> clientFactory)
        {
            client = clientFactory(client);
        }

        return client;
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new OpenAIErrorHandler();
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
            SupportsAudio = true,
            DefaulTMetadataWindow = 128000, // GPT-4 Turbo
            DocumentationUrl = "https://platform.openai.com/docs"
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in OpenAIProviderModule")]
    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        var errors = new List<string>();

        // Note: API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            errors.Add("API key is required for OpenAI. " +
                      "Set it via the apiKey parameter, OPENAI_API_KEY environment variable, or configuration.");
        }

        // Validate model name
        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required for OpenAI");

        // Validate OpenAI-specific config if present
        var openAIConfig = config.GetTypedProviderConfig<OpenAIProviderConfig>();
        if (openAIConfig != null)
        {
            // Validate Temperature range
            if (openAIConfig.Temperature.HasValue && (openAIConfig.Temperature.Value < 0 || openAIConfig.Temperature.Value > 2))
            {
                errors.Add("Temperature must be between 0 and 2");
            }

            // Validate TopP range
            if (openAIConfig.TopP.HasValue && (openAIConfig.TopP.Value < 0 || openAIConfig.TopP.Value > 1))
            {
                errors.Add("TopP must be between 0 and 1");
            }

            // Validate FrequencyPenalty range
            if (openAIConfig.FrequencyPenalty.HasValue && (openAIConfig.FrequencyPenalty.Value < -2 || openAIConfig.FrequencyPenalty.Value > 2))
            {
                errors.Add("FrequencyPenalty must be between -2 and 2");
            }

            // Validate PresencePenalty range
            if (openAIConfig.PresencePenalty.HasValue && (openAIConfig.PresencePenalty.Value < -2 || openAIConfig.PresencePenalty.Value > 2))
            {
                errors.Add("PresencePenalty must be between -2 and 2");
            }

            // Validate StopSequences count
            if (openAIConfig.StopSequences != null && openAIConfig.StopSequences.Count > 4)
            {
                errors.Add("Maximum of 4 stop sequences allowed");
            }

            // Validate TopLogProbabilityCount range
            if (openAIConfig.TopLogProbabilityCount.HasValue && (openAIConfig.TopLogProbabilityCount.Value < 0 || openAIConfig.TopLogProbabilityCount.Value > 20))
            {
                errors.Add("TopLogProbabilityCount must be between 0 and 20");
            }

            // Validate ResponseFormat
            if (!string.IsNullOrEmpty(openAIConfig.ResponseFormat))
            {
                var validFormats = new[] { "text", "json_object", "json_schema" };
                if (!validFormats.Contains(openAIConfig.ResponseFormat))
                {
                    errors.Add("ResponseFormat must be one of: text, json_object, json_schema");
                }

                // Validate json_schema requirements
                if (openAIConfig.ResponseFormat == "json_schema")
                {
                    if (string.IsNullOrEmpty(openAIConfig.JsonSchemaName))
                    {
                        errors.Add("JsonSchemaName is required when ResponseFormat is json_schema");
                    }
                    if (string.IsNullOrEmpty(openAIConfig.JsonSchema))
                    {
                        errors.Add("JsonSchema is required when ResponseFormat is json_schema");
                    }
                }
            }

            // Validate ToolChoice
            if (!string.IsNullOrEmpty(openAIConfig.ToolChoice))
            {
                var validChoices = new[] { "auto", "none", "required" };
                if (!validChoices.Contains(openAIConfig.ToolChoice))
                {
                    errors.Add("ToolChoice must be one of: auto, none, required");
                }
            }

            // Validate ReasoningEffortLevel
            if (!string.IsNullOrEmpty(openAIConfig.ReasoningEffortLevel))
            {
                var validLevels = new[] { "low", "medium", "high", "minimal" };
                if (!validLevels.Contains(openAIConfig.ReasoningEffortLevel))
                {
                    errors.Add("ReasoningEffortLevel must be one of: low, medium, high, minimal");
                }
            }

            // Validate AudioVoice
            if (!string.IsNullOrEmpty(openAIConfig.AudioVoice))
            {
                var validVoices = new[] { "alloy", "ash", "ballad", "coral", "echo", "sage", "shimmer", "verse" };
                if (!validVoices.Contains(openAIConfig.AudioVoice))
                {
                    errors.Add("AudioVoice must be one of: alloy, ash, ballad, coral, echo, sage, shimmer, verse");
                }
            }

            // Validate AudioFormat
            if (!string.IsNullOrEmpty(openAIConfig.AudioFormat))
            {
                var validFormats = new[] { "wav", "mp3", "flac", "opus", "pcm16" };
                if (!validFormats.Contains(openAIConfig.AudioFormat))
                {
                    errors.Add("AudioFormat must be one of: wav, mp3, flac, opus, pcm16");
                }
            }

            // Validate ServiceTier
            if (!string.IsNullOrEmpty(openAIConfig.ServiceTier))
            {
                var validTiers = new[] { "auto", "default" };
                if (!validTiers.Contains(openAIConfig.ServiceTier))
                {
                    errors.Add("ServiceTier must be one of: auto, default");
                }
            }
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }
}

/// <summary>
/// Azure OpenAI provider implementation (traditional API key-based endpoints).
/// Uses the newer Responses API (ResponsesClient) for enhanced capabilities.
/// For modern Azure AI Projects/Foundry, use the AzureAI provider instead.
/// </summary>
internal class AzureOpenAIProvider : IProviderFeatures
{
    public string ProviderKey => "azure-openai";
    public string DisplayName => "Azure OpenAI (Traditional)";

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
        var endpointTask = secrets.RequireAsync("azure-openai:Endpoint", "Azure OpenAI", config.Endpoint, CancellationToken.None);
        string endpoint = endpointTask.GetAwaiter().GetResult();

        // Resolve required API key using ISecretResolver
        var apiKeyTask = secrets.RequireAsync("azure-openai:ApiKey", "Azure OpenAI", config.ApiKey, CancellationToken.None);
        string apiKey = apiKeyTask.GetAwaiter().GetResult();

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For Azure OpenAI, the ModelName (deployment name) must be configured.");
        }

        // Create Azure OpenAI client and get ResponsesClient
        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey)
        );

        var responsesClient = azureClient.GetResponsesClient(modelName);
        IChatClient client = responsesClient.AsIChatClient();

        // Apply client factory middleware if provided
        if (config.AdditionalProperties?.TryGetValue("ClientFactory", out var factoryObj) == true &&
            factoryObj is Func<IChatClient, IChatClient> clientFactory)
        {
            client = clientFactory(client);
        }

        return client;
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new OpenAIErrorHandler(); // Same error format as OpenAI
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
            DefaulTMetadataWindow = 128000,
            DocumentationUrl = "https://learn.microsoft.com/azure/ai-services/openai/"
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in OpenAIProviderModule")]
    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        var errors = new List<string>();

        // Note: Endpoint and API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.Endpoint))
        {
            errors.Add("Endpoint is required for Azure OpenAI. " +
                      "Set it via the endpoint parameter, AZURE_OPENAI_ENDPOINT environment variable, or configuration.");
        }

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            errors.Add("API key is required for Azure OpenAI. " +
                      "Set it via the apiKey parameter, AZURE_OPENAI_API_KEY environment variable, or configuration.");
        }

        // Validate model name
        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name (deployment name) is required for Azure OpenAI");

        // Validate OpenAI-specific config if present (same validation as OpenAI)
        var openAIConfig = config.GetTypedProviderConfig<OpenAIProviderConfig>();
        if (openAIConfig != null)
        {
            // Validate Temperature range
            if (openAIConfig.Temperature.HasValue && (openAIConfig.Temperature.Value < 0 || openAIConfig.Temperature.Value > 2))
            {
                errors.Add("Temperature must be between 0 and 2");
            }

            // Validate TopP range
            if (openAIConfig.TopP.HasValue && (openAIConfig.TopP.Value < 0 || openAIConfig.TopP.Value > 1))
            {
                errors.Add("TopP must be between 0 and 1");
            }

            // Validate FrequencyPenalty range
            if (openAIConfig.FrequencyPenalty.HasValue && (openAIConfig.FrequencyPenalty.Value < -2 || openAIConfig.FrequencyPenalty.Value > 2))
            {
                errors.Add("FrequencyPenalty must be between -2 and 2");
            }

            // Validate PresencePenalty range
            if (openAIConfig.PresencePenalty.HasValue && (openAIConfig.PresencePenalty.Value < -2 || openAIConfig.PresencePenalty.Value > 2))
            {
                errors.Add("PresencePenalty must be between -2 and 2");
            }

            // Add other validations as needed...
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }
}
