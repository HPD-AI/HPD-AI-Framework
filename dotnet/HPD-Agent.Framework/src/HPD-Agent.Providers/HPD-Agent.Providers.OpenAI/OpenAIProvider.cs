#pragma warning disable OPENAI001 // ResponsesClient is experimental
#pragma warning disable MEAI001 // Some Microsoft.Extensions.AI OpenAI client families are experimental
#pragma warning disable AOAI001 // AzureOpenAIClientOptions default headers/query parameters are experimental SDK options

using System;
using System.Collections.Generic;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using Azure.AI.OpenAI;
using Azure;
using Azure.Core;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;
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
[HpdProvider("openai", "OpenAI")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "openai:ApiKey")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderFamily(ProviderClientFamily.ImageGeneration)]
[HpdProviderFamily(ProviderClientFamily.Embeddings)]
[HpdProviderFamily(ProviderClientFamily.HostedFiles)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(OpenAIProviderConfig), typeof(OpenAIJsonContext))]
[HpdProviderPayload(ProviderClientFamily.ImageGeneration, ProviderPayloadKind.Configuration, typeof(OpenAIProviderConfig), typeof(OpenAIJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Embeddings, ProviderPayloadKind.Configuration, typeof(OpenAIProviderConfig), typeof(OpenAIJsonContext))]
[HpdProviderPayload(ProviderClientFamily.HostedFiles, ProviderPayloadKind.Configuration, typeof(OpenAIProviderConfig), typeof(OpenAIJsonContext))]
[HpdProviderSecretAlias("openai:ApiKey", "OPENAI_API_KEY")]
internal class OpenAIProvider :
    IProvider,
    IProviderClientFactory<IChatClient>,
    IProviderClientFactory<IImageGenerator>,
    IProviderClientFactory<IEmbeddingGenerator>,
    IProviderClientFactory<IHostedFileClient>,
    IProviderSecretAliasProvider
{
    public string ProviderKey => "openai";
    public string DisplayName => "OpenAI";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("openai:ApiKey", new[] { "OPENAI_API_KEY" }),
        };

    ProviderClientCredentialBinding IProviderClientFactory<IChatClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IImageGenerator>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IEmbeddingGenerator>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IHostedFileClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);

    ValueTask<ProviderClientConstruction<IChatClient>> IProviderClientFactory<IChatClient>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        string? modelName = context.EffectiveConfig.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For OpenAI, the ModelName must be configured.");
        }

        IChatClient client;

        var openAIConfig = ReadConfig(context.EffectiveConfig);
        var openAIClient = CreateOpenAIClient(context);
        client = openAIConfig?.ChatApi == OpenAIChatApi.ChatCompletions
            ? openAIClient.GetChatClient(modelName).AsIChatClient()
            : openAIClient.GetResponsesClient().AsIChatClient(modelName);

        return Construct(client);
    }

    ValueTask<ProviderClientConstruction<IImageGenerator>> IProviderClientFactory<IImageGenerator>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        var modelName = RequireModelName(context.EffectiveConfig, "OpenAI image generation");
        return Construct(CreateOpenAIClient(context).GetImageClient(modelName).AsIImageGenerator());
    }

    ValueTask<ProviderClientConstruction<IEmbeddingGenerator>> IProviderClientFactory<IEmbeddingGenerator>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        var modelName = RequireModelName(context.EffectiveConfig, "OpenAI embeddings");
        IEmbeddingGenerator generator = CreateOpenAIClient(context).GetEmbeddingClient(modelName).AsIEmbeddingGenerator();
        return Construct(generator);
    }

    ValueTask<ProviderClientConstruction<IHostedFileClient>> IProviderClientFactory<IHostedFileClient>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        return Construct(CreateOpenAIClient(context).AsIHostedFileClient());
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

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Generated OpenAI provider payload contracts are AOT-compatible")]
    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        var errors = new List<string>();

        // Validate model name for model-scoped families. Hosted files are account/client scoped.
        if (config.Family != ProviderClientFamily.HostedFiles && string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required for OpenAI");

        OpenAIProviderConfigValidation.Validate(config, errors);

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private static ProviderClientCredentialBinding ResolveBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    private static string RequireModelName(EffectiveProviderClientConfig config, string scenario)
    {
        if (string.IsNullOrEmpty(config.ModelName))
            throw new InvalidOperationException($"For {scenario}, the ModelName must be configured.");

        return config.ModelName;
    }

    private static OpenAIClient CreateOpenAIClient(ProviderClientConstructionContext context)
    {
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);
        var config = context.EffectiveConfig;
        var hasCustomEndpoint = config.Endpoint is not null;
        var hasCustomHeaders = config.CustomHeaders?.Count > 0;
        var openAIConfig = ReadConfig(config);

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
                options.Endpoint = config.Endpoint!;
            }

            options.Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(httpClient);
        }

        return new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);
    }

    private static OpenAIProviderConfig? ReadConfig(EffectiveProviderClientConfig config) =>
        config.ProviderConfiguration.CanonicalPayload.IsEmpty ? null :
            JsonSerializer.Deserialize(config.ProviderConfiguration.CanonicalPayload.AsSpan(), OpenAIJsonContext.Default.OpenAIProviderConfig);

    private static void ValidateContext(ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static ValueTask<ProviderClientConstruction<TClient>> Construct<TClient>(TClient client)
        where TClient : class => ValueTask.FromResult(new ProviderClientConstruction<TClient>
        {
            Client = client,
            Owner = ProviderClientConstructionUtilities.Own(client)
        });

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
    public static void Validate(EffectiveProviderClientConfig config, List<string> errors)
    {
        var openAIConfig = config.ProviderConfiguration.CanonicalPayload.IsEmpty ? null :
            JsonSerializer.Deserialize(config.ProviderConfiguration.CanonicalPayload.AsSpan(), OpenAIJsonContext.Default.OpenAIProviderConfig);
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
[HpdProvider("azure-openai", "Azure OpenAI (Traditional)")]
[HpdProviderBackend("azure", ProviderAuthenticationKind.ApiKey, DefaultSecretKey = "azure-openai:ApiKey")]
[HpdProviderBackend("azure", ProviderAuthenticationKind.ExternalIdentity, IsDefaultBackend = true, IsDefaultAuthentication = true)]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderFamily(ProviderClientFamily.ImageGeneration)]
[HpdProviderFamily(ProviderClientFamily.Embeddings)]
[HpdProviderFamily(ProviderClientFamily.HostedFiles)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(AzureOpenAIProviderConfig), typeof(OpenAIJsonContext))]
[HpdProviderPayload(ProviderClientFamily.ImageGeneration, ProviderPayloadKind.Configuration, typeof(AzureOpenAIProviderConfig), typeof(OpenAIJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Embeddings, ProviderPayloadKind.Configuration, typeof(AzureOpenAIProviderConfig), typeof(OpenAIJsonContext))]
[HpdProviderPayload(ProviderClientFamily.HostedFiles, ProviderPayloadKind.Configuration, typeof(AzureOpenAIProviderConfig), typeof(OpenAIJsonContext))]
[HpdProviderSecretAlias("azure-openai:ApiKey", "AZURE_OPENAI_API_KEY")]
[HpdProviderSecretAlias("azure-openai:Endpoint", "AZURE_OPENAI_ENDPOINT")]
internal class AzureOpenAIProvider :
    IProvider,
    IProviderClientFactory<IChatClient>,
    IProviderClientFactory<IImageGenerator>,
    IProviderClientFactory<IEmbeddingGenerator>,
    IProviderClientFactory<IHostedFileClient>,
    IProviderSecretAliasProvider
{
    public string ProviderKey => "azure-openai";
    public string DisplayName => "Azure OpenAI (Traditional)";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("azure-openai:ApiKey", new[] { "AZURE_OPENAI_API_KEY" }),
            new("azure-openai:Endpoint", new[] { "AZURE_OPENAI_ENDPOINT" }),
        };

    ProviderClientCredentialBinding IProviderClientFactory<IChatClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IImageGenerator>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IEmbeddingGenerator>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);
    ProviderClientCredentialBinding IProviderClientFactory<IHostedFileClient>.ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) => ResolveBinding(descriptor);

    ValueTask<ProviderClientConstruction<IChatClient>> IProviderClientFactory<IChatClient>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        string? modelName = context.EffectiveConfig.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For Azure OpenAI, the ModelName (deployment name) must be configured.");
        }

        var openAIConfig = ReadConfig(context.EffectiveConfig);
        var azureClient = CreateAzureOpenAIClient(context);
        IChatClient client = openAIConfig?.ChatApi == OpenAIChatApi.ChatCompletions
            ? azureClient.GetChatClient(modelName).AsIChatClient()
            : azureClient.GetResponsesClient().AsIChatClient(modelName);

        return Construct(client);
    }

    ValueTask<ProviderClientConstruction<IImageGenerator>> IProviderClientFactory<IImageGenerator>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        var modelName = RequireDeploymentName(context.EffectiveConfig, "Azure OpenAI image generation");
        return Construct(CreateAzureOpenAIClient(context).GetImageClient(modelName).AsIImageGenerator());
    }

    ValueTask<ProviderClientConstruction<IEmbeddingGenerator>> IProviderClientFactory<IEmbeddingGenerator>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        var modelName = RequireDeploymentName(context.EffectiveConfig, "Azure OpenAI embeddings");
        IEmbeddingGenerator generator = CreateAzureOpenAIClient(context).GetEmbeddingClient(modelName).AsIEmbeddingGenerator();
        return Construct(generator);
    }

    ValueTask<ProviderClientConstruction<IHostedFileClient>> IProviderClientFactory<IHostedFileClient>.CreateAsync(
        ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ValidateContext(context, cancellationToken);
        return Construct(CreateAzureOpenAIClient(context).GetOpenAIFileClient().AsIHostedFileClient());
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

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Generated OpenAI provider payload contracts are AOT-compatible")]
    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        var errors = new List<string>();

        // Validate model/deployment name for model-scoped families. Hosted files are account/client scoped.
        if (config.Family != ProviderClientFamily.HostedFiles && string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name (deployment name) is required for Azure OpenAI");

        AzureOpenAIProviderConfigValidation.Validate(config, errors);

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private static ProviderClientCredentialBinding ResolveBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    private static string RequireDeploymentName(EffectiveProviderClientConfig config, string scenario)
    {
        if (string.IsNullOrEmpty(config.ModelName))
            throw new InvalidOperationException($"For {scenario}, the ModelName (deployment name) must be configured.");

        return config.ModelName;
    }

    private static AzureOpenAIClient CreateAzureOpenAIClient(ProviderClientConstructionContext context)
    {
        var config = context.EffectiveConfig;
        var endpoint = config.Endpoint ?? throw new InvalidOperationException("Azure OpenAI requires an explicit endpoint.");
        var azureConfig = ReadConfig(config);
        var options = CreateAzureOpenAIClientOptions(azureConfig);
        var lease = context.CredentialBinding is ProviderCredentialBindingContext.ConstructionTime value
            ? value.Lease
            : throw new InvalidOperationException("Azure OpenAI requires a construction credential.");
        return lease.Credential switch
        {
            ProviderCredential.ApiKey apiKey => new AzureOpenAIClient(endpoint,
                new System.ClientModel.ApiKeyCredential(apiKey.Value.Value.ToString()), options),
            ProviderCredential.ExternalIdentity external when external.Lease.Credential is TokenCredential tokenCredential =>
                new AzureOpenAIClient(endpoint, tokenCredential, options),
            _ => throw new InvalidOperationException("Azure OpenAI requires API-key or Azure TokenCredential authentication.")
        };
    }

    private static AzureOpenAIProviderConfig? ReadConfig(EffectiveProviderClientConfig config) =>
        config.ProviderConfiguration.CanonicalPayload.IsEmpty ? null :
            JsonSerializer.Deserialize(config.ProviderConfiguration.CanonicalPayload.AsSpan(), OpenAIJsonContext.Default.AzureOpenAIProviderConfig);

    private static void ValidateContext(ProviderClientConstructionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static ValueTask<ProviderClientConstruction<TClient>> Construct<TClient>(TClient client)
        where TClient : class => ValueTask.FromResult(new ProviderClientConstruction<TClient>
        {
            Client = client,
            Owner = ProviderClientConstructionUtilities.Own(client)
        });

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
    public static void Validate(EffectiveProviderClientConfig config, List<string> errors)
    {
        var azureConfig = config.ProviderConfiguration.CanonicalPayload.IsEmpty ? null :
            JsonSerializer.Deserialize(config.ProviderConfiguration.CanonicalPayload.AsSpan(), OpenAIJsonContext.Default.AzureOpenAIProviderConfig);
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
