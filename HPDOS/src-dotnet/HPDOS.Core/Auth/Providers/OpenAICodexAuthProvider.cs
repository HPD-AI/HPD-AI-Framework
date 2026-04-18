using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPDOS.Core.Auth.Providers;

/// <summary>
/// OpenAI provider for ChatGPT Plus/Pro subscriptions.
///
/// This provider uses OAuth to authenticate users with their ChatGPT subscription,
/// allowing access to the Codex API endpoints at https://chatgpt.com/backend-api/codex.
///
/// Authentication Methods:
/// 1. Browser OAuth: Opens browser window for user to authenticate via ChatGPT account
/// 2. Device Code: For headless/CLI environments (user enters code on openai.com)
/// 3. Manual API Key: For users with OpenAI API platform accounts
///
/// Special Handling for Organization Subscriptions:
/// - Extracts chatgpt_account_id from OAuth tokens
/// - Sets ChatGPT-Account-Id header for requests to map subscription benefits
/// - Automatically handles token refresh
///
/// This implementation is based on opencode's CodexAuthPlugin pattern.
/// </summary>
public class OpenAICodexAuthProvider : IAuthProvider, ILiveModelProvider
{
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private const string AuthBaseUrl = "https://auth.openai.com";
    private const string CodexApiBaseUrl = "https://chatgpt.com/backend-api/codex";

    private readonly HttpClient _httpClient;

    public OpenAICodexAuthProvider() : this(new HttpClient()) { }
    public OpenAICodexAuthProvider(HttpClient httpClient) => _httpClient = httpClient;

    public string ProviderId => "openai";
    public string DisplayName => "OpenAI (ChatGPT Plus/Pro)";
    public IReadOnlyList<string> EnvironmentVariables => ["OPENAI_API_KEY"];

    public IReadOnlyList<AuthMethod> Methods =>
    [
        new AuthMethod
        {
            Type = AuthType.OAuthBrowser,
            Label = "ChatGPT subscription (browser)",
            Description = "Opens browser for OAuth authentication",
            IsRecommended = true,
            StartFlow = StartBrowserFlowAsync
        },
        new AuthMethod
        {
            Type = AuthType.OAuthDeviceCode,
            Label = "ChatGPT subscription (device code)",
            Description = "Enter a code on openai.com — no browser popup needed",
            StartFlow = StartDeviceCodeFlowAsync
        },
        new AuthMethod
        {
            Type = AuthType.ApiKey,
            Label = "API key",
            Description = "Enter your OpenAI API key manually",
            StartFlow = _ => Task.FromResult<AuthFlowResult>(
                new AuthFlowResult.NeedsUserInput(
                    "Enter your OpenAI API key",
                    "API Key",
                    (input, _) => Task.FromResult<AuthFlowResult>(
                        new AuthFlowResult.Success(new ApiKeyEntry { Key = input.Trim() }))))
        }
    ];

    public IReadOnlyList<ModelInfo> GetModels() =>
        KnownModels.ByProvider.TryGetValue(ProviderId, out var models) ? models : [];

    private static List<ModelInfo>? _liveModels;
    private static DateTime _liveFetchedAt;
    private static readonly SemaphoreSlim _fetchLock = new(1, 1);

    public Task<IReadOnlyList<ModelInfo>> FetchModelsAsync(AuthEntry? entry, CancellationToken ct = default)
    {
        var apiKey = entry?.GetCredential();
        return string.IsNullOrEmpty(apiKey) ? Task.FromResult(GetModels()) : FetchModelsAsync(apiKey);
    }

