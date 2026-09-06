#pragma warning disable OPENAI001 // The Codex backend intentionally uses the experimental Responses SDK surface.

using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using OpenAI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// Declares the ChatGPT/Codex product backend independently from the public OpenAI API backend.
/// </summary>
/// <remarks>
/// Uses HPD's experimental OAuth profile with request-time signing. Both chat operations use
/// streaming Responses; an OpenAI API key is never accepted for this backend.
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

    /// <summary>Creates the backend for the default observed experimental endpoint.</summary>
    public OpenAICodexProvider() : this(
        new Uri("https://chatgpt.com/backend-api/codex/responses")) { }

    /// <summary>Creates the backend for one exact experimental Responses endpoint.</summary>
    /// <param name="responsesEndpoint">The authority- and path-pinned endpoint.</param>
    internal OpenAICodexProvider(
        Uri responsesEndpoint)
    {
        _responsesEndpoint = responsesEndpoint ?? throw new ArgumentNullException(nameof(responsesEndpoint));
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
                SupportedModels = [],
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsStreaming"] = true,
                    ["SupportsNonStreaming"] = true,
                    ["RequiresStreamingTransport"] = true,
                    ["Experimental"] = true,
                    ["OfficialIntegrationContract"] = false,
                    ["ModelDiscovery"] = "account-scoped"
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
        var policy = ReadModelPolicy(config);
        return policy is null || string.Equals(policy.ModelId, config.ModelName, StringComparison.Ordinal)
            ? ProviderValidationResult.Success()
            : ProviderValidationResult.Failure("The Codex model policy belongs to a different model.");
    }

    ProviderClientCredentialBinding IProviderClientFactory<IChatClient>.ResolveCredentialBinding(
        ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.RequestTime;
    }

    ProviderCredentialAudience IProviderClientFactory<IChatClient>.ResolveCredentialAudience(
        ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new ProviderCredentialAudience
        {
            Resource = _responsesEndpoint,
            Audience = _responsesEndpoint.AbsoluteUri,
            Scopes = descriptor.EffectiveConfig.Provider.Authentication.Scopes
        };
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
            RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0),
            Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(httpClient)
        };
        var sdk = new OpenAIClient(new System.ClientModel.ApiKeyCredential("hpd-experimental-unused"), options);
        var responsesClient = sdk.GetResponsesClient().AsIChatClient(model);
        var client = new OpenAICodexChatClient(responsesClient, model, ReadModelPolicy(context.EffectiveConfig));
        return ValueTask.FromResult(new ProviderClientConstruction<IChatClient>
        {
            Client = client,
            Owner = new CodexClientOwner(client, httpClient)
        });
    }

    private static OpenAICodexModelPolicy? ReadModelPolicy(EffectiveProviderClientConfig config) =>
        config.ProviderConfiguration.CanonicalPayload.IsEmpty ? null :
            JsonSerializer.Deserialize(config.ProviderConfiguration.CanonicalPayload.AsSpan(),
                OpenAIJsonContext.Default.OpenAIProviderConfig)?.CodexModelPolicy;

    internal sealed class OpenAICodexRequestSigningHandler(
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
                var contentHeaders = request.Content.Headers
                    .Where(static header => !string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    .Select(static header => (header.Key, Values: header.Value.ToArray()))
                    .ToArray();
                var body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("stream", out var stream) || stream.ValueKind != JsonValueKind.True)
                    throw new NotSupportedException(
                        "The Codex transport requires stream=true for both IChatClient operations.");
                var normalized = JsonNode.Parse(body) as JsonObject
                    ?? throw new JsonException("The Codex Responses request must be a JSON object.");
                normalized["store"] = false;
                request.Content = new StringContent(
                    normalized.ToJsonString(),
                    System.Text.Encoding.UTF8,
                    "application/json");
                foreach (var header in contentHeaders)
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Values);
            }
            request.RequestUri = endpoint;
            request.Headers.Authorization = null;
            // The Codex signer copies immutable header strings before sending. Nothing in the
            // response body refers to the signer or its zeroed token buffer (covered by tests).
            // Each new request acquires a new lease; the SDK cannot replay this request.
            await using var lease = await requestTime.Source.AcquireAsync(requestTime.Plan, cancellationToken).ConfigureAwait(false);
            if (lease.Credential is not ProviderCredential.SignedRequest signed)
                throw new InvalidOperationException("The experimental Codex backend requires a signed-request credential.");
            await signed.Lease.Signer.SignAsync(request, cancellationToken).ConfigureAwait(false);
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && response.Content is not null)
            {
                var contentHeaders = response.Content.Headers
                    .Select(header => (header.Key, Values: header.Value.ToArray()))
                    .ToArray();
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                response.Content = new ByteArrayContent(body);
                foreach (var header in contentHeaders)
                    response.Content.Headers.TryAddWithoutValidation(header.Key, header.Values);
                try
                {
                    using var error = JsonDocument.Parse(body);
                    if (error.RootElement.TryGetProperty("error", out var value))
                    {
                        var code = value.TryGetProperty("code", out var codeValue)
                            ? codeValue.GetString()
                            : null;
                        var message = value.TryGetProperty("message", out var messageValue)
                            ? messageValue.GetString()
                            : null;
                        Console.Error.WriteLine(
                            $"Codex request rejected ({(int)response.StatusCode}, {code ?? "unknown"}): {message ?? "No message."}");
                    }
                    else
                    {
                        var code = error.RootElement.TryGetProperty("code", out var codeValue)
                            ? codeValue.ToString()
                            : "unknown";
                        var message = error.RootElement.TryGetProperty("detail", out var detailValue)
                            ? detailValue.ToString()
                            : error.RootElement.TryGetProperty("message", out var messageValue)
                                ? messageValue.ToString()
                                : "No message.";
                        Console.Error.WriteLine(
                            $"Codex request rejected ({(int)response.StatusCode}, {code}): {message}");
                    }
                }
                catch (JsonException)
                {
                    Console.Error.WriteLine(
                        $"Codex request rejected ({(int)response.StatusCode}) with a non-JSON response.");
                }
            }
            return response;
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
