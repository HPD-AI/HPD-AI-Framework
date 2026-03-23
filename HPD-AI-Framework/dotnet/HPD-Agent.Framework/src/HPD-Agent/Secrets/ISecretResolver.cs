namespace HPD.Agent.Secrets;

/// <summary>
/// Resolves secrets by key. Implementations can chain (env vars → file → vault).
/// All HPD components (providers, connectors, graph nodes) use this one interface.
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// Resolves a secret by key.
    /// Returns null if the secret is not found (next resolver in chain tries).
    /// </summary>
    /// <param name="key">
    /// The secret key in "{scope}:{name}" format.
    /// Examples: "openai:ApiKey", "azure-ai:Endpoint", "stripe:ApiKey", "slack:BotToken"
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved secret, or null if not found.</returns>
    ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// A resolved secret with metadata about its source.
/// </summary>
public readonly record struct ResolvedSecret
{
    /// <summary>The secret value.</summary>
    public required string Value { get; init; }

    /// <summary>Where the secret came from (for diagnostics, never logged with the value).</summary>
    public required string Source { get; init; }

    /// <summary>When the secret expires (null = never). Enables proactive refresh.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Implicit conversion to string for ergonomic usage.</summary>
    public static implicit operator string(ResolvedSecret secret) => secret.Value;
}
