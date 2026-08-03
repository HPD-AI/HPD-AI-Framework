using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace HPD.Agent.Providers.OpenAICompatible;

/// <summary>
/// Base implementation for small OpenAI-compatible chat-completions providers.
/// </summary>
public abstract class OpenAICompatibleChatProviderBase<TConfig> : IChatClientProvider
    where TConfig : OpenAICompatibleProviderConfig
{
    protected abstract OpenAICompatibleProviderDefinition Definition { get; }

    public string ProviderKey => Definition.ProviderKey;
    public string DisplayName => Definition.DisplayName;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider packages register AOT-compatible config deserializers through ProviderDiscovery.")]
    public virtual async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            throw new InvalidOperationException($"For {DisplayName}, the ModelName must be configured.");
        }

        var secretResolver = services?.GetService(typeof(ISecretResolver)) as ISecretResolver;
        var apiKey = ResolveApiKey(config, secretResolver);
        var endpoint = ResolveEndpoint(config, secretResolver);

        var httpClient = new HttpClient
        {
            BaseAddress = endpoint
        };
        ConfigureHttpClient(httpClient, config, apiKey);

        var providerConfig = ResolveProviderConfig(config);
        var innerClient = CreateOpenAICompatibleChatClient(httpClient, config, endpoint);
        return WrapChatClient(innerClient, config, endpoint, providerConfig);
    }

    public abstract IProviderErrorHandler CreateErrorHandler();

    public virtual ProviderMetadata GetMetadata()
        => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = Definition.DocumentationUri,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    DefaultModelId = Definition.DefaultModelId,
                    Capabilities = Definition.Capabilities
                }
            }
        };

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider packages register AOT-compatible config deserializers through ProviderDiscovery.")]
    public virtual ProviderValidationResult ValidateConfiguration(
        ProviderClientConfig config,
        ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();

        if (family != ProviderClientFamily.Chat)
        {
            errors.Add($"{DisplayName} currently supports only the chat provider family.");
        }

        if (string.IsNullOrWhiteSpace(config.ModelName))
        {
            errors.Add($"Model name is required for {DisplayName}");
        }

        if (!string.IsNullOrWhiteSpace(config.Endpoint) &&
            !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("Endpoint must be a valid, absolute URI");
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    protected virtual TConfig? ResolveProviderConfig(ProviderClientConfig config)
        => config.ProviderConfig as TConfig;

    protected virtual IChatClient CreateOpenAICompatibleChatClient(
        HttpClient httpClient,
        ProviderClientConfig config,
        Uri endpoint)
        => new OpenAICompatibleChatClient(httpClient, CreateChatClientOptions(config, endpoint));

    protected virtual OpenAICompatibleChatClientOptions CreateChatClientOptions(
        ProviderClientConfig config,
        Uri endpoint)
        => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            ProviderUri = Definition.ProviderUri ?? endpoint,
            DefaultModelId = config.ModelName,
            ChatCompletionsPath = Definition.ChatCompletionsPath,
            RequestProfile = Definition.RequestProfile
        };

    protected virtual IChatClient WrapChatClient(
        IChatClient innerClient,
        ProviderClientConfig config,
        Uri endpoint,
        TConfig? providerConfig)
        => new OpenAICompatibleConfiguredChatClient<TConfig>(
            innerClient,
            ProviderKey,
            config.ModelName,
            Definition.ProviderUri ?? endpoint,
            providerConfig);

    protected virtual void ConfigureHttpClient(
        HttpClient httpClient,
        ProviderClientConfig config,
        string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }

        if (config.CustomHeaders is null)
        {
            return;
        }

        foreach (var header in config.CustomHeaders)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected static Uri EnsureTrailingSlash(Uri endpoint)
        => endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? endpoint
            : new Uri(endpoint.AbsoluteUri + "/", UriKind.Absolute);

    private string? ResolveApiKey(ProviderClientConfig config, ISecretResolver? secretResolver)
    {
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return config.ApiKey;
        }

        if (!Definition.RequiresApiKey)
        {
            return null;
        }

        if (secretResolver is null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization when no apiKey is supplied. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        return secretResolver.RequireAsync(
                Definition.ApiKeySecretKey,
                DisplayName,
                config.ApiKey,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private Uri ResolveEndpoint(ProviderClientConfig config, ISecretResolver? secretResolver)
    {
        var endpointValue = config.Endpoint;
        if (string.IsNullOrWhiteSpace(endpointValue) &&
            !string.IsNullOrWhiteSpace(Definition.EndpointSecretKey) &&
            secretResolver is not null)
        {
            endpointValue = secretResolver.ResolveOrDefaultAsync(
                    Definition.EndpointSecretKey,
                    config.Endpoint,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        return string.IsNullOrWhiteSpace(endpointValue)
            ? Definition.DefaultEndpoint
            : EnsureTrailingSlash(new Uri(endpointValue, UriKind.Absolute));
    }
}
