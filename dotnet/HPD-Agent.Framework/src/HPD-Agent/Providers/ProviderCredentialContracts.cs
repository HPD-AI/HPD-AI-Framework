using System.Collections.Immutable;

namespace HPD.Agent.Providers;

/// <summary>Identifies the trust boundary authorized to use provider credentials.</summary>
public sealed record ProviderAuthorizationScope
{
    /// <summary>Gets the host-local trust-domain identity.</summary>
    public required string TrustDomainId { get; init; }
    /// <summary>Gets the optional tenant identity.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets the optional principal identity.</summary>
    public string? PrincipalId { get; init; }
}

/// <summary>An immutable authorization-scope snapshot.</summary>
public sealed record ProviderAuthorizationScopeSnapshot
{
    /// <summary>Gets the trust-domain identity.</summary>
    public required string TrustDomainId { get; init; }
    /// <summary>Gets the optional tenant identity.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets the optional principal identity.</summary>
    public string? PrincipalId { get; init; }
}

/// <summary>Describes one provider credential request audience.</summary>
public sealed record ProviderCredentialAudience
{
    /// <summary>Gets the resource URI.</summary>
    public Uri? Resource { get; init; }
    /// <summary>Gets the audience.</summary>
    public string? Audience { get; init; }
    /// <summary>Gets the requested scopes.</summary>
    public IReadOnlyList<string>? Scopes { get; init; }
}

/// <summary>Describes one provider credential preparation request.</summary>
public sealed record ProviderCredentialRequest
{
    /// <summary>Gets the provider key.</summary>
    public required string ProviderKey { get; init; }
    /// <summary>Gets the backend key.</summary>
    public required string BackendKey { get; init; }
    /// <summary>Gets the client family.</summary>
    public required ProviderClientFamily Family { get; init; }
    /// <summary>Gets the authentication selection.</summary>
    public required ProviderAuthentication Authentication { get; init; }
    /// <summary>Gets the authorization scope.</summary>
    public required ProviderAuthorizationScope AuthorizationScope { get; init; }
    /// <summary>Gets the requested audience.</summary>
    public required ProviderCredentialAudience Audience { get; init; }
}

/// <summary>Owns clearable provider secret characters.</summary>
public interface IProviderSecretBuffer : IAsyncDisposable
{
    /// <summary>Gets the secret while alive.</summary>
    ReadOnlyMemory<char> Value { get; }
}

/// <summary>Owns one acquired SDK-native external identity.</summary>
public interface IProviderExternalIdentityLease : IAsyncDisposable
{
    /// <summary>Gets the credential.</summary>
    object Credential { get; }
    /// <summary>Gets the credential type.</summary>
    Type CredentialType { get; }
}

/// <summary>Creates leases for one named SDK-native external identity.</summary>
public interface IProviderExternalIdentityRegistration
{
    /// <summary>Gets the opaque registration name.</summary>
    string Name { get; }
    /// <summary>Gets the SDK credential type.</summary>
    Type CredentialType { get; }
    /// <summary>Acquires one identity lease.</summary>
    ValueTask<IProviderExternalIdentityLease> AcquireAsync(CancellationToken cancellationToken = default);
}

/// <summary>Resolves named SDK-native identity registrations.</summary>
public interface IProviderExternalIdentityRegistry
{
    /// <summary>Finds an exact registration name.</summary>
    IProviderExternalIdentityRegistration? Find(string name);
}

/// <summary>Resolves HPD-owned process-local literal secret registrations.</summary>
public interface IProviderRuntimeSecretRegistry
{
    /// <summary>Copies one registered secret into a new owned buffer.</summary>
    IProviderSecretBuffer Acquire(string registrationName);
}

/// <summary>Owns a request signer.</summary>
public interface IProviderRequestSignerLease : IAsyncDisposable
{
    /// <summary>Gets the signer.</summary>
    IProviderRequestSigner Signer { get; }
}

/// <summary>Signs an outgoing provider request.</summary>
public interface IProviderRequestSigner
{
    /// <summary>Applies the signature.</summary>
    ValueTask SignAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}

