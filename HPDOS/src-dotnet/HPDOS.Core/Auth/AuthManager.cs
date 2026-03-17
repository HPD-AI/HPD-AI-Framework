using HPDOS.Core.Auth.Providers;

namespace HPDOS.Core.Auth;

public class AuthManager
{
    private readonly AuthStorage _storage;
    private readonly List<IAuthProvider> _providers;

    public AuthManager() : this(new AuthStorage()) { }

    public AuthManager(AuthStorage storage)
    {
        _storage = storage;
        _providers =
        [
            new OpenAICodexAuthProvider(),
            new GitHubCopilotAuthProvider(),
            new OpenRouterAuthProvider(),
            CommonAuthProviders.Anthropic,
            CommonAuthProviders.GoogleAI,
            CommonAuthProviders.Mistral,
            CommonAuthProviders.HuggingFace,
            CommonAuthProviders.AzureOpenAI,
            CommonAuthProviders.Bedrock,
        ];
    }

    public AuthStorage Storage => _storage;
    public IReadOnlyList<IAuthProvider> Providers => _providers;

    public IAuthProvider? GetProvider(string providerId) =>
        _providers.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

    public void RegisterProvider(IAuthProvider provider)
    {
        _providers.RemoveAll(p => p.ProviderId.Equals(provider.ProviderId, StringComparison.OrdinalIgnoreCase));
        _providers.Add(provider);
    }

    public async Task<ResolvedCredentials?> ResolveCredentialsAsync(string providerId)
    {
        var provider = GetProvider(providerId);
        var entry = await _storage.GetAsync(providerId);

        if (entry != null)
        {
            if (provider != null)
            {
                var refreshed = await provider.RefreshIfNeededAsync(entry);
                if (refreshed != null)
                {
                    await _storage.UpdateActiveEntryAsync(providerId, refreshed);
                    entry = refreshed;
                }
                var loadResult = await provider.LoadAsync(entry);
                return new ResolvedCredentials
                {
                    ApiKey = loadResult.ApiKey,
                    BaseUrl = loadResult.BaseUrl,
                    CustomHeaders = loadResult.CustomHeaders,
                    Source = GetCredentialSource(entry),
                    AccountId = loadResult.AccountId
                };
            }
            return new ResolvedCredentials { ApiKey = entry.GetCredential(), Source = GetCredentialSource(entry) };
        }

        if (provider != null)
        {
            foreach (var envVar in provider.EnvironmentVariables)
            {
                var value = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrEmpty(value))
                    return new ResolvedCredentials { ApiKey = value, Source = $"env:{envVar}" };
            }
        }

        var defaultEnvVar = $"{providerId.ToUpperInvariant()}_API_KEY";
        var defaultValue = Environment.GetEnvironmentVariable(defaultEnvVar);
        if (!string.IsNullOrEmpty(defaultValue))
            return new ResolvedCredentials { ApiKey = defaultValue, Source = $"env:{defaultEnvVar}" };

        return null;
    }

    public async Task<List<AuthSummary>> GetAuthSummaryAsync()
    {
        var result = new List<AuthSummary>();
        var storedAuth = await _storage.GetAllAsync();

        foreach (var provider in _providers)
        {
            var summary = new AuthSummary { ProviderId = provider.ProviderId, DisplayName = provider.DisplayName };
            summary.HasModels = provider is IModelProvider;
            summary.SupportsFreeModels = provider is IModelProvider mp && mp.SupportsFreeSearch;

            if (storedAuth.TryGetValue(provider.ProviderId, out var slot) && slot.Entries.Count > 0)
            {
                var active = slot.ActiveEntry;
                if (active != null)
                {
                    summary.IsAuthenticated = true;
                    summary.Source = GetCredentialSource(active);
                    if (active is OAuthEntry oauth)
                    {
                        summary.ExpiresAt = oauth.ExpiresAt;
                        summary.AccountId = oauth.AccountId;
                        summary.IsExpired = oauth.IsExpired;
                    }
                }

                summary.ActiveEntryId = slot.ActiveEntryId;
                summary.StoredEntries = slot.Entries
                    .Select(s => new StoredEntryInfo
                    {
                        Id = s.Id,
                        MethodLabel = s.Entry.MethodLabel ?? GetCredentialSource(s.Entry),
                        AccountId = s.Entry is OAuthEntry o ? o.AccountId : null,
                        ExpiresAt = s.Entry is OAuthEntry oe ? oe.ExpiresAt : null,
                        IsExpired = s.Entry is OAuthEntry ox && ox.IsExpired,
                        Source = GetCredentialSource(s.Entry)
                    })
                    .ToList();
            }
            else
            {
                foreach (var envVar in provider.EnvironmentVariables)
                {
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVar)))
                    {
                        summary.IsAuthenticated = true;
                        summary.Source = $"env:{envVar}";
                        break;
                    }
                }
            }

            result.Add(summary);
        }

        return result;
    }

    private static string GetCredentialSource(AuthEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.MethodLabel))
            return entry.MethodLabel;
        return entry switch
        {
            OAuthEntry => "oauth",
            ApiKeyEntry => "api",
            WellKnownEntry wk => $"env:{wk.EnvVarName}",
            _ => "unknown"
        };
    }
}

public class ResolvedCredentials
{
    public required string ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public Dictionary<string, string>? CustomHeaders { get; init; }
    public required string Source { get; init; }
    public string? AccountId { get; init; }
}

public class AuthSummary
{
    public string ProviderId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsAuthenticated { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? AccountId { get; set; }
    public bool IsExpired { get; set; }
    public bool HasModels { get; set; }
    public bool SupportsFreeModels { get; set; }
    public string? ActiveEntryId { get; set; }
    public List<StoredEntryInfo>? StoredEntries { get; set; }
}

public class StoredEntryInfo
{
    public string Id { get; set; } = "";
    public string MethodLabel { get; set; } = "";
    public string? AccountId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsExpired { get; set; }
    public string Source { get; set; } = "";
}
