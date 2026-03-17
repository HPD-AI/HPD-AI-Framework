namespace HPDOS.Core.Auth.Providers;

public class GenericApiKeyAuthProvider : IAuthProvider, ILiveModelProvider
{
    private readonly string _providerId;
    private readonly string _displayName;
    private readonly string[] _environmentVariables;

    /// <summary>
    /// Optional live model fetch configuration.
    /// When set, "Browse all models…" becomes available for this provider.
    /// </summary>
    private readonly LiveModelConfig? _liveConfig;

    /// <summary>
    /// Configuration for fetching a live model list from a provider API.
    /// </summary>
    public sealed class LiveModelConfig
    {
        /// <summary>Full URL to the models list endpoint.</summary>
        public required string Endpoint { get; init; }

        /// <summary>
        /// Given an API key, returns the HTTP headers to add to the request.
        /// Default: <c>Authorization: Bearer {key}</c>
        /// </summary>
        public Func<string, Dictionary<string, string>>? BuildHeaders { get; init; }

        /// <summary>
        /// Extracts (id, displayName) pairs from a parsed JSON document.
        /// Default: expects <c>{ "data": [{ "id": "...", "name"?: "..." }] }</c>
        /// </summary>
        public Func<System.Text.Json.JsonDocument, IEnumerable<(string id, string name)>>? ParseModels { get; init; }
    }

    // Per-provider live model cache (keyed by providerId — static so it survives instance re-creation)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (List<ModelInfo> models, DateTime at)> _cache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public GenericApiKeyAuthProvider(string providerId, string displayName, string[] environmentVariables, LiveModelConfig? liveConfig = null)
    {
        _providerId = providerId.ToLowerInvariant();
        _displayName = displayName;
        _environmentVariables = environmentVariables.Length > 0
            ? environmentVariables
            : [$"{providerId.ToUpperInvariant()}_API_KEY"];
        _liveConfig = liveConfig;
    }

    // Backwards-compat constructor for providers without live fetch
    public GenericApiKeyAuthProvider(string providerId, string displayName, params string[] environmentVariables)
        : this(providerId, displayName, environmentVariables, null) { }

    public string ProviderId => _providerId;
    public string DisplayName => _displayName;
    public IReadOnlyList<string> EnvironmentVariables => _environmentVariables;

    public IReadOnlyList<AuthMethod> Methods =>
    [
        new AuthMethod
        {
            Type = AuthType.ApiKey,
            Label = "API key",
            Description = $"Enter your {_displayName} API key",
            IsRecommended = true,
            StartFlow = _ => Task.FromResult<AuthFlowResult>(
                new AuthFlowResult.NeedsUserInput(
                    $"Enter your {_displayName} API key",
                    "API Key",
                    (input, _) => Task.FromResult<AuthFlowResult>(
                        new AuthFlowResult.Success(new ApiKeyEntry { Key = input.Trim() }))))
        },
        new AuthMethod
        {
            Type = AuthType.WellKnown,
            Label = "Environment variable",
            Description = $"Use {string.Join(" or ", _environmentVariables)} environment variable",
            StartFlow = StartWellKnownFlowAsync
        }
    ];

    public IReadOnlyList<ModelInfo> GetModels() =>
        KnownModels.ByProvider.TryGetValue(ProviderId, out var models) ? models : [];

    public async Task<IReadOnlyList<ModelInfo>> FetchModelsAsync(AuthEntry? entry, CancellationToken ct = default)
    {
        if (_liveConfig is null) return GetModels();

        var apiKey = entry?.GetCredential();
        if (string.IsNullOrEmpty(apiKey)) return GetModels();

        if (_cache.TryGetValue(_providerId, out var cached) && DateTime.UtcNow - cached.at < TimeSpan.FromMinutes(5))
            return cached.models;

        var sem = _locks.GetOrAdd(_providerId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(_providerId, out cached) && DateTime.UtcNow - cached.at < TimeSpan.FromMinutes(5))
                return cached.models;

            using var http = new System.Net.Http.HttpClient();
            var headers = _liveConfig.BuildHeaders?.Invoke(apiKey)
                ?? new Dictionary<string, string> { ["Authorization"] = $"Bearer {apiKey}" };
            foreach (var (k, v) in headers)
                http.DefaultRequestHeaders.TryAddWithoutValidation(k, v);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "HPDOS");

            var response = await http.GetAsync(_liveConfig.Endpoint, ct);
            response.EnsureSuccessStatusCode();

            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            var staticRecommended = new HashSet<string>(
                KnownModels.ByProvider.TryGetValue(_providerId, out var staticList)
                    ? staticList.Where(m => m.IsRecommended).Select(m => m.Id)
                    : [],
                StringComparer.OrdinalIgnoreCase);

            var parseModels = _liveConfig.ParseModels ?? DefaultParseModels;
            var models = parseModels(doc)
                .Select(p => new ModelInfo(p.id, p.name, staticRecommended.Contains(p.id)))
                .ToList();

