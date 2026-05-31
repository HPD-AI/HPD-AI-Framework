#pragma warning disable OPENAI001 // ResponsesClient is experimental
#pragma warning disable MEAI001 // Some Microsoft.Extensions.AI OpenAI client families are experimental

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
using OpenAI;

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
internal class OpenAIProvider :
    IChatClientProvider,
    IImageGeneratorProvider,
    IEmbeddingGeneratorProvider,
    IHostedFileClientProvider
{
    public string ProviderKey => "openai";
    public string DisplayName => "OpenAI";

    public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        // Get secret resolver from services
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets == null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For OpenAI, the ModelName must be configured.");
        }

        IChatClient client;

        // Create the OpenAI client and get the ResponsesClient
        var openAIClient = CreateOpenAIClient(config, secrets);
        var responsesClient = openAIClient.GetResponsesClient();
        client = responsesClient.AsIChatClient();

        return client;
    }

    public IImageGenerator CreateImageGenerator(ClientProviderConfig config, IServiceProvider? services = null)
    {
        var secrets = GetSecretResolver(services);
        var modelName = RequireModelName(config, "OpenAI image generation");
        return CreateOpenAIClient(config, secrets).GetImageClient(modelName).AsIImageGenerator();
    }

    public IEmbeddingGenerator CreateEmbeddingGenerator(ClientProviderConfig config, IServiceProvider? services = null)
    {
        var secrets = GetSecretResolver(services);
        var modelName = RequireModelName(config, "OpenAI embeddings");
        return CreateOpenAIClient(config, secrets).GetEmbeddingClient(modelName).AsIEmbeddingGenerator();
    }

    public IHostedFileClient CreateHostedFileClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        var secrets = GetSecretResolver(services);
        return CreateOpenAIClient(config, secrets).AsIHostedFileClient();
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
            DocumentationUri = new Uri("https://platform.openai.com/docs"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = true,
                        ["SupportsVision"] = true,
                        ["DefaultMetadataWindow"] = 128000
                    }
                },
                [ProviderClientFamily.ImageGeneration] = new()
                {
                    Family = ProviderClientFamily.ImageGeneration,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = false
                    }
                },
                [ProviderClientFamily.Embeddings] = new()
                {
                    Family = ProviderClientFamily.Embeddings
                },
                [ProviderClientFamily.HostedFiles] = new()
                {
                    Family = ProviderClientFamily.HostedFiles,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsContainerFiles"] = true
                    }
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in OpenAIProviderModule")]
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
    {
        var errors = new List<string>();

        // Note: API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            errors.Add("API key is required for OpenAI. " +
                      "Set it via the apiKey parameter, OPENAI_API_KEY environment variable, or configuration.");
        }

        // Validate model name for model-scoped families. Hosted files are account/client scoped.
        if (family != ProviderClientFamily.HostedFiles && string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required for OpenAI");

        // Validate OpenAI-specific config if present
        var openAIConfig = config.GetProviderConfig<OpenAIProviderConfig>();
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

    private static ISecretResolver GetSecretResolver(IServiceProvider? services)
    {
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets == null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        return secrets;
    }

    private static string RequireModelName(ClientProviderConfig config, string scenario)
    {
        if (string.IsNullOrEmpty(config.ModelName))
            throw new InvalidOperationException($"For {scenario}, the ModelName must be configured.");

        return config.ModelName;
    }

    private static OpenAIClient CreateOpenAIClient(ClientProviderConfig config, ISecretResolver secrets)
    {
        var apiKeyTask = secrets.RequireAsync("openai:ApiKey", "OpenAI", config.ApiKey, CancellationToken.None);
        var apiKey = apiKeyTask.GetAwaiter().GetResult();

        var endpointTask = secrets.ResolveOrDefaultAsync("openai:Endpoint", config.Endpoint, CancellationToken.None);
        var endpoint = endpointTask.GetAwaiter().GetResult();
        var hasCustomEndpoint = !string.IsNullOrEmpty(endpoint);
        var hasCustomHeaders = config.CustomHeaders?.Count > 0;

        var options = new OpenAIClientOptions();

        if (hasCustomEndpoint || hasCustomHeaders)
        {
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

        return new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);
    }
}

