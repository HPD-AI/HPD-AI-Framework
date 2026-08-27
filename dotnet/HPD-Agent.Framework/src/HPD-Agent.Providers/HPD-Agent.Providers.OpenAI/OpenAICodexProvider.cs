#pragma warning disable OPENAI001 // The Codex backend intentionally uses the experimental Responses SDK surface.

using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using OpenAI;
using System.Text.Json;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// Declares the ChatGPT/Codex product backend independently from the public OpenAI API backend.
/// </summary>
/// <remarks>
/// The backend intentionally fails closed until the package contains a reviewed OpenAI OAuth
/// protocol profile and request transport. An OpenAI API key is never accepted for this backend.
/// </remarks>
[HpdProvider("openai", "OpenAI")]
[HpdProviderBackend(
    "codex",
    ProviderAuthenticationKind.OAuth,
    IsInteractive = true,
    SupportsRefresh = true,
    Families = [ProviderClientFamily.Chat])]
[HpdProviderFamily(ProviderClientFamily.Chat)]
internal sealed class OpenAICodexProvider :
    IProvider,
    IProviderClientFactory<IChatClient>,
    IProviderSecretAliasProvider
{
    private readonly Uri _responsesEndpoint;
    private readonly OpenAICodexModelPolicy _modelPolicy;

    /// <summary>Creates the backend for the default observed experimental endpoint.</summary>
    public OpenAICodexProvider() : this(
        new Uri("https://chatgpt.com/backend-api/codex/responses"),
        OpenAICodexModelPolicy.ObservedV1) { }

    /// <summary>Creates the backend for one exact experimental Responses endpoint.</summary>
    /// <param name="responsesEndpoint">The authority- and path-pinned endpoint.</param>
    internal OpenAICodexProvider(
        Uri responsesEndpoint,
        OpenAICodexModelPolicy? modelPolicy = null)
    {
        _responsesEndpoint = responsesEndpoint ?? throw new ArgumentNullException(nameof(responsesEndpoint));
        _modelPolicy = modelPolicy ?? OpenAICodexModelPolicy.ObservedV1;
    }

    /// <inheritdoc />
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } = [];

    /// <inheritdoc />
    public string ProviderKey => "openai";

    /// <inheritdoc />
    public string DisplayName => "OpenAI";

    /// <inheritdoc />
    public IProviderErrorHandler CreateErrorHandler() => new OpenAIErrorHandler();

    /// <inheritdoc />
    public ProviderMetadata GetMetadata() => new()
    {
        ProviderKey = ProviderKey,
        DisplayName = DisplayName,
        DocumentationUri = new Uri("https://developers.openai.com/codex/"),
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.Chat] = new()
            {
                Family = ProviderClientFamily.Chat,
                SupportedModels = _modelPolicy.SupportedModels.Order(StringComparer.Ordinal).ToArray(),
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsStreaming"] = true,
                    ["SupportsNonStreaming"] = false,
                    ["Experimental"] = true,
                    ["OfficialIntegrationContract"] = false,
                    ["ModelPolicyVersion"] = _modelPolicy.Version
                }
            }
        }
    };

    /// <inheritdoc />
    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.ModelName))
            return ProviderValidationResult.Failure("A Codex model is required.");
        return _modelPolicy.IsSupported(config.ModelName)
            ? ProviderValidationResult.Success()
            : ProviderValidationResult.Failure(
                $"Model '{config.ModelName}' is not supported by Codex model policy '{_modelPolicy.Version}'.");
    }

    ProviderClientCredentialBinding IProviderClientFactory<IChatClient>.ResolveCredentialBinding(
        ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.RequestTime;
    }

    ValueTask<ProviderClientConstruction<IChatClient>> IProviderClientFactory<IChatClient>.CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.CredentialBinding is not ProviderCredentialBindingContext.RequestTime requestTime)
            throw new InvalidOperationException("The experimental Codex backend requires request-time credentials.");
        var model = context.EffectiveConfig.ModelName;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("A Codex model is required.");

        var handler = new OpenAICodexRequestSigningHandler(requestTime, _responsesEndpoint)
        {
            InnerHandler = new HttpClientHandler { AllowAutoRedirect = false }
        };
        var httpClient = new HttpClient(handler, disposeHandler: true);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(_responsesEndpoint, "."),
            Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(httpClient)
        };
        var sdk = new OpenAIClient(new System.ClientModel.ApiKeyCredential("hpd-experimental-unused"), options);
        var client = sdk.GetResponsesClient().AsIChatClient(model);
        return ValueTask.FromResult(new ProviderClientConstruction<IChatClient>
        {
            Client = client,
            Owner = new CodexClientOwner(client, httpClient)
        });
    }

    private sealed class OpenAICodexRequestSigningHandler(
        ProviderCredentialBindingContext.RequestTime requestTime,
        Uri endpoint) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var original = request.RequestUri ?? throw new InvalidOperationException("The OpenAI SDK request URI is missing.");
            if (!original.AbsolutePath.EndsWith("/responses", StringComparison.Ordinal))
                throw new InvalidOperationException("The experimental Codex transport permits only the Responses operation.");
            if (request.Content is not null)
            {
                var body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("stream", out var stream) || stream.ValueKind != JsonValueKind.True)
                    throw new NotSupportedException(
                        "The experimental Codex endpoint requires streaming Responses requests. " +
                        "Use the streaming IChatClient operation.");
            }
            request.RequestUri = endpoint;
            request.Headers.Authorization = null;
            await using var lease = await requestTime.Source.AcquireAsync(requestTime.Plan, cancellationToken).ConfigureAwait(false);
            if (lease.Credential is not ProviderCredential.SignedRequest signed)
                throw new InvalidOperationException("The experimental Codex backend requires a signed-request credential.");
            await signed.Lease.Signer.SignAsync(request, cancellationToken).ConfigureAwait(false);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class CodexClientOwner(IChatClient client, HttpClient httpClient) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (client is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (client is IDisposable disposable)
                disposable.Dispose();
            httpClient.Dispose();
        }
    }
}
