using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>Configures HPD's experimental, informally discovered ChatGPT/Codex OAuth profile.</summary>
/// <remarks>
/// This profile is not an official OpenAI developer integration contract. Its defaults reproduce
/// publicly observable interoperable behavior and may stop working without notice.
/// </remarks>
public sealed class OpenAICodexExperimentalOptions
{
    /// <summary>Gets or sets the observed public-client identifier.</summary>
    public string ClientId { get; set; } = "app_EMoamEEZ73f0CkXaXp7hrann";

    /// <summary>Gets or sets the authorization-server origin.</summary>
    public Uri Issuer { get; set; } = new("https://auth.openai.com");

    /// <summary>Gets or sets the exact Codex Responses endpoint authorized for bearer credentials.</summary>
    public Uri ResponsesEndpoint { get; set; } = new("https://chatgpt.com/backend-api/codex/responses");

    /// <summary>Gets or sets the exact Codex model-discovery endpoint authorized for bearer credentials.</summary>
    public Uri ModelsEndpoint { get; set; } = new("https://chatgpt.com/backend-api/codex/models");

    /// <summary>Gets or sets the identifying originator sent during authorization and inference.</summary>
    public string Originator { get; set; } = "hpd-agent";

    /// <summary>Gets or sets the bounded browser transaction lifetime.</summary>
    public TimeSpan BrowserAuthorizationLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets the maximum accepted token or device response body size.</summary>
    public int MaximumResponseBytes { get; set; } = 256 * 1024;
}

/// <summary>
/// Implements the experimental ChatGPT/Codex browser and headless authorization protocols using
/// HPD-owned transaction, session, refresh, and request-signing infrastructure.
/// </summary>
public sealed class OpenAICodexExperimentalOAuthStrategy : IProviderAuthenticationStrategy
{
    private static readonly string[] DefaultScopes = ["email", "offline_access", "openid", "profile"];
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly OpenAICodexExperimentalOptions _options;
    private readonly Uri _authorizeEndpoint;
    private readonly Uri _tokenEndpoint;
    private readonly Uri _deviceBeginEndpoint;
    private readonly Uri _devicePollEndpoint;
    private readonly Uri _deviceVerificationEndpoint;
    private readonly Uri _deviceRedirectUri;

    /// <summary>Creates the experimental profile with host-owned HTTP and time dependencies.</summary>
    /// <param name="httpClient">The client used only for the pinned authorization-server endpoints.</param>
    /// <param name="options">Experimental protocol settings.</param>
    /// <param name="timeProvider">The token and transaction time authority.</param>
    public OpenAICodexExperimentalOAuthStrategy(
        HttpClient httpClient,
        OpenAICodexExperimentalOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new OpenAICodexExperimentalOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        ValidateOptions(_options);
        var issuer = _options.Issuer.AbsoluteUri.TrimEnd('/') + "/";
        _authorizeEndpoint = new Uri(new Uri(issuer), "oauth/authorize");
        _tokenEndpoint = new Uri(new Uri(issuer), "oauth/token");
        _deviceBeginEndpoint = new Uri(new Uri(issuer), "api/accounts/deviceauth/usercode");
        _devicePollEndpoint = new Uri(new Uri(issuer), "api/accounts/deviceauth/token");
        _deviceVerificationEndpoint = new Uri(new Uri(issuer), "codex/device");
        _deviceRedirectUri = new Uri(new Uri(issuer), "deviceauth/callback");
    }

    /// <inheritdoc />
    public ProviderAuthenticationStrategyDescriptor Descriptor { get; } = new()
    {
        StrategyId = new("openai.codex.experimental.oauth.v1"),
        ProviderKey = "openai",
        BackendKey = "codex",
        Kind = ProviderAuthenticationKind.OAuth,
        Flows = [ProviderAuthorizationFlow.AuthorizationCodePkce, ProviderAuthorizationFlow.DeviceAuthorization],
        SupportsRefresh = true,
        SupportsRevocation = false
    };

