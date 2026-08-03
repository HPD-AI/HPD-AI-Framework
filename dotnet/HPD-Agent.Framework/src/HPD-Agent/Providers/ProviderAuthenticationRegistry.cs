namespace HPD.Agent.Providers;

/// <summary>
/// Describes one host-registered, selectable static provider credential.
/// The registration contains only safe metadata and a secret-resolver key.
/// </summary>
public sealed record ProviderAuthenticationRegistration
{
    /// <summary>Gets the opaque registration name used by serialized run configuration.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the canonical provider key accepted by this registration.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>Gets the key passed to <see cref="Secrets.ISecretResolver"/>.</summary>
    public required string SecretKey { get; init; }

    /// <summary>Gets an optional host-facing display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets whether the host selected this registration as its default.</summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Gets the client families that may use this registration.
    /// An empty set means every family supported by the provider.
    /// </summary>
    public IReadOnlySet<ProviderClientFamily> Families { get; init; }
        = new HashSet<ProviderClientFamily>();
}

/// <summary>
/// Supplies caller information to a host authentication registry.
/// Hosts may use these values to enforce user and tenant ownership.
/// </summary>
public sealed record ProviderAuthenticationContext
{
    /// <summary>Gets the provider being resolved.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>Gets the client family being resolved.</summary>
    public required ProviderClientFamily Family { get; init; }

    /// <summary>Gets an optional host user identifier.</summary>
    public string? UserId { get; init; }

    /// <summary>Gets an optional host tenant identifier.</summary>
    public string? TenantId { get; init; }
}

/// <summary>
/// Provides safe metadata for named static provider credentials.
/// Implementations must enforce caller access before returning a registration.
/// </summary>
public interface IProviderAuthenticationRegistry
{
    /// <summary>Finds an accessible registration by its opaque name.</summary>
    ValueTask<ProviderAuthenticationRegistration?> FindAsync(
        string key,
        ProviderAuthenticationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Enumerates registrations accessible and compatible with a request.</summary>
    IAsyncEnumerable<ProviderAuthenticationRegistration> ListCompatibleAsync(
        ProviderAuthenticationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Thread-safe in-memory registry for single-process applications and tests.
/// Multi-user hosts should supply an implementation that enforces their ownership policy.
/// </summary>
public sealed class InMemoryProviderAuthenticationRegistry : IProviderAuthenticationRegistry
{
    private readonly Dictionary<string, ProviderAuthenticationRegistration> _registrations =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Adds or replaces a registration with the same opaque name.</summary>
    public void Register(ProviderAuthenticationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.ProviderKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.SecretKey);

        lock (_gate)
            _registrations[registration.Key] = registration;
    }

    /// <inheritdoc />
    public ValueTask<ProviderAuthenticationRegistration?> FindAsync(
        string key,
        ProviderAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(
                _registrations.TryGetValue(key, out var registration) && IsCompatible(registration, context)
                    ? registration
                    : null);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ProviderAuthenticationRegistration> ListCompatibleAsync(
        ProviderAuthenticationContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        ProviderAuthenticationRegistration[] snapshot;
        lock (_gate)
            snapshot = _registrations.Values.Where(item => IsCompatible(item, context)).ToArray();

        foreach (var registration in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return registration;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static bool IsCompatible(
        ProviderAuthenticationRegistration registration,
        ProviderAuthenticationContext context)
        => string.Equals(registration.ProviderKey, context.ProviderKey, StringComparison.Ordinal) &&
           (registration.Families.Count == 0 || registration.Families.Contains(context.Family));
}
