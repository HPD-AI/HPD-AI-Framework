using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace HPDOS.Core.Auth.Providers;

public class GitHubCopilotAuthProvider : IAuthProvider, ILiveModelProvider
{
    private const string ClientId = "Ov23li8tweQw6odWQebz";
    private const string DefaultGitHubUrl = "https://github.com";
    private const string CopilotApiUrl = "https://api.githubcopilot.com";

    private readonly HttpClient _httpClient;

    public GitHubCopilotAuthProvider() : this(new HttpClient()) { }
    public GitHubCopilotAuthProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string ProviderId => "github-copilot";
    public string DisplayName => "GitHub Copilot";
    public IReadOnlyList<string> EnvironmentVariables => ["GITHUB_TOKEN", "GH_TOKEN"];

    public IReadOnlyList<AuthMethod> Methods =>
    [
        new AuthMethod
        {
            Type = AuthType.OAuthDeviceCode,
            Label = "GitHub.com",
            Description = "Authenticate with your GitHub.com account",
            IsRecommended = true,
            StartFlow = ct => StartDeviceCodeFlowAsync(DefaultGitHubUrl, ct)
        },
        new AuthMethod
        {
            Type = AuthType.OAuthDeviceCode,
            Label = "GitHub Enterprise",
            Description = "Authenticate with a GitHub Enterprise instance",
            StartFlow = StartEnterpriseFlowAsync
        },
        new AuthMethod
        {
            Type = AuthType.ApiKey,
            Label = "Personal Access Token",
            Description = "Enter a GitHub personal access token",
            StartFlow = _ => Task.FromResult<AuthFlowResult>(
                new AuthFlowResult.NeedsUserInput(
                    "Enter your GitHub personal access token",
                    "Personal Access Token",
                    (input, _) => Task.FromResult<AuthFlowResult>(
                        new AuthFlowResult.Success(new ApiKeyEntry { Key = input.Trim() }))))
        }
    ];

    public IReadOnlyList<ModelInfo> GetModels() =>
        KnownModels.ByProvider.TryGetValue(ProviderId, out var models) ? models : [];

    private static List<ModelInfo>? _liveModels;
    private static DateTime _liveFetchedAt;
    private static readonly SemaphoreSlim _fetchLock = new(1, 1);

