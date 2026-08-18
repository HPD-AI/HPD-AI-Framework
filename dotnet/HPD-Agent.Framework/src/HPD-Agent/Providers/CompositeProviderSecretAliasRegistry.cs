using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers;

/// <summary>
/// Merges multiple secret-alias sources in priority order. Used to combine the
/// generated provider composition's aliases with aliases contributed at runtime by
/// explicitly-registered providers (see <see cref="IProviderSecretAliasProvider"/>).
/// Earlier sources win so the canonical generated aliases take precedence.
/// </summary>
public sealed class CompositeProviderSecretAliasRegistry : IProviderSecretAliasRegistry
{
    private readonly IReadOnlyList<IProviderSecretAliasRegistry> _sources;

    /// <summary>Initializes a registry that consults the given sources in order.</summary>
    public CompositeProviderSecretAliasRegistry(params IProviderSecretAliasRegistry[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = new List<IProviderSecretAliasRegistry>(sources);
    }

    /// <inheritdoc />
    public IReadOnlyList<string>? GetEnvironmentVariables(string secretKey)
    {
        foreach (var source in _sources)
        {
            var aliases = source?.GetEnvironmentVariables(secretKey);
            if (aliases is { Count: > 0 })
                return aliases;
        }
        return null;
    }
}
