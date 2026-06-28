namespace HPD.Agent.Secrets;

/// <summary>
/// Resolves secrets from environment variables.
/// Provider contributions populate the effective alias table used by this resolver.
/// </summary>
public sealed class EnvironmentSecretResolver : ISecretResolver
{
    public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default)
    {
        var registeredAliases = SecretAliasRegistry.GetAliases(key);
        if (registeredAliases == null)
            return default;

        foreach (var alias in registeredAliases)
        {
            var value = System.Environment.GetEnvironmentVariable(alias);
            if (!string.IsNullOrWhiteSpace(value))
                return new(new ResolvedSecret { Value = value, Source = $"env:{alias}" });
        }

        return default;
    }
}
