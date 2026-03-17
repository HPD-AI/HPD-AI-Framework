namespace HPD.Agent.Secrets;

/// <summary>
/// Extension methods for ISecretResolver that provide common resolution patterns.
/// These are the primary API surface for providers and connectors.
/// </summary>
public static class SecretResolverExtensions
{
    /// <summary>
    /// Resolves a required secret and throws SecretNotFoundException if not found.
    /// </summary>
    /// <param name="resolver">The secret resolver.</param>
    /// <param name="key">The secret key in "{scope}:{name}" format (e.g., "openai:ApiKey").</param>
    /// <param name="displayName">User-friendly name for error messages (e.g., "OpenAI API Key").</param>
    /// <param name="explicitOverride">
    /// Optional explicit value that takes highest priority.
    /// If provided, this value is returned immediately without resolution.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved secret value.</returns>
    /// <exception cref="SecretNotFoundException">Thrown when the secret cannot be resolved.</exception>
    public static async ValueTask<string> RequireAsync(
        this ISecretResolver resolver,
        string key,
        string displayName,
        string? explicitOverride = null,
        CancellationToken cancellationToken = default)
    {
        // Explicit override has highest priority
        if (explicitOverride is not null)
        {
            return explicitOverride;
        }

        // Attempt to resolve the secret
        var resolved = await resolver.ResolveAsync(key, cancellationToken);

        // Throw if not found
        if (resolved is null)
        {
            throw new SecretNotFoundException(
                $"Required secret '{displayName}' (key: '{key}') was not found. " +
                $"Please configure it in environment variables, configuration file, or secret vault.",
                key,
                displayName);
        }

        return resolved.Value.Value;
    }

    /// <summary>
    /// Resolves an optional secret and returns null if not found (no throw).
    /// Useful for optional values like endpoints where null means "use default".
    /// </summary>
    /// <param name="resolver">The secret resolver.</param>
    /// <param name="key">The secret key in "{scope}:{name}" format (e.g., "azure-ai:Endpoint").</param>
    /// <param name="explicitOverride">
    /// Optional explicit value that takes highest priority.
    /// If provided, this value is returned immediately without resolution.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved secret value, or null if not found.</returns>
    public static async ValueTask<string?> ResolveOrDefaultAsync(
        this ISecretResolver resolver,
        string key,
        string? explicitOverride = null,
        CancellationToken cancellationToken = default)
    {
        // Explicit override has highest priority
        if (explicitOverride is not null)
        {
            return explicitOverride;
        }

        // Attempt to resolve the secret
        var resolved = await resolver.ResolveAsync(key, cancellationToken);

        // Return null if not found (no throw)
        return resolved?.Value;
    }
}
