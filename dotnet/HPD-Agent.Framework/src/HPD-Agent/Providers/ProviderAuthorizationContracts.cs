namespace HPD.Agent.Providers;

/// <summary>Identifies one durable provider authorization boundary.</summary>
public sealed record ProviderAuthorizationIdentity
{
    /// <summary>Gets the canonical provider key.</summary>
    public required string ProviderKey { get; init; }
    /// <summary>Gets the canonical backend key.</summary>
    public required string BackendKey { get; init; }
    /// <summary>Gets the host-defined account label.</summary>
    public required string AccountId { get; init; }
    /// <summary>Gets the authorization-server identity.</summary>
    public required string AuthorizationServer { get; init; }
    /// <summary>Gets the OAuth client identity.</summary>
    public required string ClientIdentity { get; init; }
    /// <summary>Gets the trust-domain identity.</summary>
    public required string TrustDomainId { get; init; }
    /// <summary>Gets the normalized resource.</summary>
    public string? Resource { get; init; }
    /// <summary>Gets the normalized audience.</summary>
    public string? Audience { get; init; }
    /// <summary>Gets the tenant identity.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets the principal identity.</summary>
    public string? PrincipalId { get; init; }
}

/// <summary>Owns protected bytes returned by an authorization protector or store.</summary>
public interface IProviderProtectedBuffer : IAsyncDisposable
{
    /// <summary>Gets the protected bytes while this buffer remains alive.</summary>
    ReadOnlyMemory<byte> Value { get; }
}

/// <summary>Owns the decrypted secret members of an authorization session.</summary>
public interface IProviderAuthorizationSecretSet : IAsyncDisposable
{
    /// <summary>Gets the access token.</summary>
    IProviderSecretBuffer AccessToken { get; }
    /// <summary>Gets the renewable refresh token.</summary>
    IProviderSecretBuffer? RefreshToken { get; }
    /// <summary>Gets the OAuth client secret when required.</summary>
    IProviderSecretBuffer? ClientSecret { get; }
}

/// <summary>An ephemeral decrypted provider authorization session.</summary>
public sealed record ProviderAuthorizationSession : IAsyncDisposable
{
    /// <summary>Gets the session schema version.</summary>
    public required string SchemaVersion { get; init; }
    /// <summary>Gets the owned session secrets.</summary>
    public required IProviderAuthorizationSecretSet Secrets { get; init; }
    /// <summary>Gets the token type.</summary>
    public required string TokenType { get; init; }
    /// <summary>Gets the access-token expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>Gets the granted scopes.</summary>
    public IReadOnlyList<string>? GrantedScopes { get; init; }
    /// <summary>Gets the OAuth client ID.</summary>
    public string? ClientId { get; init; }
    /// <summary>Gets the token-endpoint authentication method.</summary>
    public string? TokenEndpointAuthenticationMethod { get; init; }
    /// <summary>Gets the exact authorization-server identity.</summary>
    public required string AuthorizationServer { get; init; }
    /// <summary>Gets a stable non-secret subject.</summary>
    public string? Subject { get; init; }
    /// <summary>Gets non-secret provider protocol state.</summary>
    public IReadOnlyDictionary<string, string>? ProviderState { get; init; }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => Secrets.DisposeAsync();
}

/// <summary>An opaque protected authorization envelope.</summary>
public sealed record ProviderAuthorizationEnvelope : IAsyncDisposable
{
    /// <summary>Gets the envelope schema version.</summary>
    public required string SchemaVersion { get; init; }
    /// <summary>Gets the owned protected payload.</summary>
    public required IProviderProtectedBuffer ProtectedPayload { get; init; }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => ProtectedPayload.DisposeAsync();
}

/// <summary>Protects and unprotects provider authorization sessions.</summary>
public interface IProviderAuthorizationProtector
{
    /// <summary>Protects an ephemeral session for durable storage.</summary>
    ValueTask<ProviderAuthorizationEnvelope> ProtectAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Creates an owned ephemeral session from a protected envelope.</summary>
    ValueTask<ProviderAuthorizationSession> UnprotectAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationEnvelope envelope,
        CancellationToken cancellationToken = default);
}

/// <summary>An owned revisioned record returned by an authorization store.</summary>
public sealed record ProviderAuthorizationRecord : IAsyncDisposable
{
    /// <summary>Gets the protected envelope.</summary>
    public required ProviderAuthorizationEnvelope Envelope { get; init; }
    /// <summary>Gets the opaque store revision.</summary>
    public required string Revision { get; init; }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => Envelope.DisposeAsync();
}

/// <summary>The closed result of a conditional authorization-store write.</summary>
public abstract record ProviderAuthorizationWriteResult
{
    private ProviderAuthorizationWriteResult() { }

    /// <summary>A successfully committed write.</summary>
    public sealed record Written(string NewRevision) : ProviderAuthorizationWriteResult;

    /// <summary>A revision conflict with an optional current record.</summary>
    public sealed record Conflict(ProviderAuthorizationRecord? Current)
        : ProviderAuthorizationWriteResult, IAsyncDisposable
    {
        /// <inheritdoc />
        public ValueTask DisposeAsync() => Current?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}

/// <summary>Persists opaque protected authorization envelopes with conditional revisions.</summary>
public interface IProviderAuthorizationStore
{
    /// <summary>Loads the current authorization record.</summary>
    ValueTask<ProviderAuthorizationRecord?> LoadAsync(
        ProviderAuthorizationIdentity identity,
        CancellationToken cancellationToken = default);

    /// <summary>Conditionally commits an authorization envelope.</summary>
    ValueTask<ProviderAuthorizationWriteResult> TrySaveAsync(
        ProviderAuthorizationIdentity identity,
        string? expectedRevision,
        ProviderAuthorizationEnvelope envelope,
        CancellationToken cancellationToken = default);

    /// <summary>Conditionally deletes an authorization record.</summary>
    ValueTask<bool> TryDeleteAsync(
        ProviderAuthorizationIdentity identity,
        string expectedRevision,
        CancellationToken cancellationToken = default);
}

/// <summary>Groups one stable authorization store with the protector for its envelopes.</summary>
public sealed record ProviderAuthorizationStoreRegistration
{
    /// <summary>Gets the stable non-secret registry identity.</summary>
    public required string Identity { get; init; }
    /// <summary>Gets the opaque authorization store.</summary>
    public required IProviderAuthorizationStore Store { get; init; }
    /// <summary>Gets the session-envelope protector.</summary>
    public required IProviderAuthorizationProtector Protector { get; init; }
    /// <summary>Gets whether this registration is the one explicit default.</summary>
    public bool IsDefault { get; init; }
}

/// <summary>Resolves stable authorization-store registrations.</summary>
public interface IProviderAuthorizationStoreRegistry
{
    /// <summary>Resolves a named store or the one explicit default when the name is absent.</summary>
    ProviderAuthorizationStoreRegistration Resolve(string? storeKey);
}

/// <summary>Controls whether client acquisition may activate host interaction.</summary>
public enum ProviderAuthorizationActivation
{
    /// <summary>Authorization must be initiated through an explicit management operation.</summary>
    ExplicitOnly,
    /// <summary>Client acquisition may invoke the configured host interaction.</summary>
    AllowDuringClientAcquisition
}
