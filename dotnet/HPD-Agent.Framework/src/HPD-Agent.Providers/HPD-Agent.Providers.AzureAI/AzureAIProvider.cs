#pragma warning disable AOAI001 // AzureOpenAIClientOptions default headers/query parameters are experimental SDK options

using System;
using System.Collections.Generic;
using System.ClientModel.Primitives;
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
/// Also supports direct Azure OpenAI-compatible endpoints.
/// </para>
/// <para>
/// Authentication methods:
/// 1. DefaultAzureCredential (recommended) - OAuth/Entra ID authentication
/// 2. API Key - For endpoints that support key-based authentication
/// </para>
/// </remarks>
[HpdProvider("azure-ai", "Azure AI (Projects)")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(AzureAIProviderConfig), typeof(AzureAIJsonContext))]
[HpdProviderSecretAlias("azure-ai:ApiKey", "AZURE_AI_API_KEY")]
[HpdProviderSecretAlias("azure-ai:Endpoint", "AZURE_AI_ENDPOINT")]
internal class AzureAIProvider : IChatClientProvider, IProviderSecretAliasProvider
{
    public string ProviderKey => "azure-ai";
    public string DisplayName => "Azure AI (Projects)";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("azure-ai:ApiKey", new[] { "AZURE_AI_API_KEY" }),
            new("azure-ai:Endpoint", new[] { "AZURE_AI_ENDPOINT" }),
        };

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

        // Resolve required endpoint using ISecretResolver (Azure requires endpoint)
        string endpoint = await secrets.RequireAsync("azure-ai:Endpoint", "Azure AI", config.Endpoint, cancellationToken).ConfigureAwait(false);

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For AzureAI, the ModelName (deployment name) must be configured.");
        }

        // Get typed config
        var azureConfig = config.ProviderConfig as AzureAIProviderConfig;

        var authMode = azureConfig?.AuthMode ?? AzureAIAuthMode.Auto;
        string? apiKey = authMode == AzureAIAuthMode.DefaultAzureCredential
            ? null
            : secrets.ResolveOrDefaultAsync("azure-ai:ApiKey", config.ApiKey, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

        if (authMode == AzureAIAuthMode.ApiKey && string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "Azure AI API key authentication was requested, but no API key was configured. " +
                "Set apiKey, AZURE_AI_API_KEY, or change AuthMode to DefaultAzureCredential.");
        }

        // Create chat client based on endpoint type
        IChatClient chatClient;
        Uri endpointUri = new Uri(endpoint);

        // Check if this is an Azure AI Projects endpoint
        if (endpoint.Contains("services.ai.azure.com") && endpoint.Contains("/api/projects/"))
        {
            // Azure AI Projects endpoint - only supports OAuth (DefaultAzureCredential)
            // For Azure AI Foundry, API keys are not supported - must use OAuth
            if (!string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException(
                    "Azure AI Foundry/Projects endpoints require OAuth authentication. " +
                    "Set AuthMode = DefaultAzureCredential or omit the API key.");
            }

            TokenCredential credential = new DefaultAzureCredential();
            chatClient = CreateProjectsChatClient(endpointUri, modelName, credential, azureConfig);
        }
        else
        {
            // Traditional Azure OpenAI endpoint - supports both auth methods
            if (string.IsNullOrEmpty(apiKey))
            {
                // Use OAuth
                TokenCredential credential = new DefaultAzureCredential();
                chatClient = CreateAzureOpenAIChatClient(endpointUri, modelName, credential, azureConfig);
            }
            else
            {
                // Use API key
                chatClient = CreateAzureOpenAIChatClientWithKey(endpointUri, modelName, apiKey, azureConfig);
            }
        }

        return chatClient;
    }

    private static IChatClient CreateProjectsChatClient(
        Uri projectEndpoint,
        string modelName,
        TokenCredential credential,
        AzureAIProviderConfig? config)
    {
        // Create AIProjectClient
        var projectClient = new AIProjectClient(projectEndpoint, credential, CreateProjectClientOptions(config));

        // Get the Azure OpenAI connection from the project
        var connectionId = string.IsNullOrWhiteSpace(config?.OpenAIConnectionId)
            ? typeof(AzureOpenAIClient).FullName!
            : config.OpenAIConnectionId;
        var connection = projectClient.GetConnection(connectionId);

        if (!connection.TryGetLocatorAsUri(out Uri? openAIUri) || openAIUri is null)
        {
            throw new InvalidOperationException("Failed to get Azure OpenAI connection URI from AI Project.");
        }

        // Create Azure OpenAI client using the connection
        var azureOpenAIClient = new AzureOpenAIClient(
            new Uri($"https://{openAIUri.Host}"),
            credential,
            CreateAzureOpenAIClientOptions(config));
        var chatClient = azureOpenAIClient.GetChatClient(modelName);

        // Convert to IChatClient using Microsoft.Extensions.AI
        return chatClient.AsIChatClient();
    }

    private static IChatClient CreateAzureOpenAIChatClient(
        Uri endpoint,
        string modelName,
        TokenCredential credential,
        AzureAIProviderConfig? config)
    {
        // Direct Azure OpenAI endpoint with OAuth
        var azureOpenAIClient = new AzureOpenAIClient(endpoint, credential, CreateAzureOpenAIClientOptions(config));
        var chatClient = azureOpenAIClient.GetChatClient(modelName);

        // Convert to IChatClient using Microsoft.Extensions.AI
        return chatClient.AsIChatClient();
    }

    private static IChatClient CreateAzureOpenAIChatClientWithKey(
        Uri endpoint,
        string modelName,
        string apiKey,
        AzureAIProviderConfig? config)
    {
        // Direct Azure OpenAI endpoint with API key
        var azureOpenAIClient = new AzureOpenAIClient(
            endpoint,
            new System.ClientModel.ApiKeyCredential(apiKey),
            CreateAzureOpenAIClientOptions(config));
        var chatClient = azureOpenAIClient.GetChatClient(modelName);

        // Convert to IChatClient using Microsoft.Extensions.AI
        return chatClient.AsIChatClient();
    }

    private static AIProjectClientOptions CreateProjectClientOptions(AzureAIProviderConfig? config)
    {
        var options = config?.ProjectServiceVersion is { } serviceVersion
            ? new AIProjectClientOptions(ToSdkProjectServiceVersion(serviceVersion))
            : new AIProjectClientOptions();

        if (config is null)
            return options;

        if (!string.IsNullOrEmpty(config.UserAgentApplicationId))
            options.UserAgentApplicationId = config.UserAgentApplicationId;

        ApplyPipelineOptions(options, config.NetworkTimeoutMs, config.EnableDistributedTracing);

        return options;
    }

    private static AzureOpenAIClientOptions CreateAzureOpenAIClientOptions(AzureAIProviderConfig? config)
    {
        var options = config?.OpenAIServiceVersion is { } serviceVersion
            ? new AzureOpenAIClientOptions(ToSdkOpenAIServiceVersion(serviceVersion))
            : new AzureOpenAIClientOptions();

        if (config is null)
            return options;

        if (!string.IsNullOrEmpty(config.OpenAIAudience))
            options.Audience = new AzureOpenAIAudience(config.OpenAIAudience);

        if (config.OpenAIDefaultHeaders is { Count: > 0 })
            options.DefaultHeaders = config.OpenAIDefaultHeaders;

        if (config.OpenAIDefaultQueryParameters is { Count: > 0 })
            options.DefaultQueryParameters = config.OpenAIDefaultQueryParameters;

        if (!string.IsNullOrEmpty(config.UserAgentApplicationId))
            options.UserAgentApplicationId = config.UserAgentApplicationId;

        ApplyPipelineOptions(options, config.NetworkTimeoutMs, config.EnableDistributedTracing);

        return options;
    }

    private static AIProjectClientOptions.ServiceVersion ToSdkProjectServiceVersion(AzureAIProjectServiceVersion version)
        => version switch
        {
            AzureAIProjectServiceVersion.V2025_05_01 => AIProjectClientOptions.ServiceVersion.V2025_05_01,
            AzureAIProjectServiceVersion.V1 => AIProjectClientOptions.ServiceVersion.V1,
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported Azure AI Projects service version.")
        };

    private static AzureOpenAIClientOptions.ServiceVersion ToSdkOpenAIServiceVersion(AzureAIOpenAIServiceVersion version)
        => version switch
        {
            AzureAIOpenAIServiceVersion.V2024_06_01 => AzureOpenAIClientOptions.ServiceVersion.V2024_06_01,
            AzureAIOpenAIServiceVersion.V2024_08_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2024_08_01_Preview,
            AzureAIOpenAIServiceVersion.V2024_09_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2024_09_01_Preview,
            AzureAIOpenAIServiceVersion.V2024_10_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2024_10_01_Preview,
            AzureAIOpenAIServiceVersion.V2024_10_21 => AzureOpenAIClientOptions.ServiceVersion.V2024_10_21,
            AzureAIOpenAIServiceVersion.V2024_12_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2024_12_01_Preview,
            AzureAIOpenAIServiceVersion.V2025_01_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2025_01_01_Preview,
            AzureAIOpenAIServiceVersion.V2025_03_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2025_03_01_Preview,
            AzureAIOpenAIServiceVersion.V2025_04_01_Preview => AzureOpenAIClientOptions.ServiceVersion.V2025_04_01_Preview,
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported Azure OpenAI service version.")
        };

    private static void ApplyPipelineOptions(
        ClientPipelineOptions options,
        int? networkTimeoutMs,
        bool? enableDistributedTracing)
    {
        if (networkTimeoutMs.HasValue)
            options.NetworkTimeout = TimeSpan.FromMilliseconds(networkTimeoutMs.Value);

        if (enableDistributedTracing.HasValue)
            options.EnableDistributedTracing = enableDistributedTracing.Value;
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
            DocumentationUri = new Uri("https://learn.microsoft.com/en-us/azure/ai-studio/"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = true,
                        ["SupportsVision"] = true
                    }
                }
            }
        };
    }

    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name (deployment name) is required for Azure AI");

        var azureConfig = config.ProviderConfig as AzureAIProviderConfig;
        if (azureConfig is not null)
        {
            if (!Enum.IsDefined(azureConfig.AuthMode))
                errors.Add("Azure AI AuthMode must be Auto, ApiKey, or DefaultAzureCredential.");

            if (azureConfig.ProjectServiceVersion.HasValue && !Enum.IsDefined(azureConfig.ProjectServiceVersion.Value))
                errors.Add("Azure AI ProjectServiceVersion must be a supported AIProjectClientOptions.ServiceVersion value.");

            if (azureConfig.OpenAIServiceVersion.HasValue && !Enum.IsDefined(azureConfig.OpenAIServiceVersion.Value))
                errors.Add("Azure AI OpenAIServiceVersion must be a supported AzureOpenAIClientOptions.ServiceVersion value.");

            if (azureConfig.NetworkTimeoutMs is <= 0)
                errors.Add("Azure AI NetworkTimeoutMs must be greater than 0 when specified.");
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }
}