    public async Task<IReadOnlyList<ModelInfo>> FetchModelsAsync(AuthEntry? entry, CancellationToken ct = default)
    {
        var token = entry?.GetCredential();
        if (string.IsNullOrEmpty(token)) return GetModels();

        if (_liveModels != null && DateTime.UtcNow - _liveFetchedAt < TimeSpan.FromMinutes(5))
            return _liveModels;

        await _fetchLock.WaitAsync(ct);
        try
        {
            if (_liveModels != null && DateTime.UtcNow - _liveFetchedAt < TimeSpan.FromMinutes(5))
                return _liveModels;

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Add("Copilot-Integration-Id", "vscode-chat");
            http.DefaultRequestHeaders.Add("User-Agent", "HPDOS");

            var response = await http.GetAsync($"{CopilotApiUrl}/models", ct);
            response.EnsureSuccessStatusCode();

            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("data", out var dataEl))
                return GetModels();

            var staticRecommended = new HashSet<string>(
                KnownModels.ByProvider.TryGetValue("github-copilot", out var staticList)
                    ? staticList.Where(m => m.IsRecommended).Select(m => m.Id)
                    : [],
                StringComparer.OrdinalIgnoreCase);

            var models = new List<ModelInfo>();
            foreach (var item in dataEl.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? id : id;
                models.Add(new ModelInfo(id, name, staticRecommended.Contains(id)));
            }

            _liveModels = [.. models.Where(m => m.IsRecommended), .. models.Where(m => !m.IsRecommended).OrderBy(m => m.Id)];
            _liveFetchedAt = DateTime.UtcNow;
            return _liveModels;
        }
        catch { return GetModels(); }
        finally { _fetchLock.Release(); }
    }

    public Task<AuthLoadResult> LoadAsync(AuthEntry entry)
    {
        var result = entry switch
        {
            OAuthEntry oauth => new AuthLoadResult
            {
                ApiKey = oauth.RefreshToken, // GitHub uses refresh token as bearer
                BaseUrl = CopilotApiUrl,
                CustomHeaders = new Dictionary<string, string>
                {
                    ["Copilot-Integration-Id"] = "hpd-agent-cli",
                    ["Openai-Intent"] = "conversation-edits"
                },
                AccountId = oauth.AccountId
            },
            ApiKeyEntry apiKey => new AuthLoadResult
            {
                ApiKey = apiKey.Key,
                BaseUrl = CopilotApiUrl,
                CustomHeaders = new Dictionary<string, string> { ["Copilot-Integration-Id"] = "hpd-agent-cli" }
            },
            WellKnownEntry wellKnown => new AuthLoadResult { ApiKey = wellKnown.GetCredential(), BaseUrl = CopilotApiUrl },
            _ => throw new ArgumentException($"Unsupported auth entry type: {entry.GetType().Name}")
        };
        return Task.FromResult(result);
    }

    public Task<AuthEntry?> RefreshIfNeededAsync(AuthEntry entry)
    {
        // GitHub tokens don't expire; re-auth required if invalid
        return Task.FromResult<AuthEntry?>(null);
    }

    public async Task<bool> ValidateAsync(AuthEntry entry)
    {
        try
        {
            var loadResult = await LoadAsync(entry);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{CopilotApiUrl}/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loadResult.ApiKey);
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private Task<AuthFlowResult> StartEnterpriseFlowAsync(CancellationToken cancellationToken) =>
        Task.FromResult<AuthFlowResult>(new AuthFlowResult.PendingUserAction(
            "Enter your GitHub Enterprise URL",
            null, null,
            _ => Task.FromResult<AuthFlowResult>(
                new AuthFlowResult.Failed("Enterprise URL input required — handled by the UI"))));

    internal async Task<AuthFlowResult> StartDeviceCodeFlowAsync(string githubUrl, CancellationToken cancellationToken)
    {
        try
        {
            var deviceCodeResponse = await RequestDeviceCodeAsync(githubUrl, cancellationToken);
            if (deviceCodeResponse == null) return new AuthFlowResult.Failed("Failed to get device code from GitHub");
            var verificationUrl = deviceCodeResponse.VerificationUri ?? $"{githubUrl}/login/device";
            return new AuthFlowResult.PendingUserAction(
                $"Enter code: {deviceCodeResponse.UserCode}",
                verificationUrl,
                deviceCodeResponse.UserCode,
                ct => PollForAccessTokenAsync(githubUrl, deviceCodeResponse.DeviceCode, deviceCodeResponse.Interval ?? 5, ct));
        }
        catch (Exception ex) { return new AuthFlowResult.Failed($"Failed to start GitHub device code flow: {ex.Message}", ex); }
    }

    private async Task<GitHubDeviceCodeResponse?> RequestDeviceCodeAsync(string githubUrl, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync(
            $"{githubUrl}/login/device/code",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = ClientId, ["scope"] = "read:user" }),
            cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.GitHubDeviceCodeResponse, cancellationToken: cancellationToken)
            : null;
    }

    private async Task<AuthFlowResult> PollForAccessTokenAsync(string githubUrl, string deviceCode, int intervalSeconds, CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(intervalSeconds, 5));
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (cancellationToken.IsCancellationRequested) return new AuthFlowResult.Cancelled();
            await Task.Delay(pollInterval, cancellationToken);
            try
            {
                var response = await _httpClient.PostAsync(
                    $"{githubUrl}/login/oauth/access_token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = ClientId,
                        ["device_code"] = deviceCode,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                    }),
                    cancellationToken);
                var tokenResponse = await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.GitHubAccessTokenResponse, cancellationToken: cancellationToken);
                if (!string.IsNullOrEmpty(tokenResponse?.AccessToken))
                {
                    var accountId = await GetUserLoginAsync(tokenResponse.AccessToken);
                    return new AuthFlowResult.Success(new OAuthEntry
                    {
                        AccessToken = tokenResponse.AccessToken,
                        RefreshToken = tokenResponse.AccessToken,
                        ExpiresAtUnixMs = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeMilliseconds(),
                        AccountId = accountId,
                        EnterpriseUrl = githubUrl != DefaultGitHubUrl ? githubUrl : null
                    });
                }
                if (tokenResponse?.Error == "authorization_pending") continue;
                if (tokenResponse?.Error == "slow_down") { pollInterval = pollInterval.Add(TimeSpan.FromSeconds(5)); continue; }
                if (tokenResponse?.Error == "expired_token") return new AuthFlowResult.Failed("Device code expired. Please try again.");
                if (tokenResponse?.Error == "access_denied") return new AuthFlowResult.Failed("Access denied. Please approve the authorization.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        return new AuthFlowResult.Failed("Authorization timed out. Please try again.");
    }

    private async Task<string?> GetUserLoginAsync(string accessToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HPDOS", "1.0"));
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var user = await response.Content.ReadFromJsonAsync(GitHubJsonContext.Default.GitHubUser);
                return user?.Login;
            }
        }
        catch { }
        return null;
    }

}

internal class GitHubDeviceCodeResponse
{
    [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = "";
    [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
    [JsonPropertyName("verification_uri")] public string? VerificationUri { get; set; }
    [JsonPropertyName("interval")] public int? Interval { get; set; }
}

internal class GitHubAccessTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

internal class GitHubUser
{
    [JsonPropertyName("login")] public string? Login { get; set; }
}

[JsonSerializable(typeof(GitHubDeviceCodeResponse))]
[JsonSerializable(typeof(GitHubAccessTokenResponse))]
[JsonSerializable(typeof(GitHubUser))]
internal partial class GitHubJsonContext : JsonSerializerContext { }
