using HPD.Agent.Providers;

namespace HPD.Agent.Secrets;

/// <summary>
/// Resolves secrets from environment variables.
/// Aliases come from the immutable generated provider composition installed by the host.
/// </summary>
public sealed class EnvironmentSecretResolver : ISecretResolver
{
    private readonly IProviderSecretAliasRegistry? _generatedAliases;

    /// <summary>Initializes an environment resolver with optional generated provider aliases.</summary>
    public EnvironmentSecretResolver(IProviderSecretAliasRegistry? generatedAliases = null) =>
        _generatedAliases = generatedAliases;

    /// <inheritdoc />
    public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default)
    {
        var registeredAliases = _generatedAliases?.GetEnvironmentVariables(key)
            ?? HPD.Agent.Providers.ProviderCompositionGlobalRegistry.SecretAliases?.GetEnvironmentVariables(key);
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
