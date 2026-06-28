// HPD-Agent/Secrets/SecretAliasRegistry.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HPD.Agent;

namespace HPD.Agent.Secrets;

/// <summary>
/// Effective table of canonical environment variable names for secret keys.
/// Provider packages contribute aliases through the provider contribution surface;
/// this table is what secret resolvers query after those contributions are applied.
/// EnvironmentSecretResolver queries this registry instead of inferring names.
/// </summary>
public static class SecretAliasRegistry
{
    private static readonly ConcurrentDictionary<string, SecretAliasContribution> _aliases =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Applies canonical environment variable names for a secret key.
    /// Thread-safe and idempotent; calling multiple times with the same key replaces the effective alias contribution.
    /// </summary>
    /// <param name="secretKey">The secret key in "{scope}:{name}" format (e.g., "huggingface:ApiKey")</param>
    /// <param name="owner">The contribution owner that supplied the aliases.</param>
    /// <param name="envVarNames">One or more canonical environment variable names to check, in priority order.</param>
    internal static void Apply(string secretKey, HpdContributionOwner owner, params string[] envVarNames)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new ArgumentException("Secret key cannot be null or whitespace.", nameof(secretKey));

        ArgumentNullException.ThrowIfNull(owner);

        if (envVarNames == null || envVarNames.Length == 0)
            throw new ArgumentException("At least one environment variable name must be provided.", nameof(envVarNames));

        if (envVarNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Environment variable names cannot be null or whitespace.", nameof(envVarNames));

        _aliases[secretKey] = new SecretAliasContribution(secretKey, envVarNames, owner);
    }

    /// <summary>
    /// Gets the registered environment variable names for a secret key.
    /// Used by EnvironmentSecretResolver to check explicit env var names.
    /// </summary>
    /// <param name="secretKey">The secret key in "{scope}:{name}" format</param>
    /// <returns>Array of environment variable names in priority order, or null if no names are registered</returns>
    /// <example>
    /// <code>
    /// // In EnvironmentSecretResolver.ResolveAsync():
    /// var aliases = SecretAliasRegistry.GetAliases("huggingface:ApiKey");
    /// // returns ["HUGGINGFACE_API_KEY"]
    ///
    /// foreach (var envVar in aliases ?? Array.Empty&lt;string&gt;())
    /// {
    ///     var value = System.Environment.GetEnvironmentVariable(envVar);
    ///     if (!string.IsNullOrEmpty(value))
    ///         return new ResolvedSecret { Value = value, Source = $"env:{envVar}" };
    /// }
    /// </code>
    /// </example>
    internal static string[]? GetAliases(string secretKey)
    {
        return _aliases.TryGetValue(secretKey, out var aliases) ? aliases.EnvironmentVariableNames : null;
    }

    /// <summary>
    /// Gets all registered secret key aliases.
    /// Used by CLI diagnostics and testing to inspect the registry.
    /// </summary>
    /// <returns>Read-only dictionary of all registered aliases</returns>
    /// <example>
    /// <code>
    /// // CLI diagnostics:
    /// var allAliases = SecretAliasRegistry.GetAll();
    /// foreach (var (secretKey, envVars) in allAliases)
    /// {
    ///     Console.WriteLine($"{secretKey}: {string.Join(", ", envVars)}");
    /// }
    /// </code>
    /// </example>
    public static IReadOnlyDictionary<string, string[]> GetAll()
    {
        // Return a snapshot copy for thread safety
        return _aliases.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.EnvironmentVariableNames,
            StringComparer.Ordinal);
    }

    internal static bool RemoveOwner(HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var removed = false;
        foreach (var pair in _aliases.ToArray())
        {
            if (pair.Value.Owner == owner)
            {
                removed |= _aliases.TryRemove(pair.Key, out _);
            }
        }

        return removed;
    }

    /// <summary>
    /// For testing: clear the alias registry.
    /// </summary>
    internal static void ClearForTesting()
    {
        _aliases.Clear();
    }

    private sealed record SecretAliasContribution(
        string SecretKey,
        string[] EnvironmentVariableNames,
        HpdContributionOwner Owner);
}