    /// <summary>Fetches live models from OpenAI API, caches for 5 minutes, falls back to static list.</summary>
    public async Task<IReadOnlyList<ModelInfo>> FetchModelsAsync(string apiKey)
    {
        if (_liveModels != null && DateTime.UtcNow - _liveFetchedAt < TimeSpan.FromMinutes(5))
            return _liveModels;

        await _fetchLock.WaitAsync();
        try
        {
            if (_liveModels != null && DateTime.UtcNow - _liveFetchedAt < TimeSpan.FromMinutes(5))
                return _liveModels;

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            http.DefaultRequestHeaders.Add("User-Agent", "HPDOS");

            var response = await http.GetAsync("https://api.openai.com/v1/models");
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("data", out var dataEl))
                return GetModels();

            var staticRecommended = new HashSet<string>(
                KnownModels.ByProvider.TryGetValue("openai", out var staticList)
                    ? staticList.Where(m => m.IsRecommended).Select(m => m.Id)
                    : [],
                StringComparer.OrdinalIgnoreCase);

            // Exclude non-chat models (audio, image, embedding, moderation, legacy completion)
            var excludePrefixes = new[] { "dall-e", "tts-", "whisper-", "text-embedding", "text-moderation", "babbage-", "davinci-" };

            var models = new List<ModelInfo>();
            foreach (var item in dataEl.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;
                if (excludePrefixes.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

                var isRecommended = staticRecommended.Contains(id);
                models.Add(new ModelInfo(id, id, isRecommended));
            }

            // Recommended first, then newest-first by version number extracted from the ID
            _liveModels = [.. models.Where(m => m.IsRecommended), .. models.Where(m => !m.IsRecommended).OrderByDescending(ModelVersionKey)];
            _liveFetchedAt = DateTime.UtcNow;
            return _liveModels;
        }
        catch
        {
            return GetModels();
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <summary>
    /// Extracts a sortable version key from a model ID so newer models sort first.
    /// e.g. "gpt-5.3-codex" → (5, 3, 0), "gpt-4o" → (4, 0, 0), "o3-mini" → (3, 0, 0)
    /// </summary>
    private static (int major, int minor, int patch) ModelVersionKey(ModelInfo m)
    {
        var id = m.Id;
        // Find the first digit run in the id and parse up to 3 dot-separated version parts
        int i = 0;
        while (i < id.Length && !char.IsDigit(id[i])) i++;
        if (i >= id.Length) return (0, 0, 0);

        var parts = new int[3];
        for (int p = 0; p < 3 && i < id.Length; p++)
        {
            int start = i;
            while (i < id.Length && char.IsDigit(id[i])) i++;
            if (i > start) parts[p] = int.Parse(id[start..i]);
            if (i < id.Length && (id[i] == '.' || id[i] == '-')) i++;
            else break;
            // Don't parse the next part if it doesn't start with a digit (e.g. "-mini", "-codex")
            if (i < id.Length && !char.IsDigit(id[i])) break;
        }
        return (parts[0], parts[1], parts[2]);
    }

    public Task<AuthLoadResult> LoadAsync(AuthEntry entry)
    {
        var result = entry switch
        {
            OAuthEntry oauth => new AuthLoadResult
            {
                // For OAuth, use access token as bearer token (not traditional API key)
                ApiKey = oauth.AccessToken,
                BaseUrl = CodexApiBaseUrl,
                // Include ChatGPT-Account-Id header for organization subscriptions
                // This header is required when user has a ChatGPT Pro/Plus subscription
                CustomHeaders = BuildCustomHeaders(oauth),
                AccountId = oauth.AccountId
            },
            ApiKeyEntry apiKey => new AuthLoadResult { ApiKey = apiKey.Key },
            WellKnownEntry wellKnown => new AuthLoadResult { ApiKey = wellKnown.GetCredential() },
            _ => throw new ArgumentException($"Unsupported auth entry type: {entry.GetType().Name}")
        };
        return Task.FromResult(result);
    }

    /// <summary>
    /// Builds custom headers for ChatGPT OAuth requests.
    /// Includes ChatGPT-Account-Id header when available for organization subscriptions.
    /// </summary>
    private static Dictionary<string, string>? BuildCustomHeaders(OAuthEntry oauth)
    {
        if (oauth.AccountId == null)
            return null;

        return new Dictionary<string, string>
        {
            // Required header for ChatGPT Pro/Plus subscriptions
            // Maps the user's ChatGPT account to their subscription benefits
            ["ChatGPT-Account-Id"] = oauth.AccountId
        };
    }

    public async Task<AuthEntry?> RefreshIfNeededAsync(AuthEntry entry)
    {
        if (entry is not OAuthEntry oauth || !oauth.ExpiresWithin(TimeSpan.FromMinutes(5))) return null;
        try
        {
            var tokenResponse = await _httpClient.RefreshTokenAsync($"{AuthBaseUrl}/oauth/token", oauth.RefreshToken, ClientId);
            if (string.IsNullOrEmpty(tokenResponse.AccessToken)) return null;
            return new OAuthEntry
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken ?? oauth.RefreshToken,
                ExpiresAtUnixMs = tokenResponse.GetExpiresAtUnixMs(),
                AccountId = ExtractAccountId(tokenResponse.AccessToken) ?? oauth.AccountId
            };
        }
        catch { return null; }
    }

    public Task<bool> ValidateAsync(AuthEntry entry)
    {
        if (entry is OAuthEntry oauth && oauth.IsExpired) return Task.FromResult(false);
        return Task.FromResult(true);
    }

    private async Task<AuthFlowResult> StartBrowserFlowAsync(CancellationToken cancellationToken)
    {
        var state = OAuthHelpers.GenerateRandomString();
        var codeVerifier = OAuthHelpers.GenerateCodeVerifier();
        var codeChallenge = OAuthHelpers.GenerateCodeChallenge(codeVerifier);

        const int oauthPort = 1455;
        var port = OAuthCallbackServer.FindAvailablePort(oauthPort);
        if (port != oauthPort)
            return new AuthFlowResult.Failed($"Port {oauthPort} is in use. Please close any other OAuth flows and try again.");

        await using var callbackServer = new OAuthCallbackServer(port, state);
        var authUrl = OAuthHelpers.BuildUrl($"{AuthBaseUrl}/oauth/authorize", new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = callbackServer.CallbackUrl,
            // Request scopes for ChatGPT subscription access
            // openid: Identity information
            // profile: User profile (needed for account ID extraction)
            // email: Email address
            // offline_access: Refresh token for long-lived sessions
            ["scope"] = "openid profile email offline_access",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            // Request organization information (for ChatGPT Pro/Plus accounts)
            ["id_token_add_organizations"] = "true",
            // Simplified flow for CLI/headless usage
            ["codex_cli_simplified_flow"] = "true",
            ["state"] = state,
            ["originator"] = ""
        });

        if (!OAuthHelpers.OpenBrowser(authUrl))
            return new AuthFlowResult.Failed("Failed to open browser. Please try device code flow instead.");

        var callbackResult = await callbackServer.WaitForCallbackAsync(cancellationToken);
        return callbackResult switch
        {
            OAuthCallbackResult.Success success => await ExchangeCodeAsync(success.Code, callbackServer.CallbackUrl, codeVerifier, cancellationToken),
            OAuthCallbackResult.Cancelled => new AuthFlowResult.Cancelled(),
            OAuthCallbackResult.Timeout => new AuthFlowResult.Failed("Authentication timed out. Please try again."),
            OAuthCallbackResult.Error error => new AuthFlowResult.Failed(error.Message, error.Exception),
            _ => new AuthFlowResult.Failed("Unexpected callback result")
        };
    }

