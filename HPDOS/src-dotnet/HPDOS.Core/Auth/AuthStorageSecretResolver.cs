using HPD.Agent.Secrets;

namespace HPDOS.Core.Auth;

public sealed class AuthStorageSecretResolver : ISecretResolver
{
    private readonly AuthStorage _storage;
    private readonly AuthManager _authManager;

    public AuthStorageSecretResolver(AuthStorage storage, AuthManager authManager)
    {
        _storage = storage;
        _authManager = authManager;
    }

    public async ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default)
    {
        // Key format: "{scope}:{subKey}" e.g. "openai:ApiKey", "openai:Endpoint", "openai:CustomHeaders"
        // Handles API keys and OAuth metadata (BaseUrl, CustomHeaders) for custom endpoints.
        string scope, subKey;
        var colon = key.IndexOf(':');
        if (colon >= 0)
        {
            scope = key[..colon];
            subKey = key[(colon + 1)..];
        }
        else
        {
            scope = key;
            subKey = "ApiKey"; // default
        }

        var entry = await _storage.GetAsync(scope);
        if (entry is null) return null;

        var provider = _authManager.GetProvider(scope);
        if (provider != null)
        {
            var refreshed = await provider.RefreshIfNeededAsync(entry);
            if (refreshed != null) { await _storage.UpdateActiveEntryAsync(scope, refreshed); entry = refreshed; }
        }

        // Handle different secret sub-keys
        if (subKey.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedSecret
            {
                Value = entry.GetCredential(),
                Source = entry switch
                {
                    OAuthEntry => "oauth",
                    ApiKeyEntry => $"auth-storage:{scope}",
                    WellKnownEntry wk => $"env:{wk.EnvVarName}",
                    _ => "auth-storage"
                },
                ExpiresAt = entry is OAuthEntry o
                    ? DateTimeOffset.FromUnixTimeMilliseconds(o.ExpiresAtUnixMs)
                    : null
            };
        }

        // For Endpoint and CustomHeaders, load via auth provider to get the full AuthLoadResult
        if (subKey.Equals("Endpoint", StringComparison.OrdinalIgnoreCase) ||
            subKey.Equals("CustomHeaders", StringComparison.OrdinalIgnoreCase))
        {
            if (provider == null) return null;

            var loadResult = await provider.LoadAsync(entry);

            if (subKey.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(loadResult.BaseUrl))
                    return null;

                return new ResolvedSecret
                {
                    Value = loadResult.BaseUrl,
                    Source = "auth-provider",
                    ExpiresAt = entry is OAuthEntry o
                        ? DateTimeOffset.FromUnixTimeMilliseconds(o.ExpiresAtUnixMs)
                        : null
                };
            }

            if (subKey.Equals("CustomHeaders", StringComparison.OrdinalIgnoreCase))
            {
                // CustomHeaders is a dictionary, but ResolvedSecret only accepts string values.
                // For now, return a marker value. The provider will call LoadAsync to get the actual headers.
                if (loadResult.CustomHeaders == null || loadResult.CustomHeaders.Count == 0)
                    return null;

                // Return a JSON-serialized version of the headers dictionary
                var json = System.Text.Json.JsonSerializer.Serialize(loadResult.CustomHeaders);
                return new ResolvedSecret
                {
                    Value = json,
                    Source = "auth-provider",
                    ExpiresAt = entry is OAuthEntry o
                        ? DateTimeOffset.FromUnixTimeMilliseconds(o.ExpiresAtUnixMs)
                        : null
                };
            }
        }

        return null;
    }
}
