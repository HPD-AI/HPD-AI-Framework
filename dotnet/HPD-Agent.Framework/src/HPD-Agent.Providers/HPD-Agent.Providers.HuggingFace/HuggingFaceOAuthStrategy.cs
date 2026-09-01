using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.HuggingFace;

/// <summary>
/// Implements Hugging Face's documented public-client authorization-code flow with PKCE for
/// the <c>inference-api</c> scope.
/// </summary>
public sealed class HuggingFaceOAuthStrategy : IProviderAuthenticationStrategy
{
    private static readonly Uri Issuer = new("https://huggingface.co");
    private static readonly Uri AuthorizationEndpoint = new("https://huggingface.co/oauth/authorize");
    private static readonly Uri TokenEndpoint = new("https://huggingface.co/oauth/token");
    private readonly string _clientId;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the strategy for one registered Hugging Face public OAuth client.</summary>
    /// <param name="clientId">The registered public OAuth client identifier.</param>
    /// <param name="httpClient">The host-owned HTTP client.</param>
    /// <param name="timeProvider">The expiry time authority.</param>
    public HuggingFaceOAuthStrategy(string clientId, HttpClient httpClient, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Any(char.IsControl))
            throw new ArgumentException("OAuth client ID must be nonblank and control-free.", nameof(clientId));
        _clientId = clientId;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ProviderAuthenticationStrategyDescriptor Descriptor { get; } = new()
    {
        StrategyId = new ProviderAuthenticationStrategyId("huggingface.oauth.pkce.v1"),
        ProviderKey = "huggingface",
        BackendKey = "platform",
        Kind = ProviderAuthenticationKind.OAuth,
        Flows = [ProviderAuthorizationFlow.AuthorizationCodePkce],
        SupportsRefresh = true,
        SupportsRevocation = false
    };

