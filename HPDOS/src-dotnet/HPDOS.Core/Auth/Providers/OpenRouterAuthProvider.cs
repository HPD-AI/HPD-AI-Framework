using System.Net.Http.Json;
using System.Text.Json;

namespace HPDOS.Core.Auth.Providers;

public class OpenRouterAuthProvider : IAuthProvider, ILiveModelProvider
{
    private const string AuthBaseUrl = "https://openrouter.ai/auth";
    private const string ApiBaseUrl = "https://openrouter.ai/api/v1";
    private const int OAuthPort = 3000;

    public string ProviderId => "openrouter";
    public string DisplayName => "OpenRouter";
    public IReadOnlyList<string> EnvironmentVariables => ["OPENROUTER_API_KEY"];

    public IReadOnlyList<AuthMethod> Methods =>
    [
        new AuthMethod
        {
            Type = AuthType.OAuthBrowser,
            Label = "Browser login",
            Description = "Sign in with your OpenRouter account",
            IsRecommended = true,
            StartFlow = StartBrowserFlowAsync
        },
        new AuthMethod
        {
            Type = AuthType.ApiKey,
            Label = "API key",
            Description = "Enter your OpenRouter API key manually",
            StartFlow = _ => Task.FromResult<AuthFlowResult>(
                new AuthFlowResult.NeedsUserInput(
                    "Enter your OpenRouter API key",
                    "API Key",
                    (input, _) => Task.FromResult<AuthFlowResult>(
                        new AuthFlowResult.Success(new ApiKeyEntry { Key = input.Trim() }))))
        }
    ];

    public Task<AuthLoadResult> LoadAsync(AuthEntry entry)
    {
        var apiKey = entry switch
        {
            ApiKeyEntry ak => ak.Key,
            OAuthEntry oauth => oauth.AccessToken,
            WellKnownEntry wk => wk.GetCredential(),
            _ => throw new ArgumentException($"Unsupported auth entry type: {entry.GetType().Name}")
        };
        return Task.FromResult(new AuthLoadResult { ApiKey = apiKey, BaseUrl = ApiBaseUrl });
    }

    public IReadOnlyList<ModelInfo> GetModels() =>
        KnownModels.ByProvider.TryGetValue(ProviderId, out var models) ? models : [];

    public bool SupportsFreeSearch => true;

    private static List<ModelInfo>? _liveModels;
    private static DateTime _liveFetchedAt;
    private static readonly SemaphoreSlim _fetchLock = new(1, 1);

    public Task<IReadOnlyList<ModelInfo>> FetchModelsAsync(AuthEntry? entry, CancellationToken ct = default)
        => FetchModelsAsync();

