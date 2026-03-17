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
        // Key format: "{scope}:{subKey}" e.g. "openai:ApiKey", "openai:Endpoint"
        // We only hold API credentials, so only respond to ApiKey sub-keys.
        string scope, subKey;
        var colon = key.IndexOf(':');
        if (colon >= 0)
        {
            scope = key[..colon];
            subKey = key[(colon + 1)..];
            if (!subKey.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
                return null;
        }
        else
        {
            scope = key;
        }

        var entry = await _storage.GetAsync(scope);
        if (entry is null) return null;

        var provider = _authManager.GetProvider(scope);
        if (provider != null)
        {
            var refreshed = await provider.RefreshIfNeededAsync(entry);
            if (refreshed != null) { await _storage.UpdateActiveEntryAsync(scope, refreshed); entry = refreshed; }
        }

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
}