/// <summary>
/// Azure OpenAI provider implementation (traditional API key-based endpoints).
/// Uses the newer Responses API (ResponsesClient) for enhanced capabilities.
/// For modern Azure AI Projects/Foundry, use the AzureAI provider instead.
/// </summary>
internal class AzureOpenAIProvider :
    IChatClientProvider,
    IImageGeneratorProvider,
    IEmbeddingGeneratorProvider,
    IHostedFileClientProvider
{
    public string ProviderKey => "azure-openai";
    public string DisplayName => "Azure OpenAI (Traditional)";

    public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For Azure OpenAI, the ModelName (deployment name) must be configured.");
        }

        // Create Azure OpenAI client and get ResponsesClient
        var azureClient = CreateAzureOpenAIClient(config, GetSecretResolver(services));

        var responsesClient = azureClient.GetResponsesClient();
        IChatClient client = responsesClient.AsIChatClient();

        return client;
    }

    public IImageGenerator CreateImageGenerator(ClientProviderConfig config, IServiceProvider? services = null)
    {
        var modelName = RequireDeploymentName(config, "Azure OpenAI image generation");
        return CreateAzureOpenAIClient(config, GetSecretResolver(services)).GetImageClient(modelName).AsIImageGenerator();
    }

    public IEmbeddingGenerator CreateEmbeddingGenerator(ClientProviderConfig config, IServiceProvider? services = null)
    {
        var modelName = RequireDeploymentName(config, "Azure OpenAI embeddings");
        return CreateAzureOpenAIClient(config, GetSecretResolver(services)).GetEmbeddingClient(modelName).AsIEmbeddingGenerator();
    }

    public IHostedFileClient CreateHostedFileClient(ClientProviderConfig config, IServiceProvider? services = null)
    {
        return CreateAzureOpenAIClient(config, GetSecretResolver(services)).GetOpenAIFileClient().AsIHostedFileClient();
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
            DocumentationUri = new Uri("https://learn.microsoft.com/azure/ai-services/openai/"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = true,
                        ["SupportsVision"] = true,
                        ["DefaultMetadataWindow"] = 128000
                    }
                },
                [ProviderClientFamily.ImageGeneration] = new()
                {
                    Family = ProviderClientFamily.ImageGeneration,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = false
                    }
                },
                [ProviderClientFamily.Embeddings] = new()
                {
                    Family = ProviderClientFamily.Embeddings
                },
                [ProviderClientFamily.HostedFiles] = new()
                {
                    Family = ProviderClientFamily.HostedFiles,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsContainerFiles"] = false
                    }
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in OpenAIProviderModule")]
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
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

        // Validate model/deployment name for model-scoped families. Hosted files are account/client scoped.
        if (family != ProviderClientFamily.HostedFiles && string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name (deployment name) is required for Azure OpenAI");

        // Validate OpenAI-specific config if present (same validation as OpenAI)
        var openAIConfig = config.GetProviderConfig<OpenAIProviderConfig>();
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

    private static ISecretResolver GetSecretResolver(IServiceProvider? services)
    {
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets == null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        return secrets;
    }

    private static string RequireDeploymentName(ClientProviderConfig config, string scenario)
    {
        if (string.IsNullOrEmpty(config.ModelName))
            throw new InvalidOperationException($"For {scenario}, the ModelName (deployment name) must be configured.");

        return config.ModelName;
    }

    private static AzureOpenAIClient CreateAzureOpenAIClient(ClientProviderConfig config, ISecretResolver secrets)
    {
        var endpointTask = secrets.RequireAsync("azure-openai:Endpoint", "Azure OpenAI", config.Endpoint, CancellationToken.None);
        var endpoint = endpointTask.GetAwaiter().GetResult();

        var apiKeyTask = secrets.RequireAsync("azure-openai:ApiKey", "Azure OpenAI", config.ApiKey, CancellationToken.None);
        var apiKey = apiKeyTask.GetAwaiter().GetResult();

        return new AzureOpenAIClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));
    }
}
