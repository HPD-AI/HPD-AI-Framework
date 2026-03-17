namespace HPD.Agent.Secrets;

/// <summary>
/// Resolves secrets from environment variables.
///
/// Resolution order:
///   1. Check SecretAliasRegistry for registered aliases (from [ModuleInitializer]s)
///      e.g., "huggingface:ApiKey" → tries HF_TOKEN, HUGGINGFACE_API_KEY
///   2. Fall back to naming convention:
///      "stripe:ApiKey"     → STRIPE_API_KEY, STRIPE_APIKEY
///      "slack:BotToken"    → SLACK_BOT_TOKEN, SLACK_BOTTOKEN
///      "azure-ai:Endpoint" → AZURE_AI_ENDPOINT, AZURE_AI_ENDPOINT
///      "azure-ai:ApiKey"   → AZURE_AI_API_KEY, AZURE_AI_APIKEY
/// </summary>
public sealed class EnvironmentSecretResolver : ISecretResolver
{
    public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default)
    {
        // Check module-initializer-registered aliases first
        var registeredAliases = SecretAliasRegistry.GetAliases(key);
        if (registeredAliases != null)
        {
            foreach (var alias in registeredAliases)
            {
                var value = Environment.GetEnvironmentVariable(alias);
                if (!string.IsNullOrWhiteSpace(value))
                    return new(new ResolvedSecret { Value = value, Source = $"env:{alias}" });
            }
        }

        // Fall back to naming convention
        var (scope, name) = ParseKey(key);
        var envNames = GenerateEnvVarNames(scope, name);

        foreach (var envName in envNames)
        {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value))
                return new(new ResolvedSecret { Value = value, Source = $"env:{envName}" });
        }

        return default;
    }

    private static (string scope, string name) ParseKey(string key)
    {
        var colonIndex = key.IndexOf(':');
        return colonIndex >= 0
            ? (key[..colonIndex], key[(colonIndex + 1)..])
            : (key, "ApiKey");
    }

    /// <summary>
    /// Generates env var name candidates from scope + name.
    ///
    /// "stripe", "ApiKey"     → ["STRIPE_API_KEY", "STRIPE_APIKEY"]
    /// "slack", "BotToken"    → ["SLACK_BOT_TOKEN", "SLACK_BOTTOKEN"]
    /// "azure-ai", "ApiKey"   → ["AZURE_AI_API_KEY", "AZURE_AI_APIKEY"]
    /// "azure-ai", "Endpoint" → ["AZURE_AI_ENDPOINT"]
    /// </summary>
    private static string[] GenerateEnvVarNames(string scope, string name)
    {
        var prefix = scope.ToUpperInvariant().Replace("-", "_");
        var snakeName = PascalToSnakeCase(name).ToUpperInvariant();
        var flatName = name.ToUpperInvariant();

        return snakeName == flatName
            ? [$"{prefix}_{snakeName}"]
            : [$"{prefix}_{snakeName}", $"{prefix}_{flatName}"];
    }

    private static string PascalToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (i > 0 && char.IsUpper(input[i]))
                sb.Append('_');
            sb.Append(input[i]);
        }
        return sb.ToString();
    }
}