            var result = new List<ModelInfo>([
                .. models.Where(m => m.IsRecommended),
                .. models.Where(m => !m.IsRecommended).OrderBy(m => m.Id)
            ]);

            _cache[_providerId] = (result, DateTime.UtcNow);
            return result;
        }
        catch { return GetModels(); }
        finally { sem.Release(); }
    }

    private static IEnumerable<(string id, string name)> DefaultParseModels(System.Text.Json.JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("data", out var dataEl)) yield break;
        foreach (var item in dataEl.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrEmpty(id)) continue;
            var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? id : id;
            yield return (id, name);
        }
    }

    public Task<AuthLoadResult> LoadAsync(AuthEntry entry)
    {
        var apiKey = entry switch
        {
            ApiKeyEntry ak => ak.Key,
            WellKnownEntry wk => wk.GetCredential(),
            OAuthEntry oauth => oauth.AccessToken,
            _ => throw new ArgumentException($"Unsupported auth entry type: {entry.GetType().Name}")
        };
        return Task.FromResult(new AuthLoadResult { ApiKey = apiKey });
    }

    public Task<AuthEntry?> RefreshIfNeededAsync(AuthEntry entry) => Task.FromResult<AuthEntry?>(null);

    public Task<bool> ValidateAsync(AuthEntry entry)
    {
        var credential = entry switch
        {
            ApiKeyEntry ak => ak.Key,
            WellKnownEntry wk => wk.GetCredential(),
            OAuthEntry oauth => oauth.AccessToken,
            _ => null
        };
        return Task.FromResult(!string.IsNullOrWhiteSpace(credential));
    }

    private Task<AuthFlowResult> StartWellKnownFlowAsync(CancellationToken cancellationToken)
    {
        foreach (var envVar in _environmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
                return Task.FromResult<AuthFlowResult>(
                    new AuthFlowResult.Success(new WellKnownEntry { EnvVarName = envVar, CachedToken = value }));
        }
        return Task.FromResult<AuthFlowResult>(
            new AuthFlowResult.Failed($"No environment variable found. Set one of: {string.Join(", ", _environmentVariables)}"));
    }

    public static AuthEntry CreateApiKeyEntry(string apiKey) => new ApiKeyEntry { Key = apiKey };

    public static AuthEntry CreateWellKnownEntry(string envVarName) => new WellKnownEntry
    {
        EnvVarName = envVarName,
        CachedToken = Environment.GetEnvironmentVariable(envVarName)
    };
}

public static class CommonAuthProviders
{
    public static GenericApiKeyAuthProvider Anthropic => new(
        "anthropic", "Anthropic (Claude)", ["ANTHROPIC_API_KEY"],
        new GenericApiKeyAuthProvider.LiveModelConfig
        {
            Endpoint = "https://api.anthropic.com/v1/models",
            BuildHeaders = key => new()
            {
                ["x-api-key"] = key,
                ["anthropic-version"] = "2023-06-01"
            }
        });

    public static GenericApiKeyAuthProvider GoogleAI => new(
        "googleai", "Google AI (Gemini)", ["GOOGLE_API_KEY", "GEMINI_API_KEY"],
        new GenericApiKeyAuthProvider.LiveModelConfig
        {
            Endpoint = "https://generativelanguage.googleapis.com/v1beta/models",
            BuildHeaders = key => new() { ["x-goog-api-key"] = key },
            ParseModels = doc =>
            {
                if (!doc.RootElement.TryGetProperty("models", out var arr))
                    return [];
                return arr.EnumerateArray()
                    .Select(item =>
                    {
                        var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        // "models/gemini-2.0-flash" → "gemini-2.0-flash"
                        var id = name.StartsWith("models/") ? name["models/".Length..] : name;
                        var display = item.TryGetProperty("displayName", out var d) ? d.GetString() ?? id : id;
                        return (id, display);
                    })
                    .Where(p => !string.IsNullOrEmpty(p.id));
            }
        });

    public static GenericApiKeyAuthProvider Mistral => new(
        "mistral", "Mistral AI", ["MISTRAL_API_KEY"],
        new GenericApiKeyAuthProvider.LiveModelConfig
        {
            Endpoint = "https://api.mistral.ai/v1/models"
        });

    public static GenericApiKeyAuthProvider OpenRouter => new("openrouter", "OpenRouter", "OPENROUTER_API_KEY");
    public static GenericApiKeyAuthProvider HuggingFace => new("huggingface", "HuggingFace", "HUGGINGFACE_API_KEY", "HF_TOKEN");
    public static GenericApiKeyAuthProvider AzureOpenAI => new("azureopenai", "Azure OpenAI", "AZURE_OPENAI_API_KEY");
    public static GenericApiKeyAuthProvider Bedrock => new("bedrock", "AWS Bedrock", "AWS_ACCESS_KEY_ID");
}