    /// <inheritdoc />
    public ValueTask<NormalizedProviderAuthorizationRequest> NormalizeAsync(
        ProviderCredentialRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Authentication is not OAuthProviderAuthentication oauth)
            throw new ArgumentException("Hugging Face OAuth requires OAuth authentication configuration.", nameof(request));
        var scopes = (oauth.Scopes ?? ["inference-api"])
            .Append("inference-api")
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var resource = request.Audience.Resource?.AbsoluteUri;
        var scopeIdentity = Hash(string.Join('\n', scopes));
        var grantIdentity = Hash($"{resource}|{request.Audience.Audience}|{scopeIdentity}");
        return ValueTask.FromResult(new NormalizedProviderAuthorizationRequest
        {
            Original = request,
            Identity = new ProviderAuthorizationIdentity
            {
                ProviderKey = "huggingface",
                BackendKey = "platform",
                AccountId = oauth.AccountId,
                AuthorizationServer = Issuer.AbsoluteUri.TrimEnd('/'),
                ClientIdentity = _clientId,
                TrustDomainId = request.AuthorizationScope.TrustDomainId,
                TenantId = request.AuthorizationScope.TenantId,
                PrincipalId = request.AuthorizationScope.PrincipalId,
                Resource = resource,
                Audience = request.Audience.Audience
            },
            Grant = new ProviderAuthorizationGrant
            {
                GrantIdentity = grantIdentity,
                RequestedScopes = scopes,
                RequestedScopeSetIdentity = scopeIdentity,
                Audience = new ProviderCredentialAudience
                {
                    Resource = request.Audience.Resource,
                    Audience = request.Audience.Audience,
                    Scopes = scopes
                }
            }
        });
    }

    /// <inheritdoc />
    public ValueTask<ProviderAuthorizationStart> BeginAuthorizationAsync(
        ProviderAuthorizationBeginContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context is not BrowserProviderAuthorizationBeginContext browser)
            throw new ArgumentException("Hugging Face supports only browser PKCE authorization.", nameof(context));
        if (browser.RedirectUri.Scheme is not ("https" or "http") ||
            (browser.RedirectUri.Scheme == "http" && !browser.RedirectUri.IsLoopback) ||
            !string.IsNullOrEmpty(browser.RedirectUri.UserInfo) ||
            !string.IsNullOrEmpty(browser.RedirectUri.Query) ||
            !string.IsNullOrEmpty(browser.RedirectUri.Fragment))
            throw new ArgumentException(
                "OAuth redirect URI must use HTTPS or loopback HTTP and cannot contain user info, query, or fragment components.",
                nameof(context));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var transactionId = Base64Url(RandomNumberGenerator.GetBytes(24));
        var expiry = context.TimeProvider.GetUtcNow().AddMinutes(10);
        var payload = new TransactionPayload
        {
            Verifier = verifier,
            State = state,
            RedirectUri = browser.RedirectUri.AbsoluteUri,
            Scopes = context.Request.Grant.RequestedScopes.ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, HuggingFaceOAuthJsonContext.Default.TransactionPayload);
        var challenge = new BrowserAuthorizationChallenge
        {
            TransactionId = transactionId,
            ProviderKey = "huggingface",
            BackendKey = "platform",
            AccountId = context.Request.Identity.AccountId,
            ExpiresAt = expiry,
            RedirectUri = browser.RedirectUri,
            AuthorizationUri = BuildAuthorizationUri(browser.RedirectUri, state, verifier, payload.Scopes)
        };
        return ValueTask.FromResult(new ProviderAuthorizationStart
        {
            Challenge = challenge,
            TransactionState = new ProviderAuthorizationTransactionState
            {
                TransactionId = transactionId,
                Identity = context.Request.Identity,
                StrategyId = Descriptor.StrategyId,
                Flow = ProviderAuthorizationFlow.AuthorizationCodePkce,
                ExpiresAt = expiry,
                ProviderState = new SensitiveBytes(bytes)
            }
        });
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
        if (!HasExactRedirectBinding(new Uri(payload.RedirectUri, UriKind.Absolute), callback))
            throw Protocol(transaction, "OAuthRedirectMismatch", "The OAuth callback redirect binding is invalid.");
        var parameters = ParseQuery(callback.Query, transaction);
        if (!parameters.TryGetValue("state", out var returnedState) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(payload.State), Encoding.UTF8.GetBytes(returnedState)))
            throw Protocol(transaction, "OAuthStateMismatch", "The OAuth callback state is invalid.");
        if (parameters.ContainsKey("error"))
            throw Protocol(transaction, "OAuthAuthorizationRejected", "Hugging Face rejected the authorization request.");
        if (!parameters.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw Protocol(transaction, "OAuthCodeMissing", "The OAuth callback did not contain an authorization code.");
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
        var code = ParseQuery(response.CallbackUri.Query, transaction)["code"];
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _clientId,
            ["code"] = code,
            ["redirect_uri"] = payload.RedirectUri,
            ["code_verifier"] = payload.Verifier
        }, transaction.Identity, cancellationToken).ConfigureAwait(false);
        return Session(token, transaction.Identity, payload.Scopes, _timeProvider.GetUtcNow());
    }

    /// <inheritdoc />
    public ValueTask<ProviderDeviceAuthorizationProgress> AdvanceDeviceAuthorizationAsync(
        ProviderAuthorizationTransactionState transaction,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<ProviderDeviceAuthorizationProgress>(
            new NotSupportedException("Hugging Face does not support device authorization."));

    /// <inheritdoc />
    public async ValueTask<ProviderAuthorizationRefreshResult> RefreshAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationSession current,
        CancellationToken cancellationToken = default)
    {
        var refreshToken = current.Secrets.RefreshToken ?? throw Failure(
            identity, ProviderAuthenticationFailureKind.InvalidGrant, "RefreshTokenMissing", "The OAuth session cannot be refreshed.");
        var token = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _clientId,
            ["refresh_token"] = refreshToken.Value.ToString()
        }, identity, cancellationToken).ConfigureAwait(false);
        var access = new SecretChars(token.AccessToken);
        var replacement = string.IsNullOrWhiteSpace(token.RefreshToken) ? null : new SecretChars(token.RefreshToken);
        return new ProviderAuthorizationRefreshResult
        {
            Secrets = new RefreshSecrets(access, replacement),
            TokenType = token.TokenType ?? "Bearer",
            ExpiresAt = token.ExpiresIn is { } seconds ? _timeProvider.GetUtcNow().AddSeconds(seconds) : null,
            GrantedScopes = ParseScopes(token.Scope, current.GrantedScopes),
            RefreshTokenDisposition = replacement is null
                ? ProviderRefreshTokenDisposition.RetainCurrent
                : ProviderRefreshTokenDisposition.Replace
        };
    }

    /// <inheritdoc />
    public ValueTask<ProviderCredential> CreateCredentialAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationSession current,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ProviderCredential>(
            new ProviderCredential.BearerToken(new SecretChars(current.Secrets.AccessToken.Value.Span)));
    }

    /// <inheritdoc />
    public ValueTask<ProviderRevocationResult> RevokeAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationSession current,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ProviderRevocationResult
        {
            Revoked = false,
            DiagnosticCode = "RevocationUnsupported"
        });

    private async ValueTask<TokenResponse> RequestTokenAsync(
        IReadOnlyDictionary<string, string> values,
        ProviderAuthorizationIdentity identity,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var kind = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                ? ProviderAuthenticationFailureKind.InvalidGrant
                : ProviderAuthenticationFailureKind.TemporarilyUnavailable;
            throw Failure(identity, kind, $"TokenEndpoint{(int)response.StatusCode}",
                "The Hugging Face token endpoint rejected the request.", kind == ProviderAuthenticationFailureKind.TemporarilyUnavailable);
        }
        var token = await JsonSerializer.DeserializeAsync(
            stream, HuggingFaceOAuthJsonContext.Default.TokenResponse, cancellationToken).ConfigureAwait(false);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            throw Failure(identity, ProviderAuthenticationFailureKind.ProtocolError,
                "TokenResponseInvalid", "The Hugging Face token response was invalid.");
        return token;
    }

    private static ProviderAuthorizationSession Session(
        TokenResponse token, ProviderAuthorizationIdentity identity, IReadOnlyList<string> requestedScopes,
        DateTimeOffset now)
    {
        var access = new SecretChars(token.AccessToken);
        var refresh = string.IsNullOrWhiteSpace(token.RefreshToken) ? null : new SecretChars(token.RefreshToken);
        return new ProviderAuthorizationSession
        {
            SchemaVersion = "huggingface.oauth.session.v1",
            Secrets = new SessionSecrets(access, refresh),
            TokenType = token.TokenType ?? "Bearer",
            ExpiresAt = token.ExpiresIn is { } seconds ? now.AddSeconds(seconds) : null,
            GrantedScopes = ParseScopes(token.Scope, requestedScopes),
            ClientId = identity.ClientIdentity,
            TokenEndpointAuthenticationMethod = "none",
            AuthorizationServer = identity.AuthorizationServer,
            Subject = identity.AccountId
        };
    }

    private Uri BuildAuthorizationUri(Uri redirect, string state, string verifier, IReadOnlyList<string> scopes)
    {
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _clientId,
            ["redirect_uri"] = redirect.AbsoluteUri,
            ["scope"] = string.Join(' ', scopes),
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };
        return new UriBuilder(AuthorizationEndpoint) { Query = string.Join('&', query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")) }.Uri;
    }

    private static TransactionPayload ReadPayload(ProviderAuthorizationTransactionState transaction) =>
        JsonSerializer.Deserialize(transaction.ProviderState.Value.Span, HuggingFaceOAuthJsonContext.Default.TransactionPayload)
        ?? throw Protocol(transaction, "TransactionPayloadInvalid", "The OAuth transaction payload is invalid.");

    private static BrowserAuthorizationResponse RequireBrowserResponse(ProviderAuthorizationResponse response) =>
        response as BrowserAuthorizationResponse ?? throw new ArgumentException("A browser callback response is required.", nameof(response));

    private static bool HasExactRedirectBinding(Uri expected, Uri callback) =>
        callback.IsAbsoluteUri &&
        string.IsNullOrEmpty(callback.UserInfo) &&
        string.IsNullOrEmpty(callback.Fragment) &&
        string.Equals(expected.Scheme, callback.Scheme, StringComparison.Ordinal) &&
        string.Equals(expected.IdnHost, callback.IdnHost, StringComparison.Ordinal) &&
        expected.Port == callback.Port &&
        string.Equals(expected.AbsolutePath, callback.AbsolutePath, StringComparison.Ordinal) &&
        string.Equals(expected.Query, string.Empty, StringComparison.Ordinal);

    private static Dictionary<string, string> ParseQuery(
        string query,
        ProviderAuthorizationTransactionState transaction)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = component.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? component : component[..separator]);
            var value = Uri.UnescapeDataString(separator < 0 ? string.Empty : component[(separator + 1)..].Replace('+', ' '));
            if (!values.TryAdd(key, value))
                throw Protocol(transaction, "OAuthDuplicateParameter", "The OAuth callback contains duplicate parameters.");
        }
        return values;
    }

    private static IReadOnlyList<string> ParseScopes(string? scopes, IReadOnlyList<string>? fallback) =>
        string.IsNullOrWhiteSpace(scopes)
            ? (fallback ?? []).ToArray()
            : scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static ProviderAuthenticationException Protocol(
        ProviderAuthorizationTransactionState transaction, string code, string message) =>
        Failure(transaction.Identity, ProviderAuthenticationFailureKind.ProtocolError, code, message);

    private static ProviderAuthenticationException Failure(
        ProviderAuthorizationIdentity identity,
        ProviderAuthenticationFailureKind kind,
        string code,
        string message,
        bool retryable = false) => new(
            kind, identity.ProviderKey, identity.BackendKey, ProviderClientFamily.Chat,
            Hash($"{identity.AccountId}|{identity.ClientIdentity}"), message,
            isRetryable: retryable, interactionCanResolve: kind == ProviderAuthenticationFailureKind.InvalidGrant,
            diagnosticCode: code);

    private static string Base64Url(ReadOnlySpan<byte> value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal sealed class TransactionPayload
    {
        public string Verifier { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string[] Scopes { get; set; } = [];
    }

    internal sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
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
        public SecretChars(ReadOnlySpan<char> value) => _value = value.ToArray();
        public ReadOnlyMemory<char> Value => _value ?? throw new ObjectDisposedException(nameof(SecretChars));
        public ValueTask DisposeAsync() { var value = Interlocked.Exchange(ref _value, null); if (value is not null) Array.Clear(value); return ValueTask.CompletedTask; }
    }

    private sealed class SessionSecrets(IProviderSecretBuffer access, IProviderSecretBuffer? refresh) : IProviderAuthorizationSecretSet
    {
        private IProviderSecretBuffer? _access = access, _refresh = refresh;
        public IProviderSecretBuffer AccessToken => _access ?? throw new ObjectDisposedException(nameof(SessionSecrets));
        public IProviderSecretBuffer? RefreshToken => _refresh;
        public IProviderSecretBuffer? ClientSecret => null;
        public async ValueTask DisposeAsync()
        {
            var accessValue = Interlocked.Exchange(ref _access, null);
            var refreshValue = Interlocked.Exchange(ref _refresh, null);
            if (accessValue is not null) await accessValue.DisposeAsync().ConfigureAwait(false);
            if (refreshValue is not null && !ReferenceEquals(refreshValue, accessValue)) await refreshValue.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class RefreshSecrets(IProviderSecretBuffer access, IProviderSecretBuffer? refresh) : IProviderRefreshSecretSet
    {
        private IProviderSecretBuffer? _access = access, _refresh = refresh;
        public IProviderSecretBuffer AccessToken => _access ?? throw new ObjectDisposedException(nameof(RefreshSecrets));
        public IProviderSecretBuffer? ReplacementRefreshToken => _refresh;
        public async ValueTask DisposeAsync()
        {
            var accessValue = Interlocked.Exchange(ref _access, null);
            var refreshValue = Interlocked.Exchange(ref _refresh, null);
            if (accessValue is not null) await accessValue.DisposeAsync().ConfigureAwait(false);
            if (refreshValue is not null && !ReferenceEquals(refreshValue, accessValue)) await refreshValue.DisposeAsync().ConfigureAwait(false);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HuggingFaceOAuthStrategy.TransactionPayload))]
[JsonSerializable(typeof(HuggingFaceOAuthStrategy.TokenResponse))]
internal partial class HuggingFaceOAuthJsonContext : JsonSerializerContext;
