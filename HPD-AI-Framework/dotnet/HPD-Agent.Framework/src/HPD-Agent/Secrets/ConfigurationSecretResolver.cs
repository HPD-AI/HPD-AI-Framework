using Microsoft.Extensions.Configuration;

namespace HPD.Agent.Secrets;

/// <summary>
/// Resolves secrets from Microsoft.Extensions.Configuration (appsettings.json, user-secrets, etc.).
///
/// Key "stripe:ApiKey" checks:
///   1. configuration["stripe:ApiKey"]
///   2. configuration["Stripe:ApiKey"]
///   3. configuration["Providers:stripe:ApiKey"]
///   4. configuration["Providers:Stripe:ApiKey"]
///
/// IConfiguration already uses ":" as section separator, so the key format
/// maps naturally: "stripe:ApiKey" → { "stripe": { "ApiKey": "..." } }
/// The "Providers:{provider}" form matches AgentBuilder provider configuration.
/// </summary>
public sealed class ConfigurationSecretResolver : ISecretResolver
{
    private readonly IConfiguration _configuration;

    public ConfigurationSecretResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default)
    {
        foreach (var candidate in GetCandidateKeys(key))
        {
            var value = _configuration[candidate];
            if (!string.IsNullOrWhiteSpace(value))
                return new(new ResolvedSecret { Value = value, Source = $"config:{candidate}" });
        }

        return default;
    }

    private static IEnumerable<string> GetCandidateKeys(string key)
    {
        yield return key;

        var colonIndex = key.IndexOf(':');
        if (colonIndex <= 0)
        {
            yield break;
        }

        var scope = key[..colonIndex];
        var name = key[(colonIndex + 1)..];
        var capitalizedScope = Capitalize(scope);

        yield return $"{capitalizedScope}:{name}";
        yield return $"Providers:{scope}:{name}";
        yield return $"Providers:{capitalizedScope}:{name}";
    }

    private static string Capitalize(string value)
        => string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];
}