    public async Task<IReadOnlyList<ModelInfo>> FetchModelsAsync()
    {
        if (_liveModels != null && DateTime.UtcNow - _liveFetchedAt < TimeSpan.FromMinutes(5))
            return _liveModels;

        await _fetchLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_liveModels != null && DateTime.UtcNow - _liveFetchedAt < TimeSpan.FromMinutes(5))
                return _liveModels;

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "HPDOS");
            var response = await http.GetAsync("https://openrouter.ai/api/v1/models");
            response.EnsureSuccessStatusCode();

            using var doc = System.Text.Json.JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            if (!doc.RootElement.TryGetProperty("data", out var dataEl))
                return GetModels();

            // Get the set of recommended static model IDs for ordering
            var staticRecommended = new HashSet<string>(
                KnownModels.ByProvider.TryGetValue("openrouter", out var staticList)
                    ? staticList.Where(m => m.IsRecommended).Select(m => m.Id)
                    : [],
                StringComparer.OrdinalIgnoreCase);

            var recommended = new List<ModelInfo>();
            var paid = new List<ModelInfo>();
            var free = new List<ModelInfo>();

            foreach (var item in dataEl.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;

                var description = item.TryGetProperty("name", out var nameEl)
                    ? nameEl.GetString() ?? id
                    : id;

                // Free detection: ID ends with ":free" OR pricing.prompt=="0" && pricing.completion=="0"
                bool isFree = id.EndsWith(":free", StringComparison.OrdinalIgnoreCase);
                if (!isFree && item.TryGetProperty("pricing", out var pricingEl))
                {
                    var promptPrice = pricingEl.TryGetProperty("prompt", out var pp) ? pp.GetString() : null;
                    var completionPrice = pricingEl.TryGetProperty("completion", out var cp) ? cp.GetString() : null;
                    isFree = promptPrice == "0" && completionPrice == "0";
                }

                // Tool support: supported_parameters array contains "tools"
                bool supportsTools = false;
                if (item.TryGetProperty("supported_parameters", out var paramsEl))
                {
                    foreach (var param in paramsEl.EnumerateArray())
                    {
                        if (param.GetString() == "tools") { supportsTools = true; break; }
                    }
                }

                bool isRecommended = staticRecommended.Contains(id);

                var model = new ModelInfo(id, description, isRecommended, isFree, supportsTools);

                if (isRecommended)
                    recommended.Add(model);
                else if (isFree)
                    free.Add(model);
                else
                    paid.Add(model);
            }

            var result = new List<ModelInfo>(recommended.Count + paid.Count + free.Count);
            result.AddRange(recommended);
            result.AddRange(paid);
            result.AddRange(free);

            _liveModels = result;
            _liveFetchedAt = DateTime.UtcNow;
            return _liveModels;
        }
        catch
        {
            // Fall back to static list on any error
            return GetModels();
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    public Task<AuthEntry?> RefreshIfNeededAsync(AuthEntry entry) => Task.FromResult<AuthEntry?>(null);

    public async Task<bool> ValidateAsync(AuthEntry entry)
    {
        var apiKey = entry switch
        {
            ApiKeyEntry ak => ak.Key,
            OAuthEntry oauth => oauth.AccessToken,
            WellKnownEntry wk => wk.GetCredential(),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(apiKey)) return false;
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            var response = await httpClient.GetAsync($"{ApiBaseUrl}/auth/key");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<AuthFlowResult> StartBrowserFlowAsync(CancellationToken cancellationToken)
    {
        var codeVerifier = OAuthHelpers.GenerateCodeVerifier();
        var codeChallenge = OAuthHelpers.GenerateCodeChallenge(codeVerifier);

        var port = OAuthCallbackServer.FindAvailablePort(OAuthPort);
        if (port != OAuthPort)
            return new AuthFlowResult.Failed($"Port {OAuthPort} is required for OpenRouter OAuth but is in use.");

        // OpenRouter does not echo the state parameter back in the callback, so pass null to skip CSRF check.
        await using var callbackServer = new OAuthCallbackServer(port, expectedState: null);
        var authUrl = OAuthHelpers.BuildUrl(AuthBaseUrl, new Dictionary<string, string>
        {
            ["callback_url"] = callbackServer.CallbackUrl,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        });

        if (!OAuthHelpers.OpenBrowser(authUrl))
            return new AuthFlowResult.Failed("Failed to open browser. Please use API key authentication instead.");

        var callbackResult = await callbackServer.WaitForCallbackAsync(cancellationToken);
        return callbackResult switch
        {
            OAuthCallbackResult.Success success => await ExchangeCodeForKeyAsync(success.Code, codeVerifier),
            OAuthCallbackResult.Cancelled => new AuthFlowResult.Cancelled(),
            OAuthCallbackResult.Timeout => new AuthFlowResult.Failed("Authentication timed out. Please try again."),
            OAuthCallbackResult.Error error => new AuthFlowResult.Failed(error.Message),
            _ => new AuthFlowResult.Failed("Unknown callback result")
        };
    }

    private async Task<AuthFlowResult> ExchangeCodeForKeyAsync(string code, string codeVerifier)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "HPDOS");
            var body = $"{{\"code\":\"{EscapeJson(code)}\",\"code_verifier\":\"{EscapeJson(codeVerifier)}\",\"code_challenge_method\":\"S256\"}}";
            var response = await httpClient.PostAsync($"{ApiBaseUrl}/auth/keys",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                return new AuthFlowResult.Failed($"Failed to exchange code for API key: {response.StatusCode} - {err}");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("key", out var keyElement))
                return new AuthFlowResult.Failed("Response did not contain API key");

            var apiKey = keyElement.GetString();
            if (string.IsNullOrEmpty(apiKey)) return new AuthFlowResult.Failed("Received empty API key");

            return new AuthFlowResult.Success(new ApiKeyEntry { Key = apiKey });
        }
        catch (Exception ex) { return new AuthFlowResult.Failed($"Error exchanging code for API key: {ex.Message}", ex); }
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}