    /// <inheritdoc />
    public ValueTask<NormalizedProviderAuthorizationRequest> NormalizeAsync(
        ProviderCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Authentication is not OAuthProviderAuthentication oauth)
            throw new ArgumentException("The OpenAI Codex backend requires OAuth authentication.", nameof(request));
        var scopes = (oauth.Scopes ?? DefaultScopes).Concat(DefaultScopes)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var resource = _options.ResponsesEndpoint.AbsoluteUri;
        var scopeIdentity = Hash(string.Join('\n', scopes));
        return ValueTask.FromResult(new NormalizedProviderAuthorizationRequest
        {
            Original = request,
            Identity = new ProviderAuthorizationIdentity
            {
                ProviderKey = "openai",
                BackendKey = "codex",
                AccountId = oauth.AccountId,
                AuthorizationServer = _options.Issuer.AbsoluteUri.TrimEnd('/'),
                ClientIdentity = _options.ClientId,
                TrustDomainId = request.AuthorizationScope.TrustDomainId,
                TenantId = request.AuthorizationScope.TenantId,
                PrincipalId = request.AuthorizationScope.PrincipalId,
                Resource = resource,
                Audience = request.Audience.Audience
            },
            Grant = new ProviderAuthorizationGrant
            {
                GrantIdentity = Hash($"{resource}|{request.Audience.Audience}|{scopeIdentity}"),
                RequestedScopes = scopes,
                RequestedScopeSetIdentity = scopeIdentity,
                Audience = new ProviderCredentialAudience
                {
                    Resource = _options.ResponsesEndpoint,
                    Audience = request.Audience.Audience,
                    Scopes = scopes
                }
            }
        });
    }

    /// <inheritdoc />
    public async ValueTask<ProviderAuthorizationStart> BeginAuthorizationAsync(
        ProviderAuthorizationBeginContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return context switch
        {
            BrowserProviderAuthorizationBeginContext browser => BeginBrowser(browser),
            DeviceProviderAuthorizationBeginContext device => await BeginDeviceAsync(device, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException("The authorization flow is not supported.", nameof(context))
        };
    }

    /// <inheritdoc />
    public ValueTask ValidateBrowserAuthorizationResponseAsync(
        ProviderAuthorizationTransactionState transaction,
        BrowserAuthorizationResponse response,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = ReadPayload(transaction);
        var callback = response.CallbackUri;
        if (!HasExactRedirectBinding(new Uri(payload.RedirectUri!, UriKind.Absolute), callback))
            throw Protocol(transaction.Identity, "OAuthRedirectMismatch", "The OAuth callback redirect binding is invalid.");
        var parameters = ParseQuery(callback.Query, transaction.Identity);
        if (!parameters.TryGetValue("state", out var returnedState) ||
            !FixedTimeEquals(payload.State!, returnedState))
            throw Protocol(transaction.Identity, "OAuthStateMismatch", "The OAuth callback state is invalid.");
        if (parameters.ContainsKey("error"))
            throw Protocol(transaction.Identity, "OAuthAuthorizationRejected", "OpenAI rejected the authorization request.");
        if (!parameters.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw Protocol(transaction.Identity, "OAuthCodeMissing", "The OAuth callback did not contain an authorization code.");
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<ProviderAuthorizationSession> CompleteBrowserAuthorizationAsync(
        ProviderAuthorizationTransactionState transaction,
        BrowserAuthorizationResponse response,
        CancellationToken cancellationToken = default)
    {
        await ValidateBrowserAuthorizationResponseAsync(transaction, response, cancellationToken).ConfigureAwait(false);
        var payload = ReadPayload(transaction);
        var code = ParseQuery(response.CallbackUri.Query, transaction.Identity)["code"];
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = payload.RedirectUri!,
            ["client_id"] = _options.ClientId,
            ["code_verifier"] = payload.Verifier!
        }, transaction.Identity, cancellationToken).ConfigureAwait(false);
        return CreateSession(token, transaction.Identity, transaction.Identity.AccountId, _timeProvider.GetUtcNow());
    }

    /// <inheritdoc />
    public async ValueTask<ProviderDeviceAuthorizationProgress> AdvanceDeviceAuthorizationAsync(
        ProviderAuthorizationTransactionState transaction,
        CancellationToken cancellationToken = default)
    {
        var payload = ReadPayload(transaction);
        using var response = await SendJsonAsync(_devicePollEndpoint, new DevicePollRequest
        {
            DeviceAuthId = payload.DeviceAuthId,
            UserCode = payload.UserCode
        }, OpenAICodexExperimentalJsonContext.Default.DevicePollRequest, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            var next = _timeProvider.GetUtcNow().AddSeconds(Math.Max(payload.PollIntervalSeconds, 1));
            return new ProviderDeviceAuthorizationProgress.Pending
            {
                Transaction = CloneTransaction(transaction, payload, next)
            };
        }
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode >= 500)
            {
                var next = _timeProvider.GetUtcNow().AddSeconds(Math.Max(payload.PollIntervalSeconds, 1));
                return new ProviderDeviceAuthorizationProgress.Pending
                {
                    Transaction = CloneTransaction(transaction, payload, next),
                    DiagnosticCode = $"DevicePoll{(int)response.StatusCode}"
                };
            }
            return new ProviderDeviceAuthorizationProgress.Terminal
            {
                Status = ProviderDeviceAuthorizationStatusKind.Denied,
                DiagnosticCode = $"DevicePoll{(int)response.StatusCode}"
            };
        }
        var authorization = await ReadJsonAsync(
            response, OpenAICodexExperimentalJsonContext.Default.DevicePollResponse, transaction.Identity, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(authorization.AuthorizationCode) || string.IsNullOrWhiteSpace(authorization.CodeVerifier))
            throw Protocol(transaction.Identity, "DevicePollResponseInvalid", "The device authorization response was invalid.");
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorization.AuthorizationCode,
            ["redirect_uri"] = _deviceRedirectUri.AbsoluteUri,
            ["client_id"] = _options.ClientId,
            ["code_verifier"] = authorization.CodeVerifier
        }, transaction.Identity, cancellationToken).ConfigureAwait(false);
        return new ProviderDeviceAuthorizationProgress.Authorized
        {
            Transaction = CloneTransaction(transaction, payload, transaction.NextPollAt),
            Session = CreateSession(token, transaction.Identity, transaction.Identity.AccountId, _timeProvider.GetUtcNow())
        };
    }

    /// <inheritdoc />
    public async ValueTask<ProviderAuthorizationRefreshResult> RefreshAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationSession current,
        CancellationToken cancellationToken = default)
    {
        var refresh = current.Secrets.RefreshToken ?? throw Failure(
            identity, ProviderAuthenticationFailureKind.InvalidGrant, "RefreshTokenMissing", "The OAuth session cannot be refreshed.");
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refresh.Value.ToString(),
            ["client_id"] = _options.ClientId
        }, identity, cancellationToken).ConfigureAwait(false);
        var accountId = ExtractAccountId(token) ?? current.ProviderState?.GetValueOrDefault("chatgpt_account_id");
        var previousAccountId = current.ProviderState?.GetValueOrDefault("chatgpt_account_id");
        if (previousAccountId is not null && accountId is not null && !FixedTimeEquals(previousAccountId, accountId))
            throw Protocol(identity, "RefreshAccountChanged", "The refreshed credential belongs to a different ChatGPT account.");
        var replacement = string.IsNullOrWhiteSpace(token.RefreshToken) ? null : new SecretChars(token.RefreshToken);
        return new ProviderAuthorizationRefreshResult
        {
            Secrets = new RefreshSecrets(new SecretChars(token.AccessToken), replacement),
            TokenType = token.TokenType ?? "Bearer",
            ExpiresAt = _timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn ?? 3600),
            GrantedScopes = current.GrantedScopes,
            RefreshTokenDisposition = replacement is null ? ProviderRefreshTokenDisposition.RetainCurrent : ProviderRefreshTokenDisposition.Replace,
            ProviderState = accountId is null ? current.ProviderState : State(accountId)
        };
    }

    /// <inheritdoc />
    public ValueTask<ProviderCredential> CreateCredentialAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationSession current,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var accountId = current.ProviderState?.GetValueOrDefault("chatgpt_account_id");
        return ValueTask.FromResult<ProviderCredential>(new ProviderCredential.SignedRequest(
            new SignerLease(new CodexSigner(
                current.Secrets.AccessToken.Value.Span,
                accountId,
                [_options.ResponsesEndpoint, _options.ModelsEndpoint],
                _options.Originator))));
    }

    /// <inheritdoc />
    public ValueTask<ProviderRevocationResult> RevokeAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationSession current,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ProviderRevocationResult { Revoked = false, DiagnosticCode = "RevocationUnsupported" });

    private ProviderAuthorizationStart BeginBrowser(BrowserProviderAuthorizationBeginContext context)
    {
        ValidateRedirect(context.RedirectUri);
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var transactionId = Base64Url(RandomNumberGenerator.GetBytes(24));
        var expiry = context.TimeProvider.GetUtcNow().Add(_options.BrowserAuthorizationLifetime);
        var payload = new TransactionPayload
        {
            Verifier = verifier,
            State = state,
            RedirectUri = context.RedirectUri.AbsoluteUri
        };
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = context.RedirectUri.AbsoluteUri,
            ["scope"] = string.Join(' ', context.Request.Grant.RequestedScopes),
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
            ["id_token_add_organizations"] = "true",
            ["codex_cli_simplified_flow"] = "true",
            ["state"] = state,
            ["originator"] = _options.Originator
        };
        var authorizationUri = new UriBuilder(_authorizeEndpoint)
        {
            Query = string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
        }.Uri;
        return Start(context.Request.Identity, transactionId, expiry, ProviderAuthorizationFlow.AuthorizationCodePkce,
            payload, new BrowserAuthorizationChallenge
            {
                TransactionId = transactionId,
                ProviderKey = "openai",
                BackendKey = "codex",
                AccountId = context.Request.Identity.AccountId,
                ExpiresAt = expiry,
                AuthorizationUri = authorizationUri,
                RedirectUri = context.RedirectUri
            });
    }

    private async ValueTask<ProviderAuthorizationStart> BeginDeviceAsync(
        DeviceProviderAuthorizationBeginContext context,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(_deviceBeginEndpoint, new DeviceBeginRequest { ClientId = _options.ClientId },
            OpenAICodexExperimentalJsonContext.Default.DeviceBeginRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw Failure(context.Request.Identity, ProviderAuthenticationFailureKind.TemporarilyUnavailable,
                $"DeviceBegin{(int)response.StatusCode}", "OpenAI rejected device authorization initiation.", true);
        var device = await ReadJsonAsync(response, OpenAICodexExperimentalJsonContext.Default.DeviceBeginResponse,
            context.Request.Identity, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(device.DeviceAuthId) || string.IsNullOrWhiteSpace(device.UserCode))
            throw Protocol(context.Request.Identity, "DeviceBeginResponseInvalid", "The device authorization response was invalid.");
        var interval = Math.Max(int.TryParse(device.Interval, out var parsed) ? parsed : 5, 1) + 3;
        var transactionId = Base64Url(RandomNumberGenerator.GetBytes(24));
        var expiry = context.TimeProvider.GetUtcNow().AddMinutes(15);
        var payload = new TransactionPayload
        {
            DeviceAuthId = device.DeviceAuthId,
            UserCode = device.UserCode,
            PollIntervalSeconds = interval
        };
        return Start(context.Request.Identity, transactionId, expiry, ProviderAuthorizationFlow.DeviceAuthorization,
            payload, new DeviceAuthorizationChallenge
            {
                TransactionId = transactionId,
                ProviderKey = "openai",
                BackendKey = "codex",
                AccountId = context.Request.Identity.AccountId,
                ExpiresAt = expiry,
                VerificationUri = _deviceVerificationEndpoint,
                UserCode = device.UserCode
            }, context.TimeProvider.GetUtcNow().AddSeconds(interval));
    }

    private ProviderAuthorizationStart Start(
        ProviderAuthorizationIdentity identity, string transactionId, DateTimeOffset expiry,
        ProviderAuthorizationFlow flow, TransactionPayload payload, ProviderAuthorizationChallenge challenge,
        DateTimeOffset? nextPollAt = null) => new()
    {
        Challenge = challenge,
        TransactionState = new ProviderAuthorizationTransactionState
        {
            TransactionId = transactionId,
            Identity = identity,
            StrategyId = Descriptor.StrategyId,
            Flow = flow,
            ExpiresAt = expiry,
            NextPollAt = nextPollAt,
            ProviderState = Serialize(payload)
        }
    };

    private async ValueTask<TokenResponse> RequestTokenAsync(
        IReadOnlyDictionary<string, string> values, ProviderAuthorizationIdentity identity,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var kind = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                ? ProviderAuthenticationFailureKind.InvalidGrant : ProviderAuthenticationFailureKind.TemporarilyUnavailable;
            throw Failure(identity, kind, $"TokenEndpoint{(int)response.StatusCode}",
                "OpenAI rejected the OAuth token request.", kind == ProviderAuthenticationFailureKind.TemporarilyUnavailable);
        }
        var token = await ReadJsonAsync(response, OpenAICodexExperimentalJsonContext.Default.TokenResponse,
            identity, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw Protocol(identity, "TokenResponseInvalid", "The OAuth token response was invalid.");
        return token;
    }

    private async ValueTask<HttpResponseMessage> SendJsonAsync<T>(
        Uri endpoint, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.UserAgent.ParseAdd("HPD-Agent/experimental-codex-oauth");
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            request.Dispose();
        }
    }

    private async ValueTask<T> ReadJsonAsync<T>(
        HttpResponseMessage response, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        ProviderAuthorizationIdentity identity, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var length && length > _options.MaximumResponseBytes)
            throw Protocol(identity, "ResponseTooLarge", "The OAuth response exceeded the configured size limit.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length > _options.MaximumResponseBytes)
            throw Protocol(identity, "ResponseTooLarge", "The OAuth response exceeded the configured size limit.");
        try
        {
            return JsonSerializer.Deserialize(bytes, typeInfo)
                ?? throw Protocol(identity, "ResponseInvalid", "The OAuth response was empty or invalid.");
        }
        catch (JsonException)
        {
            throw new ProviderAuthenticationException(
                ProviderAuthenticationFailureKind.ProtocolError, identity.ProviderKey, identity.BackendKey,
                ProviderClientFamily.Chat, Hash(identity.AccountId), "The OAuth response was not valid JSON.",
                false, false, "ResponseJsonInvalid");
        }
    }

    private ProviderAuthorizationSession CreateSession(
        TokenResponse token, ProviderAuthorizationIdentity identity, string fallbackSubject, DateTimeOffset now)
    {
        var accountId = ExtractAccountId(token);
        return new ProviderAuthorizationSession
        {
            SchemaVersion = "openai.codex.experimental.oauth.session.v1",
            Secrets = new SessionSecrets(new SecretChars(token.AccessToken),
                string.IsNullOrWhiteSpace(token.RefreshToken) ? null : new SecretChars(token.RefreshToken)),
            TokenType = token.TokenType ?? "Bearer",
            ExpiresAt = now.AddSeconds(token.ExpiresIn ?? 3600),
            GrantedScopes = DefaultScopes,
            ClientId = _options.ClientId,
            TokenEndpointAuthenticationMethod = "none",
            AuthorizationServer = identity.AuthorizationServer,
            Subject = accountId ?? fallbackSubject,
            ProviderState = accountId is null ? null : State(accountId)
        };
    }

    private static IReadOnlyDictionary<string, string> State(string accountId) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["chatgpt_account_id"] = accountId };

    private static string? ExtractAccountId(TokenResponse token) =>
        ExtractAccountId(token.IdToken) ?? ExtractAccountId(token.AccessToken);

    private static string? ExtractAccountId(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;
        var parts = jwt.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            using var document = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var root = document.RootElement;
            if (root.TryGetProperty("chatgpt_account_id", out var direct)) return direct.GetString();
            if (root.TryGetProperty("https://api.openai.com/auth", out var auth) &&
                auth.TryGetProperty("chatgpt_account_id", out var nested)) return nested.GetString();
            if (root.TryGetProperty("organizations", out var organizations) && organizations.GetArrayLength() > 0 &&
                organizations[0].TryGetProperty("id", out var organization)) return organization.GetString();
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { }
        return null;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }

    private static ProviderAuthorizationTransactionState CloneTransaction(
        ProviderAuthorizationTransactionState transaction, TransactionPayload payload, DateTimeOffset? nextPollAt) => new()
    {
        TransactionId = transaction.TransactionId,
        Identity = transaction.Identity,
        StrategyId = transaction.StrategyId,
        Flow = transaction.Flow,
        ExpiresAt = transaction.ExpiresAt,
        NextPollAt = nextPollAt,
        ProviderState = Serialize(payload)
    };

    private static SensitiveBytes Serialize(TransactionPayload payload) => new(
        JsonSerializer.SerializeToUtf8Bytes(payload, OpenAICodexExperimentalJsonContext.Default.TransactionPayload));

    private static TransactionPayload ReadPayload(ProviderAuthorizationTransactionState transaction) =>
        JsonSerializer.Deserialize(transaction.ProviderState.Value.Span,
            OpenAICodexExperimentalJsonContext.Default.TransactionPayload)
        ?? throw Protocol(transaction.Identity, "TransactionPayloadInvalid", "The OAuth transaction payload is invalid.");

    private static Dictionary<string, string> ParseQuery(string query, ProviderAuthorizationIdentity identity)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = component.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? component : component[..separator]);
            var value = Uri.UnescapeDataString(separator < 0 ? string.Empty : component[(separator + 1)..].Replace('+', ' '));
            if (!values.TryAdd(key, value))
                throw Protocol(identity, "OAuthDuplicateParameter", "The OAuth callback contains duplicate parameters.");
        }
        return values;
    }

    private static void ValidateRedirect(Uri redirect)
    {
        ArgumentNullException.ThrowIfNull(redirect);
        if (!redirect.IsAbsoluteUri || (redirect.Scheme == "http" && !redirect.IsLoopback) ||
            redirect.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(redirect.UserInfo) ||
            !string.IsNullOrEmpty(redirect.Query) || !string.IsNullOrEmpty(redirect.Fragment))
            throw new ArgumentException("The callback must be HTTPS or loopback HTTP without user info, query, or fragment.", nameof(redirect));
    }

    private static void ValidateOptions(OpenAICodexExperimentalOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || options.ClientId.Any(char.IsControl))
            throw new ArgumentException("The experimental client ID must be nonblank and control-free.", nameof(options));
        if (!options.Issuer.IsAbsoluteUri || options.Issuer.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(options.Issuer.UserInfo) || !string.IsNullOrEmpty(options.Issuer.Query) ||
            !string.IsNullOrEmpty(options.Issuer.Fragment))
            throw new ArgumentException("The experimental issuer must be an HTTPS origin.", nameof(options));
        if (!options.ResponsesEndpoint.IsAbsoluteUri || options.ResponsesEndpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(options.ResponsesEndpoint.UserInfo) || !string.IsNullOrEmpty(options.ResponsesEndpoint.Query) ||
            !string.IsNullOrEmpty(options.ResponsesEndpoint.Fragment))
            throw new ArgumentException("The experimental Responses endpoint must be an exact HTTPS URI.", nameof(options));
        if (!options.ModelsEndpoint.IsAbsoluteUri || options.ModelsEndpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(options.ModelsEndpoint.UserInfo) || !string.IsNullOrEmpty(options.ModelsEndpoint.Query) ||
            !string.IsNullOrEmpty(options.ModelsEndpoint.Fragment))
            throw new ArgumentException("The experimental models endpoint must be an exact HTTPS URI.", nameof(options));
        if (options.MaximumResponseBytes is < 1024 or > 4 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "The response limit must be between 1 KiB and 4 MiB.");
    }

    private static bool HasExactRedirectBinding(Uri expected, Uri callback) => callback.IsAbsoluteUri &&
        string.IsNullOrEmpty(callback.UserInfo) && string.IsNullOrEmpty(callback.Fragment) &&
        string.Equals(expected.Scheme, callback.Scheme, StringComparison.Ordinal) &&
        string.Equals(expected.IdnHost, callback.IdnHost, StringComparison.Ordinal) && expected.Port == callback.Port &&
        string.Equals(expected.AbsolutePath, callback.AbsolutePath, StringComparison.Ordinal) && string.IsNullOrEmpty(expected.Query);

    private static bool FixedTimeEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static string Base64Url(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static ProviderAuthenticationException Protocol(ProviderAuthorizationIdentity identity, string code, string message) =>
        Failure(identity, ProviderAuthenticationFailureKind.ProtocolError, code, message);
    private static ProviderAuthenticationException Failure(ProviderAuthorizationIdentity identity,
        ProviderAuthenticationFailureKind kind, string code, string message, bool retryable = false) => new(
            kind, identity.ProviderKey, identity.BackendKey, ProviderClientFamily.Chat,
            Hash($"{identity.AccountId}|{identity.ClientIdentity}"), message, retryable,
            kind == ProviderAuthenticationFailureKind.InvalidGrant, code);

    internal sealed class TransactionPayload
    {
        public string? Verifier { get; set; }
        public string? State { get; set; }
        public string? RedirectUri { get; set; }
        public string? DeviceAuthId { get; set; }
        public string? UserCode { get; set; }
        public int PollIntervalSeconds { get; set; }
    }
    internal sealed class DeviceBeginRequest { [JsonPropertyName("client_id")] public string ClientId { get; set; } = string.Empty; }
    internal sealed class DeviceBeginResponse
    {
        [JsonPropertyName("device_auth_id")] public string DeviceAuthId { get; set; } = string.Empty;
        [JsonPropertyName("user_code")] public string UserCode { get; set; } = string.Empty;
        [JsonPropertyName("interval")] public string? Interval { get; set; }
    }
    internal sealed class DevicePollRequest
    {
        [JsonPropertyName("device_auth_id")] public string? DeviceAuthId { get; set; }
        [JsonPropertyName("user_code")] public string? UserCode { get; set; }
    }
    internal sealed class DevicePollResponse
    {
        [JsonPropertyName("authorization_code")] public string AuthorizationCode { get; set; } = string.Empty;
        [JsonPropertyName("code_verifier")] public string CodeVerifier { get; set; } = string.Empty;
    }
    internal sealed class TokenResponse
    {
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
    }

    private sealed class CodexSigner(ReadOnlySpan<char> token, string? accountId, IReadOnlyList<Uri> endpoints, string originator)
        : IProviderRequestSigner, IAsyncDisposable
    {
        private char[]? _token = token.ToArray();
        public ValueTask SignAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = _token ?? throw new ObjectDisposedException(nameof(CodexSigner));
            var uri = request.RequestUri ?? throw new InvalidOperationException("A request URI is required before signing.");
            if (uri.Scheme != Uri.UriSchemeHttps || !endpoints.Any(endpoint =>
                    string.Equals(uri.IdnHost, endpoint.IdnHost, StringComparison.OrdinalIgnoreCase)
                    && uri.Port == endpoint.Port
                    && string.Equals(uri.AbsolutePath, endpoint.AbsolutePath, StringComparison.Ordinal)))
                throw new InvalidOperationException("The experimental Codex credential cannot be sent to this authority or path.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new string(value));
            if (!string.IsNullOrWhiteSpace(accountId)) request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);
            request.Headers.TryAddWithoutValidation("originator", originator);
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() { var value = Interlocked.Exchange(ref _token, null); if (value is not null) Array.Clear(value); return ValueTask.CompletedTask; }
    }
    private sealed class SignerLease(CodexSigner signer) : IProviderRequestSignerLease
    {
        private CodexSigner? _signer = signer;
        public IProviderRequestSigner Signer => _signer ?? throw new ObjectDisposedException(nameof(SignerLease));
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _signer, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
    private sealed class SensitiveBytes(byte[] bytes) : IProviderSensitiveBuffer
    {
        private byte[]? _bytes = bytes;
        public ReadOnlyMemory<byte> Value => _bytes ?? throw new ObjectDisposedException(nameof(SensitiveBytes));
        public ValueTask DisposeAsync() { var value = Interlocked.Exchange(ref _bytes, null); if (value is not null) CryptographicOperations.ZeroMemory(value); return ValueTask.CompletedTask; }
    }
    private sealed class SecretChars : IProviderSecretBuffer
    {
        private char[]? _value;
        public SecretChars(string value) => _value = value.ToCharArray();
        public ReadOnlyMemory<char> Value => _value ?? throw new ObjectDisposedException(nameof(SecretChars));
        public ValueTask DisposeAsync() { var value = Interlocked.Exchange(ref _value, null); if (value is not null) Array.Clear(value); return ValueTask.CompletedTask; }
    }
    private sealed class SessionSecrets(IProviderSecretBuffer access, IProviderSecretBuffer? refresh) : IProviderAuthorizationSecretSet
    {
        private IProviderSecretBuffer? _access = access, _refresh = refresh;
        public IProviderSecretBuffer AccessToken => _access ?? throw new ObjectDisposedException(nameof(SessionSecrets));
        public IProviderSecretBuffer? RefreshToken => _refresh;
        public IProviderSecretBuffer? ClientSecret => null;
        public async ValueTask DisposeAsync() { var accessValue = Interlocked.Exchange(ref _access, null); var refreshValue = Interlocked.Exchange(ref _refresh, null); if (accessValue is not null) await accessValue.DisposeAsync().ConfigureAwait(false); if (refreshValue is not null) await refreshValue.DisposeAsync().ConfigureAwait(false); }
    }
    private sealed class RefreshSecrets(IProviderSecretBuffer access, IProviderSecretBuffer? refresh) : IProviderRefreshSecretSet
    {
        private IProviderSecretBuffer? _access = access, _refresh = refresh;
        public IProviderSecretBuffer AccessToken => _access ?? throw new ObjectDisposedException(nameof(RefreshSecrets));
        public IProviderSecretBuffer? ReplacementRefreshToken => _refresh;
        public async ValueTask DisposeAsync() { var accessValue = Interlocked.Exchange(ref _access, null); var refreshValue = Interlocked.Exchange(ref _refresh, null); if (accessValue is not null) await accessValue.DisposeAsync().ConfigureAwait(false); if (refreshValue is not null) await refreshValue.DisposeAsync().ConfigureAwait(false); }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OpenAICodexExperimentalOAuthStrategy.TransactionPayload))]
[JsonSerializable(typeof(OpenAICodexExperimentalOAuthStrategy.DeviceBeginRequest))]
[JsonSerializable(typeof(OpenAICodexExperimentalOAuthStrategy.DeviceBeginResponse))]
[JsonSerializable(typeof(OpenAICodexExperimentalOAuthStrategy.DevicePollRequest))]
[JsonSerializable(typeof(OpenAICodexExperimentalOAuthStrategy.DevicePollResponse))]
[JsonSerializable(typeof(OpenAICodexExperimentalOAuthStrategy.TokenResponse))]
internal partial class OpenAICodexExperimentalJsonContext : JsonSerializerContext;
