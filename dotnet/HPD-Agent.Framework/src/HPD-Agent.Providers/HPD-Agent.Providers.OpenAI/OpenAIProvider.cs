#pragma warning disable OPENAI001 // ResponsesClient is experimental
#pragma warning disable MEAI001 // Some Microsoft.Extensions.AI OpenAI client families are experimental
#pragma warning disable AOAI001 // AzureOpenAIClientOptions default headers/query parameters are experimental SDK options

using System;
using System.Collections.Generic;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Azure.AI.OpenAI;
using Azure;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;

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

    public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
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

        var openAIConfig = config.GetProviderConfig<OpenAIProviderConfig>();
        var openAIClient = CreateOpenAIClient(config, secrets);
        client = openAIConfig?.ChatApi == OpenAIChatApi.ChatCompletions
            ? openAIClient.GetChatClient(modelName).AsIChatClient()
            : openAIClient.GetResponsesClient().AsIChatClient(modelName);

        return client;
    }

    public IImageGenerator CreateImageGenerator(ProviderClientConfig config, IServiceProvider? services = null)
    {
        var secrets = GetSecretResolver(services);
        var modelName = RequireModelName(config, "OpenAI image generation");
        return CreateOpenAIClient(config, secrets).GetImageClient(modelName).AsIImageGenerator();
    }

    public IEmbeddingGenerator CreateEmbeddingGenerator(ProviderClientConfig config, IServiceProvider? services = null)
    {
        var secrets = GetSecretResolver(services);
        var modelName = RequireModelName(config, "OpenAI embeddings");
        return CreateOpenAIClient(config, secrets).GetEmbeddingClient(modelName).AsIEmbeddingGenerator();
    }

    public IHostedFileClient CreateHostedFileClient(ProviderClientConfig config, IServiceProvider? services = null)
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
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        var errors = new List<string>();

        // Validate model name for model-scoped families. Hosted files are account/client scoped.
        if (family != ProviderClientFamily.HostedFiles && string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required for OpenAI");

        OpenAIProviderConfigValidation.Validate(config, errors);

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

    private static string RequireModelName(ProviderClientConfig config, string scenario)
    {
        if (string.IsNullOrEmpty(config.ModelName))
            throw new InvalidOperationException($"For {scenario}, the ModelName must be configured.");

        return config.ModelName;
    }

    private static OpenAIClient CreateOpenAIClient(ProviderClientConfig config, ISecretResolver secrets)
    {
        var apiKeyTask = secrets.RequireAsync("openai:ApiKey", "OpenAI", config.ApiKey, CancellationToken.None);
        var apiKey = apiKeyTask.GetAwaiter().GetResult();

        var endpointTask = secrets.ResolveOrDefaultAsync("openai:Endpoint", config.Endpoint, CancellationToken.None);
        var endpoint = endpointTask.GetAwaiter().GetResult();
        var hasCustomEndpoint = !string.IsNullOrEmpty(endpoint);
        var hasCustomHeaders = config.CustomHeaders?.Count > 0;
        var openAIConfig = config.GetProviderConfig<OpenAIProviderConfig>();

        var options = new OpenAIClientOptions();
        ApplyOpenAIOptions(options, openAIConfig);

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

    private static void ApplyOpenAIOptions(OpenAIClientOptions options, OpenAIProviderConfig? config)
    {
        if (config is null)
            return;

        if (!string.IsNullOrEmpty(config.OrganizationId))
            options.OrganizationId = config.OrganizationId;

        if (!string.IsNullOrEmpty(config.ProjectId))
            options.ProjectId = config.ProjectId;

        if (!string.IsNullOrEmpty(config.UserAgentApplicationId))
            options.UserAgentApplicationId = config.UserAgentApplicationId;

        ApplyPipelineOptions(options, config.NetworkTimeoutMs, config.EnableDistributedTracing);
    }

    internal static void ApplyPipelineOptions(
        ClientPipelineOptions options,
        int? networkTimeoutMs,
        bool? enableDistributedTracing)
    {
        if (networkTimeoutMs.HasValue)
            options.NetworkTimeout = TimeSpan.FromMilliseconds(networkTimeoutMs.Value);

        if (enableDistributedTracing.HasValue)
            options.EnableDistributedTracing = enableDistributedTracing.Value;
    }
}

internal static class OpenAIProviderConfigValidation
{
    public static void Validate(ProviderClientConfig config, List<string> errors)
    {
        var openAIConfig = config.GetProviderConfig<OpenAIProviderConfig>();
        if (openAIConfig is null)
            return;

        if (!Enum.IsDefined(openAIConfig.ChatApi))
            errors.Add("OpenAI ChatApi must be Responses or ChatCompletions.");

        if (openAIConfig.NetworkTimeoutMs is <= 0)
            errors.Add("OpenAI NetworkTimeoutMs must be greater than 0 when specified.");
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

    public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For Azure OpenAI, the ModelName (deployment name) must be configured.");
        }

        var openAIConfig = config.GetProviderConfig<AzureOpenAIProviderConfig>();
        var azureClient = CreateAzureOpenAIClient(config, GetSecretResolver(services));
        IChatClient client = openAIConfig?.ChatApi == OpenAIChatApi.ChatCompletions
            ? azureClient.GetChatClient(modelName).AsIChatClient()
            : azureClient.GetResponsesClient().AsIChatClient(modelName);

        return client;
    }

    public IImageGenerator CreateImageGenerator(ProviderClientConfig config, IServiceProvider? services = null)
    {
        var modelName = RequireDeploymentName(config, "Azure OpenAI image generation");
        return CreateAzureOpenAIClient(config, GetSecretResolver(services)).GetImageClient(modelName).AsIImageGenerator();
    }

    public IEmbeddingGenerator CreateEmbeddingGenerator(ProviderClientConfig config, IServiceProvider? services = null)
    {
        var modelName = RequireDeploymentName(config, "Azure OpenAI embeddings");
        return CreateAzureOpenAIClient(config, GetSecretResolver(services)).GetEmbeddingClient(modelName).AsIEmbeddingGenerator();
    }

    public IHostedFileClient CreateHostedFileClient(ProviderClientConfig config, IServiceProvider? services = null)
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
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        var errors = new List<string>();

        // Validate model/deployment name for model-scoped families. Hosted files are account/client scoped.
        if (family != ProviderClientFamily.HostedFiles && string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name (deployment name) is required for Azure OpenAI");

        AzureOpenAIProviderConfigValidation.Validate(config, errors);

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

    private static string RequireDeploymentName(ProviderClientConfig config, string scenario)
    {
        if (string.IsNullOrEmpty(config.ModelName))
            throw new InvalidOperationException($"For {scenario}, the ModelName (deployment name) must be configured.");

        return config.ModelName;
    }

    private static AzureOpenAIClient CreateAzureOpenAIClient(ProviderClientConfig config, ISecretResolver secrets)
    {
        var endpointTask = secrets.RequireAsync("azure-openai:Endpoint", "Azure OpenAI", config.Endpoint, CancellationToken.None);
        var endpoint = endpointTask.GetAwaiter().GetResult();

        var apiKeyTask = secrets.RequireAsync("azure-openai:ApiKey", "Azure OpenAI", config.ApiKey, CancellationToken.None);
        var apiKey = apiKeyTask.GetAwaiter().GetResult();
        var azureConfig = config.GetProviderConfig<AzureOpenAIProviderConfig>();
        var options = CreateAzureOpenAIClientOptions(azureConfig);

        return new AzureOpenAIClient(
            new Uri(endpoint),
            new System.ClientModel.ApiKeyCredential(apiKey),
            options);
    }

    private static AzureOpenAIClientOptions CreateAzureOpenAIClientOptions(AzureOpenAIProviderConfig? config)
    {
        var options = config?.ServiceVersion is { } serviceVersion
            ? new AzureOpenAIClientOptions(ToSdkServiceVersion(serviceVersion))
            : new AzureOpenAIClientOptions();

        if (config is null)
            return options;

        if (!string.IsNullOrEmpty(config.Audience))
            options.Audience = new AzureOpenAIAudience(config.Audience);

        if (config.DefaultHeaders is { Count: > 0 })
            options.DefaultHeaders = config.DefaultHeaders;

        if (config.DefaultQueryParameters is { Count: > 0 })
            options.DefaultQueryParameters = config.DefaultQueryParameters;

        if (!string.IsNullOrEmpty(config.UserAgentApplicationId))
            options.UserAgentApplicationId = config.UserAgentApplicationId;

        OpenAIProvider.ApplyPipelineOptions(options, config.NetworkTimeoutMs, config.EnableDistributedTracing);

        return options;
    }

    private static AzureOpenAIClientOptions.ServiceVersion ToSdkServiceVersion(AzureOpenAIServiceVersion version)
        => version switch
        {
            AzureOpenAIServiceVersion.V2024_06_01 => AzureOpenAIClientOptions.ServiceVersion.V2024_06_01,
            AzureOpenAIServiceVersion.V2024_08_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2024_08_01_Preview,
            AzureOpenAIServiceVersion.V2024_09_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2024_09_01_Preview,
            AzureOpenAIServiceVersion.V2024_10_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2024_10_01_Preview,
            AzureOpenAIServiceVersion.V2024_10_21 => AzureOpenAIClientOptions.ServiceVersion.V2024_10_21,
            AzureOpenAIServiceVersion.V2024_12_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2024_12_01_Preview,
            AzureOpenAIServiceVersion.V2025_01_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2025_01_01_Preview,
            AzureOpenAIServiceVersion.V2025_03_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2025_03_01_Preview,
            AzureOpenAIServiceVersion.V2025_04_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2025_04_01_Preview,
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported Azure OpenAI service version.")
        };
}

internal static class AzureOpenAIProviderConfigValidation
{
    public static void Validate(ProviderClientConfig config, List<string> errors)
    {
        var azureConfig = config.GetProviderConfig<AzureOpenAIProviderConfig>();
        if (azureConfig is null)
            return;

        if (!Enum.IsDefined(azureConfig.ChatApi))
            errors.Add("Azure OpenAI ChatApi must be Responses or ChatCompletions.");

        if (azureConfig.ServiceVersion.HasValue && !Enum.IsDefined(azureConfig.ServiceVersion.Value))
            errors.Add("Azure OpenAI ServiceVersion must be a supported AzureOpenAIClientOptions.ServiceVersion value.");

        if (azureConfig.NetworkTimeoutMs is <= 0)
            errors.Add("Azure OpenAI NetworkTimeoutMs must be greater than 0 when specified.");
    }
}
