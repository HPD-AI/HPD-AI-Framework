using Microsoft.Extensions.Configuration;

namespace HPD.Agent.Secrets;

/// <summary>
/// Resolves secrets from Microsoft.Extensions.Configuration (appsettings.json, user-secrets, etc.).
///
/// Key "stripe:ApiKey" checks:
///   1. configuration["stripe:ApiKey"]
///   2. configuration["Stripe:ApiKey"]
///
/// IConfiguration already uses ":" as section separator, so the key format
/// maps naturally: "stripe:ApiKey" → { "stripe": { "ApiKey": "..." } }
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
        // Direct key lookup: "stripe:ApiKey"
        var value = _configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
            return new(new ResolvedSecret { Value = value, Source = $"config:{key}" });

        // Capitalized scope: "Stripe:ApiKey"
        var colonIndex = key.IndexOf(':');
        if (colonIndex > 0)
        {
            var capitalizedKey = char.ToUpperInvariant(key[0]) + key[1..];
            value = _configuration[capitalizedKey];
            if (!string.IsNullOrWhiteSpace(value))
                return new(new ResolvedSecret { Value = value, Source = $"config:{capitalizedKey}" });
        }

        return default;
    }
}
