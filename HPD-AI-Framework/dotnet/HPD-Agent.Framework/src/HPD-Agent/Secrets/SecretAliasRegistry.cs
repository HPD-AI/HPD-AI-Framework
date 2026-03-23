// HPD-Agent/Secrets/SecretAliasRegistry.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HPD.Agent.Secrets;

/// <summary>
/// Global registry for well-known environment variable aliases for secret keys.
/// Providers register aliases during ModuleInitializers (e.g., HuggingFace uses both HUGGINGFACE_API_KEY and HF_TOKEN).
/// EnvironmentSecretResolver queries this registry to resolve secrets from multiple env var names.
/// </summary>
public static class SecretAliasRegistry
{
    private static readonly ConcurrentDictionary<string, string[]> _aliases =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers well-known environment variable names for a secret key.
    /// Called by provider package ModuleInitializers to register env var aliases.
    /// Thread-safe and idempotent - calling multiple times with the same key overwrites previous registration.
    /// </summary>
    /// <param name="secretKey">The secret key in "{scope}:{name}" format (e.g., "huggingface:ApiKey")</param>
    /// <param name="envVarNames">One or more environment variable names to check, in priority order (e.g., "HUGGINGFACE_API_KEY", "HF_TOKEN")</param>
    /// <example>
    /// <code>
    /// // In HuggingFaceProviderModule.Initialize():
    /// SecretAliasRegistry.Register("huggingface:ApiKey", "HUGGINGFACE_API_KEY", "HF_TOKEN");
    ///
    /// // In OpenAIProviderModule.Initialize():
    /// SecretAliasRegistry.Register("openai:ApiKey", "OPENAI_API_KEY");
    /// </code>
    /// </example>
    public static void Register(string secretKey, params string[] envVarNames)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new ArgumentException("Secret key cannot be null or whitespace.", nameof(secretKey));

        if (envVarNames == null || envVarNames.Length == 0)
            throw new ArgumentException("At least one environment variable name must be provided.", nameof(envVarNames));

        if (envVarNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Environment variable names cannot be null or whitespace.", nameof(envVarNames));

        _aliases[secretKey] = envVarNames;
    }

    /// <summary>
    /// Gets the registered environment variable aliases for a secret key.
    /// Used by EnvironmentSecretResolver to check multiple env var names.
    /// </summary>
    /// <param name="secretKey">The secret key in "{scope}:{name}" format</param>
    /// <returns>Array of environment variable names in priority order, or null if no aliases are registered</returns>
    /// <example>
    /// <code>
    /// // In EnvironmentSecretResolver.ResolveAsync():
    /// var aliases = SecretAliasRegistry.GetAliases("huggingface:ApiKey");
    /// // returns ["HUGGINGFACE_API_KEY", "HF_TOKEN"]
    ///
    /// foreach (var envVar in aliases ?? Array.Empty&lt;string&gt;())
    /// {
    ///     var value = Environment.GetEnvironmentVariable(envVar);
    ///     if (!string.IsNullOrEmpty(value))
    ///         return new ResolvedSecret { Value = value, Source = $"env:{envVar}" };
    /// }
    /// </code>
    /// </example>
    internal static string[]? GetAliases(string secretKey)
    {
        return _aliases.TryGetValue(secretKey, out var aliases) ? aliases : null;
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
        return new Dictionary<string, string[]>(_aliases, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For testing: clear the alias registry.
    /// </summary>
    internal static void ClearForTesting()
    {
        _aliases.Clear();
    }
}
