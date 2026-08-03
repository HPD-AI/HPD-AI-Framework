using HPD.Agent.Secrets;

namespace HPD.Agent.Providers;

/// <summary>Identifies the trust boundary authorized to use provider credentials.</summary>
public sealed record ProviderAuthorizationScope
{
    /// <summary>Gets the host-local trust-domain identity.</summary>
    public required string TrustDomainId { get; init; }

    /// <summary>Gets an optional tenant identity.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets an optional principal identity.</summary>
    public string? PrincipalId { get; init; }
}

/// <summary>Describes one atomic provider credential acquisition.</summary>
public sealed record ProviderCredentialRequest
{
    /// <summary>Gets the canonical provider key.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>Gets the client family that will use the credential.</summary>
    public required ProviderClientFamily Family { get; init; }

    /// <summary>Gets the opaque credential identity used in client cache keys.</summary>
    public required string Identity { get; init; }

    /// <summary>Gets the canonical key passed to the underlying secret resolver.</summary>
    public required string SecretKey { get; init; }

    /// <summary>Gets the caller's authorization scope.</summary>
    public required ProviderAuthorizationScope AuthorizationScope { get; init; }
}

/// <summary>Leases secret material and its cache identity as one atomic value.</summary>
public interface IProviderCredentialLease : IAsyncDisposable
{
    /// <summary>Gets the leased secret material.</summary>
    ReadOnlyMemory<char> Secret { get; }

    /// <summary>Gets the opaque non-secret credential identity.</summary>
    string Identity { get; }

    /// <summary>Gets the credential generation used in client cache identity.</summary>
    long Generation { get; }

    /// <summary>Gets the optional credential expiry.</summary>
    DateTimeOffset? ExpiresAt { get; }

    /// <summary>Gets a token signaled when this generation begins rotating.</summary>
    CancellationToken RotationToken { get; }
}

/// <summary>Atomically resolves provider secret material and identity metadata.</summary>
public interface IProviderCredentialResolver
{
    /// <summary>Acquires one execution-scoped credential lease.</summary>
    ValueTask<IProviderCredentialLease> AcquireAsync(
        ProviderCredentialRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Adapts the existing host secret chain to atomic provider credential leases.</summary>
public sealed class SecretResolverProviderCredentialResolver : IProviderCredentialResolver
{
    private readonly ISecretResolver _secrets;

    /// <summary>Initializes the adapter.</summary>
    public SecretResolverProviderCredentialResolver(ISecretResolver secrets) =>
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));

    /// <inheritdoc />
    public async ValueTask<IProviderCredentialLease> AcquireAsync(
        ProviderCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SecretKey);
        ArgumentNullException.ThrowIfNull(request.AuthorizationScope);

        var resolved = await _secrets.ResolveAsync(request.SecretKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Provider credential '{request.Identity}' did not resolve secret key '{request.SecretKey}'.");
        return new OwnedProviderCredentialLease(
            resolved.Value.ToCharArray(),
            request.Identity,
            generation: 0,
            expiresAt: null,
            CancellationToken.None);
    }
}

/// <summary>Creates non-cacheable credential leases for explicit per-run API keys.</summary>
public static class ProviderCredentialLease
{
    /// <summary>Creates an owned lease whose character buffer is cleared on disposal.</summary>
    public static IProviderCredentialLease CreateExplicit(ReadOnlySpan<char> secret)
    {
        if (secret.IsEmpty)
            throw new ArgumentException("The explicit provider credential cannot be empty.", nameof(secret));
        return new OwnedProviderCredentialLease(
            secret.ToArray(),
            $"explicit:{Guid.NewGuid():N}",
            generation: 0,
            expiresAt: null,
            CancellationToken.None);
    }
}

internal sealed class OwnedProviderCredentialLease : IProviderCredentialLease
{
    private char[]? _secret;

    public OwnedProviderCredentialLease(
        char[] secret,
        string identity,
        long generation,
        DateTimeOffset? expiresAt,
        CancellationToken rotationToken)
    {
        _secret = secret;
        Identity = identity;
        Generation = generation;
        ExpiresAt = expiresAt;
        RotationToken = rotationToken;
    }

    public ReadOnlyMemory<char> Secret => _secret is { } value
        ? value
        : throw new ObjectDisposedException(nameof(OwnedProviderCredentialLease));
    public string Identity { get; }
    public long Generation { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public CancellationToken RotationToken { get; }

    public ValueTask DisposeAsync()
    {
        var secret = Interlocked.Exchange(ref _secret, null);
        if (secret is not null)
            Array.Clear(secret);
        return ValueTask.CompletedTask;
    }
}