    private async Task<AuthFlowResult> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        try
        {
            var tokenResponse = await _httpClient.ExchangeCodeForTokensAsync($"{AuthBaseUrl}/oauth/token", code, ClientId, redirectUri, codeVerifier, cancellationToken);
            if (string.IsNullOrEmpty(tokenResponse.AccessToken) || string.IsNullOrEmpty(tokenResponse.RefreshToken))
                return new AuthFlowResult.Failed("Invalid token response from OpenAI");
            return new AuthFlowResult.Success(new OAuthEntry
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAtUnixMs = tokenResponse.GetExpiresAtUnixMs(),
                AccountId = ExtractAccountId(tokenResponse.AccessToken)
            });
        }
        catch (Exception ex) { return new AuthFlowResult.Failed($"Failed to exchange code: {ex.Message}", ex); }
    }

    private async Task<AuthFlowResult> StartDeviceCodeFlowAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deviceCodeResponse = await RequestDeviceCodeAsync(cancellationToken);
            if (deviceCodeResponse == null) return new AuthFlowResult.Failed("Failed to get device code from OpenAI");
            return new AuthFlowResult.PendingUserAction(
                $"Enter code: {deviceCodeResponse.UserCode}",
                "https://auth.openai.com/codex/device",
                deviceCodeResponse.UserCode,
                async ct => await PollForDeviceTokenAsync(deviceCodeResponse.DeviceAuthId, ct));
        }
        catch (Exception ex) { return new AuthFlowResult.Failed($"Failed to start device code flow: {ex.Message}", ex); }
    }

    private async Task<OpenAIDeviceCodeResponse?> RequestDeviceCodeAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync(
            $"{AuthBaseUrl}/api/accounts/deviceauth/usercode",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = ClientId }),
            cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync(OpenAIJsonContext.Default.OpenAIDeviceCodeResponse, cancellationToken: cancellationToken)
            : null;
    }

    private async Task<AuthFlowResult> PollForDeviceTokenAsync(string deviceAuthId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (cancellationToken.IsCancellationRequested) return new AuthFlowResult.Cancelled();
            try
            {
                var response = await _httpClient.PostAsync(
                    $"{AuthBaseUrl}/api/accounts/deviceauth/token",
                    new FormUrlEncodedContent(new Dictionary<string, string> { ["device_auth_id"] = deviceAuthId }),
                    cancellationToken);
                var tokenResult = JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(cancellationToken), OpenAIJsonContext.Default.OpenAIDeviceTokenResponse);
                if (tokenResult?.Status == "complete" && !string.IsNullOrEmpty(tokenResult.Code))
                    return await ExchangeDeviceCodeAsync(tokenResult!.Code, cancellationToken);
                if (tokenResult?.Status == "expired") return new AuthFlowResult.Failed("Device code expired. Please try again.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            await Task.Delay(5000, cancellationToken);
        }
        return new AuthFlowResult.Failed("Device code flow timed out. Please try again.");
    }

    private async Task<AuthFlowResult> ExchangeDeviceCodeAsync(string code, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{AuthBaseUrl}/oauth/token",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = ClientId, ["code"] = code }),
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) return new AuthFlowResult.Failed($"Token exchange failed: {json}");
            var tokenResponse = JsonSerializer.Deserialize(json, OAuthJsonContext.Default.TokenResponse);
            if (string.IsNullOrEmpty(tokenResponse?.AccessToken) || string.IsNullOrEmpty(tokenResponse.RefreshToken))
                return new AuthFlowResult.Failed("Invalid token response from OpenAI");
            return new AuthFlowResult.Success(new OAuthEntry
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAtUnixMs = tokenResponse.GetExpiresAtUnixMs(),
                AccountId = ExtractAccountId(tokenResponse.AccessToken)
            });
        }
        catch (Exception ex) { return new AuthFlowResult.Failed($"Failed to exchange device code: {ex.Message}", ex); }
    }

    /// <summary>
    /// Extracts the ChatGPT account ID from OAuth tokens for organization subscriptions.
    ///
    /// The account ID is used to set the ChatGPT-Account-Id header in API requests,
    /// which maps the authenticated user to their ChatGPT Plus/Pro subscription benefits.
    ///
    /// JWT Claims are checked in order:
    /// 1. Top-level "chatgpt_account_id" claim
    /// 2. Nested "https://api.openai.com/auth" -> "chatgpt_account_id"
    /// 3. Fallback to "organizations" claim
    /// </summary>
    private static string? ExtractAccountId(string accessToken)
    {
        var claims = OAuthHelpers.ParseJwtClaims(accessToken);
        if (claims == null) return null;

        // Check top-level chatgpt_account_id claim
        var accountId = OAuthHelpers.GetJwtClaim(claims, "chatgpt_account_id");
        if (!string.IsNullOrEmpty(accountId)) return accountId;

        // Check nested chatgpt_account_id in https://api.openai.com/auth
        if (claims.TryGetValue("https://api.openai.com/auth", out var authClaim) &&
            authClaim.ValueKind == System.Text.Json.JsonValueKind.Object &&
            authClaim.TryGetProperty("chatgpt_account_id", out var nested) &&
            nested.ValueKind == System.Text.Json.JsonValueKind.String)
            return nested.GetString();

        // Fallback to organizations claim for org subscriptions
        return OAuthHelpers.GetJwtClaim(claims, "organizations");
    }

}

internal class OpenAIDeviceCodeResponse
{
    [JsonPropertyName("device_auth_id")] public string DeviceAuthId { get; set; } = "";
    [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
}

internal class OpenAIDeviceTokenResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("code")] public string? Code { get; set; }
}

[JsonSerializable(typeof(OpenAIDeviceCodeResponse))]
[JsonSerializable(typeof(OpenAIDeviceTokenResponse))]
internal partial class OpenAIJsonContext : JsonSerializerContext { }