/// <summary>An owned request-ready provider credential.</summary>
public abstract record ProviderCredential : IAsyncDisposable
{
    private ProviderCredential() { }
    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();
    /// <summary>An API key.</summary>
    public sealed record ApiKey(IProviderSecretBuffer Value) : ProviderCredential { /// <inheritdoc />
        public override ValueTask DisposeAsync() => Value.DisposeAsync(); }
    /// <summary>A bearer token.</summary>
    public sealed record BearerToken(IProviderSecretBuffer Value) : ProviderCredential { /// <inheritdoc />
        public override ValueTask DisposeAsync() => Value.DisposeAsync(); }
    /// <summary>An external identity.</summary>
    public sealed record ExternalIdentity(IProviderExternalIdentityLease Lease) : ProviderCredential { /// <inheritdoc />
        public override ValueTask DisposeAsync() => Lease.DisposeAsync(); }
    /// <summary>A signed request.</summary>
    public sealed record SignedRequest(IProviderRequestSignerLease Lease) : ProviderCredential { /// <inheritdoc />
        public override ValueTask DisposeAsync() => Lease.DisposeAsync(); }
    /// <summary>Anonymous access.</summary>
    public sealed record Anonymous : ProviderCredential { /// <inheritdoc />
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}

/// <summary>An opaque committed credential generation.</summary>
public readonly record struct ProviderCredentialGeneration(string Value);

/// <summary>Identifies a credential and its isolation boundary.</summary>
public sealed record ProviderCredentialIdentity
{
    /// <summary>Gets the provider key.</summary>
    public required string ProviderKey { get; init; }
    /// <summary>Gets the backend key.</summary>
    public required string BackendKey { get; init; }
    /// <summary>Gets the opaque subject.</summary>
    public required string Subject { get; init; }
    /// <summary>Gets the trust domain.</summary>
    public required string TrustDomainId { get; init; }
    /// <summary>Gets the tenant.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets the principal.</summary>
    public string? PrincipalId { get; init; }
}

/// <summary>Leases a typed credential and lifecycle metadata.</summary>
public interface IProviderCredentialLease : IAsyncDisposable
{
    /// <summary>Gets the credential.</summary>
    ProviderCredential Credential { get; }
    /// <summary>Gets the identity.</summary>
    ProviderCredentialIdentity Identity { get; }
    /// <summary>Gets the generation.</summary>
    ProviderCredentialGeneration Generation { get; }
    /// <summary>Gets the expiry.</summary>
    DateTimeOffset? ExpiresAt { get; }
    /// <summary>Gets the rotation signal.</summary>
    CancellationToken RotationToken { get; }
}

/// <summary>An immutable normalized authorization grant.</summary>
public sealed record ProviderAuthorizationGrantSnapshot
{
    /// <summary>Gets the grant identity.</summary>
    public required string GrantIdentity { get; init; }
    /// <summary>Gets requested scopes.</summary>
    public required ImmutableArray<string> RequestedScopes { get; init; }
    /// <summary>Gets the scope-set identity.</summary>
    public required string RequestedScopeSetIdentity { get; init; }
    /// <summary>Gets the resource.</summary>
    public Uri? Resource { get; init; }
    /// <summary>Gets the audience.</summary>
    public string? Audience { get; init; }
}

/// <summary>A stable plan produced before credential acquisition.</summary>
public sealed record ProviderCredentialPlan
{
    /// <summary>Gets the backend.</summary>
    public required ProviderBackendIdentity Backend { get; init; }
    /// <summary>Gets the family.</summary>
    public required ProviderClientFamily Family { get; init; }
    /// <summary>Gets the scope.</summary>
    public required ProviderAuthorizationScopeSnapshot AuthorizationScope { get; init; }
    /// <summary>Gets the identity.</summary>
    public required ProviderCredentialIdentity Identity { get; init; }
    /// <summary>Gets the grant.</summary>
    public required ProviderAuthorizationGrantSnapshot Grant { get; init; }
    /// <summary>Gets the store identity.</summary>
    public string? AuthorizationStoreIdentity { get; init; }
    /// <summary>Gets the stable credential identity.</summary>
    public required string StableCredentialIdentity { get; init; }
    /// <summary>Gets the scope identity.</summary>
    public required string AuthorizationScopeIdentity { get; init; }
}

/// <summary>Separates identity preparation from credential acquisition.</summary>
public interface IProviderCredentialSource
{
    /// <summary>Prepares a stable plan.</summary>
    ValueTask<ProviderCredentialPlan> PrepareAsync(ProviderCredentialRequest request, CancellationToken cancellationToken = default);
    /// <summary>Acquires an owned credential.</summary>
    ValueTask<IProviderCredentialLease> AcquireAsync(ProviderCredentialPlan plan, CancellationToken cancellationToken = default);
}
