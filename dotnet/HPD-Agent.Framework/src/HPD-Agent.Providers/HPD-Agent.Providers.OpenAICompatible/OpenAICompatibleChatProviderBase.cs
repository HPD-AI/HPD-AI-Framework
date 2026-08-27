using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.OpenAICompatible;

/// <summary>Base implementation for small OpenAI-compatible chat-completions providers.</summary>
public abstract class OpenAICompatibleChatProviderBase<TConfig> :
    IProvider,
    IProviderClientFactory<IChatClient>,
    IProviderSecretAliasProvider
    where TConfig : OpenAICompatibleProviderConfig
{
    /// <summary>Gets immutable provider protocol metadata.</summary>
    protected abstract OpenAICompatibleProviderDefinition Definition { get; }

    /// <summary>Gets source-generated metadata for the provider configuration payload.</summary>
    protected abstract JsonTypeInfo<TConfig> ConfigurationTypeInfo { get; }

    /// <inheritdoc />
    public string ProviderKey => Definition.ProviderKey;

    /// <inheritdoc />
    public string DisplayName => Definition.DisplayName;

    /// <inheritdoc />
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases
    {
        get
        {
            var registrations = new List<ProviderSecretAliasRegistration>();
            if (!string.IsNullOrWhiteSpace(Definition.ApiKeySecretKey) && Definition.ApiKeyEnvironmentVariables.Length > 0)
                registrations.Add(new ProviderSecretAliasRegistration(
                    Definition.ApiKeySecretKey, Definition.ApiKeyEnvironmentVariables));
            if (!string.IsNullOrWhiteSpace(Definition.EndpointSecretKey) && Definition.EndpointEnvironmentVariables.Length > 0)
                registrations.Add(new ProviderSecretAliasRegistration(
                    Definition.EndpointSecretKey, Definition.EndpointEnvironmentVariables));
            return registrations;
        }
    }

    /// <inheritdoc />
    public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Provider packages use generated AOT-compatible payload contracts.")]
    public ValueTask<ProviderClientConstruction<IChatClient>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var config = context.EffectiveConfig;
        var providerConfig = config.ProviderConfiguration.CanonicalPayload.IsEmpty
            ? null
            : JsonSerializer.Deserialize(config.ProviderConfiguration.CanonicalPayload.AsSpan(), ConfigurationTypeInfo);
        if (string.IsNullOrWhiteSpace(config.ModelName))
            throw new InvalidOperationException($"For {DisplayName}, the model name must be configured.");
        var apiKey = GetApiKey(context.CredentialBinding, Definition.RequiresApiKey);
        var endpoint = config.Endpoint is null ? Definition.DefaultEndpoint : EnsureTrailingSlash(config.Endpoint);
        var httpClient = context.Services.HttpClientFactory.CreateClient($"hpd-provider-{ProviderKey}");
        httpClient.BaseAddress = endpoint;
        ConfigureHttpClient(httpClient, config, apiKey);
        var inner = CreateOpenAICompatibleChatClient(httpClient, config, endpoint);
        var client = WrapChatClient(inner, config, endpoint, providerConfig);
        return ValueTask.FromResult(new ProviderClientConstruction<IChatClient>
        {
            Client = client,
            Owner = new OpenAICompatibleClientOwner(client, httpClient)
        });
    }

    /// <inheritdoc />
    public abstract IProviderErrorHandler CreateErrorHandler();

    /// <inheritdoc />
    public virtual ProviderMetadata GetMetadata() => new()
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

    /// <inheritdoc />
    public virtual ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<string>();
        if (config.Family != ProviderClientFamily.Chat)
            errors.Add($"{DisplayName} currently supports only the chat provider family.");
        if (string.IsNullOrWhiteSpace(config.ModelName))
            errors.Add($"Model name is required for {DisplayName}.");
        return errors.Count == 0
            ? ProviderValidationResult.Success()
            : ProviderValidationResult.Failure(errors.ToArray());
    }

    /// <summary>Creates the protocol client.</summary>
    protected virtual IChatClient CreateOpenAICompatibleChatClient(
        HttpClient httpClient,
        EffectiveProviderClientConfig config,
        Uri endpoint) => new OpenAICompatibleChatClient(httpClient, CreateChatClientOptions(config, endpoint));

    /// <summary>Creates protocol options from the immutable effective snapshot.</summary>
    protected virtual OpenAICompatibleChatClientOptions CreateChatClientOptions(
        EffectiveProviderClientConfig config,
        Uri endpoint) => new()
    {
        ProviderKey = ProviderKey,
        DisplayName = DisplayName,
        ProviderUri = Definition.ProviderUri ?? endpoint,
        DefaultModelId = config.ModelName,
        ChatCompletionsPath = Definition.ChatCompletionsPath,
        RequestProfile = Definition.RequestProfile
    };

    /// <summary>Wraps the protocol client with provider-specific operation behavior.</summary>
    protected virtual IChatClient WrapChatClient(
        IChatClient innerClient,
        EffectiveProviderClientConfig config,
        Uri endpoint,
        TConfig? providerConfig) => new OpenAICompatibleConfiguredChatClient<TConfig>(
            innerClient,
            ProviderKey,
            config.ModelName,
            Definition.ProviderUri ?? endpoint,
            providerConfig);

    /// <summary>Applies authentication and safe configured headers.</summary>
    protected virtual void ConfigureHttpClient(
        HttpClient httpClient,
        EffectiveProviderClientConfig config,
        string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        foreach (var header in config.CustomHeaders)
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
    }

    /// <summary>Normalizes a base endpoint for relative protocol paths.</summary>
    protected static Uri EnsureTrailingSlash(Uri endpoint) =>
        endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? endpoint
            : new Uri(endpoint.AbsoluteUri + "/", UriKind.Absolute);

    private static string? GetApiKey(ProviderCredentialBindingContext binding, bool required)
    {
        if (binding is not ProviderCredentialBindingContext.ConstructionTime construction)
            throw new InvalidOperationException("OpenAI-compatible clients require a construction-time credential.");
        if (construction.Lease.Credential is ProviderCredential.Anonymous && !required)
            return null;
        if (construction.Lease.Credential is not ProviderCredential.ApiKey apiKey)
            throw new InvalidOperationException("The provider requires an API-key credential.");
        return apiKey.Value.Value.ToString();
    }

    private sealed class OpenAICompatibleClientOwner(IChatClient client, HttpClient httpClient) : IAsyncDisposable
    {
        private IChatClient? _client = client;
        private HttpClient? _httpClient = httpClient;

        public async ValueTask DisposeAsync()
        {
            var currentClient = Interlocked.Exchange(ref _client, null);
            var currentHttp = Interlocked.Exchange(ref _httpClient, null);
            try
            {
                if (currentClient is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else
                    currentClient?.Dispose();
            }
            finally
            {
                currentHttp?.Dispose();
            }
        }
    }
}
