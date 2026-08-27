using System.Text.Json.Serialization;

namespace HPD.Agent.Providers;

/// <summary>Identifies one supported interactive provider-authorization flow.</summary>
public enum ProviderAuthorizationFlow
{
    /// <summary>OAuth authorization code with PKCE S256.</summary>
    AuthorizationCodePkce,
    /// <summary>OAuth device authorization.</summary>
    DeviceAuthorization
}

/// <summary>Identifies a provider authentication strategy and its contract version.</summary>
public readonly record struct ProviderAuthenticationStrategyId(string Value);

/// <summary>Describes one provider/backend authentication strategy.</summary>
public sealed record ProviderAuthenticationStrategyDescriptor
{
    /// <summary>Gets the namespaced strategy identity.</summary>
    public required ProviderAuthenticationStrategyId StrategyId { get; init; }
    /// <summary>Gets the canonical provider key.</summary>
    public required string ProviderKey { get; init; }
    /// <summary>Gets the canonical backend key.</summary>
    public required string BackendKey { get; init; }
    /// <summary>Gets the authentication kind.</summary>
    public required ProviderAuthenticationKind Kind { get; init; }
    /// <summary>Gets the supported interactive flows.</summary>
    public required IReadOnlyList<ProviderAuthorizationFlow> Flows { get; init; }
    /// <summary>Gets whether refresh is supported.</summary>
    public required bool SupportsRefresh { get; init; }
    /// <summary>Gets whether remote revocation is supported.</summary>
    public required bool SupportsRevocation { get; init; }
}

/// <summary>Describes one normalized grant within an authorization identity.</summary>
public sealed record ProviderAuthorizationGrant
{
    /// <summary>Gets the opaque normalized grant identity.</summary>
    public required string GrantIdentity { get; init; }
    /// <summary>Gets the normalized requested scopes.</summary>
    public required IReadOnlyList<string> RequestedScopes { get; init; }
    /// <summary>Gets the normalized scope-set identity.</summary>
    public required string RequestedScopeSetIdentity { get; init; }
    /// <summary>Gets the normalized audience requirement.</summary>
    public required ProviderCredentialAudience Audience { get; init; }
}

/// <summary>Contains the immutable normalized authorization identity and grant.</summary>
public sealed record NormalizedProviderAuthorizationRequest
{
    /// <summary>Gets an owned snapshot of the original request.</summary>
    public required ProviderCredentialRequest Original { get; init; }
    /// <summary>Gets the durable authorization identity.</summary>
    public required ProviderAuthorizationIdentity Identity { get; init; }
    /// <summary>Gets the normalized grant.</summary>
    public required ProviderAuthorizationGrant Grant { get; init; }
}

/// <summary>Base class for flow-specific provider authorization begin inputs.</summary>
public abstract record ProviderAuthorizationBeginContext
{
    /// <summary>Gets the normalized request.</summary>
    public required NormalizedProviderAuthorizationRequest Request { get; init; }
    /// <summary>Gets the time authority.</summary>
    public required TimeProvider TimeProvider { get; init; }
}

/// <summary>Contains inputs used to begin browser authorization.</summary>
public sealed record BrowserProviderAuthorizationBeginContext : ProviderAuthorizationBeginContext
{
    /// <summary>Gets the host callback URI that the strategy must validate before use.</summary>
    public required Uri RedirectUri { get; init; }
}

/// <summary>Contains inputs used to begin device authorization without a redirect URI.</summary>
public sealed record DeviceProviderAuthorizationBeginContext : ProviderAuthorizationBeginContext;

/// <summary>Selects one exact flow for an authorization begin operation.</summary>
public sealed record BeginProviderAuthorizationRequest
{
    /// <summary>Gets the immutable OAuth credential plan.</summary>
    public required ProviderCredentialPlan Plan { get; init; }
    /// <summary>Gets the flow selected by the host for this attempt.</summary>
    public required ProviderAuthorizationFlow Flow { get; init; }
}

/// <summary>Owns sensitive plaintext provider transaction state.</summary>
public interface IProviderSensitiveBuffer : IAsyncDisposable
{
    /// <summary>Gets the sensitive bytes while the buffer is alive.</summary>
    ReadOnlyMemory<byte> Value { get; }
}

/// <summary>Owns one plaintext authorization transaction.</summary>
public sealed record ProviderAuthorizationTransactionState : IAsyncDisposable
{
    /// <summary>Gets the opaque transaction identity.</summary>
    public required string TransactionId { get; init; }
    /// <summary>Gets the bound authorization identity.</summary>
    public required ProviderAuthorizationIdentity Identity { get; init; }
    /// <summary>Gets the bound strategy identity.</summary>
    public required ProviderAuthenticationStrategyId StrategyId { get; init; }
    /// <summary>Gets the flow bound to this transaction.</summary>
    public required ProviderAuthorizationFlow Flow { get; init; }
    /// <summary>Gets the transaction expiry.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
    /// <summary>Gets the earliest next device progression time, when applicable.</summary>
    public DateTimeOffset? NextPollAt { get; init; }
    /// <summary>Gets a coordinator-owned session commit that must finish before terminal consumption.</summary>
    public ProviderPendingAuthorizationCommit? PendingCommit { get; init; }
    /// <summary>Gets provider-owned PKCE, state, nonce, and protocol data.</summary>
    public required IProviderSensitiveBuffer ProviderState { get; init; }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ProviderState.DisposeAsync().ConfigureAwait(false);
        if (PendingCommit is not null)
            await PendingCommit.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Owns a protected session awaiting a recoverable cross-store commit.</summary>
public sealed record ProviderPendingAuthorizationCommit : IAsyncDisposable
{
    /// <summary>Gets the authorization-store revision observed before provider completion.</summary>
    public string? ExpectedAuthorizationRevision { get; init; }
    /// <summary>Gets the owned protected session envelope.</summary>
    public required ProviderAuthorizationEnvelope Envelope { get; init; }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => Envelope.DisposeAsync();
}

/// <summary>Contains a provider challenge and the transaction state transferred to the coordinator.</summary>
public sealed record ProviderAuthorizationStart
{
    /// <summary>Gets the owned plaintext transaction state.</summary>
    public required ProviderAuthorizationTransactionState TransactionState { get; init; }
    /// <summary>Gets the host-facing challenge.</summary>
    public required ProviderAuthorizationChallenge Challenge { get; init; }
}

/// <summary>Base class for host-facing authorization challenges.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BrowserAuthorizationChallenge), "browser")]
[JsonDerivedType(typeof(DeviceAuthorizationChallenge), "device")]
public abstract record ProviderAuthorizationChallenge
{
    /// <summary>Gets the opaque correlation identity.</summary>
    public required string TransactionId { get; init; }
    /// <summary>Gets the provider key.</summary>
    public required string ProviderKey { get; init; }
    /// <summary>Gets the backend key.</summary>
    public required string BackendKey { get; init; }
    /// <summary>Gets the account label.</summary>
    public required string AccountId { get; init; }
    /// <summary>Gets the challenge expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>Requests browser-based authorization.</summary>
public sealed record BrowserAuthorizationChallenge : ProviderAuthorizationChallenge
{
    /// <summary>Gets the provider authorization URI.</summary>
    public required Uri AuthorizationUri { get; init; }
    /// <summary>Gets the exact redirect URI.</summary>
    public required Uri RedirectUri { get; init; }
}

/// <summary>Requests device authorization.</summary>
public sealed record DeviceAuthorizationChallenge : ProviderAuthorizationChallenge
{
    /// <summary>Gets the user verification URI.</summary>
    public required Uri VerificationUri { get; init; }
    /// <summary>Gets the short user code.</summary>
    public required string UserCode { get; init; }
    /// <summary>Gets a provider-composed verification URI when available.</summary>
    public Uri? VerificationUriComplete { get; init; }
}

/// <summary>Base class for a host authorization response.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BrowserAuthorizationResponse), "browser")]
[JsonDerivedType(typeof(DeviceAuthorizationPresentationResponse), "device")]
public abstract record ProviderAuthorizationResponse
{
    /// <summary>Gets the matching transaction identity.</summary>
    public required string TransactionId { get; init; }
}

/// <summary>Contains the transient browser callback URI.</summary>
public sealed record BrowserAuthorizationResponse : ProviderAuthorizationResponse
{
    /// <summary>Gets the callback URI, which must never be logged or persisted.</summary>
    public required Uri CallbackUri { get; init; }
}

/// <summary>Defines how a host responds to a device authorization challenge.</summary>
public enum ProviderDeviceAuthorizationAction
{
    /// <summary>The host presented the device challenge; progression occurs separately.</summary>
    Presented,
    /// <summary>Cancel the authorization attempt.</summary>
    Cancel
}

/// <summary>Contains the host response to a device authorization challenge.</summary>
public sealed record DeviceAuthorizationPresentationResponse : ProviderAuthorizationResponse
{
    /// <summary>Gets the requested action.</summary>
    public required ProviderDeviceAuthorizationAction Action { get; init; }
}

/// <summary>Delegates environment-specific authorization interaction to the host.</summary>
public interface IProviderAuthorizationInteraction
{
    /// <summary>Presents a challenge and returns the correlated host response.</summary>
    ValueTask<ProviderAuthorizationResponse> AuthorizeAsync(
        ProviderAuthorizationChallenge challenge,
        CancellationToken cancellationToken = default);
}

/// <summary>Owns a protected authorization transaction envelope.</summary>
public sealed record ProviderAuthorizationTransactionEnvelope : IAsyncDisposable
{
    /// <summary>Gets the opaque transaction identity.</summary>
    public required string TransactionId { get; init; }
    /// <summary>Gets the canonical authorization-scope identity.</summary>
    public required string AuthorizationScopeIdentity { get; init; }
    /// <summary>Gets the transaction expiry.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
    /// <summary>Gets the protected payload.</summary>
    public required IProviderProtectedBuffer ProtectedPayload { get; init; }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => ProtectedPayload.DisposeAsync();
}

/// <summary>Owns a revisioned protected transaction record.</summary>
public sealed record ProviderAuthorizationTransactionRecord : IAsyncDisposable
{
    /// <summary>Gets the protected envelope.</summary>
    public required ProviderAuthorizationTransactionEnvelope Envelope { get; init; }
    /// <summary>Gets the opaque store revision.</summary>
    public required string Revision { get; init; }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => Envelope.DisposeAsync();
}

/// <summary>Protects and unprotects short-lived authorization transactions.</summary>
public interface IProviderAuthorizationTransactionProtector
{
    /// <summary>Protects plaintext state for transaction storage.</summary>
    ValueTask<ProviderAuthorizationTransactionEnvelope> ProtectAsync(
        ProviderAuthorizationTransactionState transaction,
        CancellationToken cancellationToken = default);

    /// <summary>Creates newly owned plaintext state after validating scope binding.</summary>
    ValueTask<ProviderAuthorizationTransactionState> UnprotectAsync(
        ProviderAuthorizationTransactionEnvelope envelope,
        ProviderAuthorizationScope scope,
        CancellationToken cancellationToken = default);
}

/// <summary>Stores protected expiring authorization transactions with revisioned replacement and terminal consumption.</summary>
public interface IProviderAuthorizationTransactionStore
{
    /// <summary>Creates a transaction after copying its protected payload.</summary>
    ValueTask<string> CreateAsync(
        ProviderAuthorizationTransactionEnvelope envelope,
        CancellationToken cancellationToken = default);
    /// <summary>Loads an owned transaction record.</summary>
    ValueTask<ProviderAuthorizationTransactionRecord?> LoadAsync(
        string transactionId,
        string authorizationScopeIdentity,
        CancellationToken cancellationToken = default);
    /// <summary>Atomically replaces the exact live revision after copying the protected payload.</summary>
    ValueTask<bool> TrySaveAsync(
        ProviderAuthorizationTransactionEnvelope envelope,
        string expectedRevision,
        CancellationToken cancellationToken = default);
    /// <summary>Atomically consumes the exact revision after identity validation.</summary>
    ValueTask<bool> TryConsumeAsync(
        string transactionId,
        string authorizationScopeIdentity,
        string expectedRevision,
        CancellationToken cancellationToken = default);
    /// <summary>Cancels a transaction in the given authorization scope.</summary>
    ValueTask CancelAsync(
        string transactionId,
        string authorizationScopeIdentity,
        CancellationToken cancellationToken = default);
}

/// <summary>Identifies the redacted state of one device authorization transaction.</summary>
public enum ProviderDeviceAuthorizationStatusKind
{
    /// <summary>The provider has not completed authorization.</summary>
    Pending,
    /// <summary>The provider requested a longer polling interval.</summary>
    SlowDown,
    /// <summary>A retryable provider failure preserved the transaction.</summary>
    TransientFailure,
    /// <summary>Authorization completed and the session was committed.</summary>
    Authorized,
    /// <summary>The user or provider denied authorization.</summary>
    Denied,
    /// <summary>The transaction expired.</summary>
    Expired
}

/// <summary>Contains redacted device authorization progression state.</summary>
public sealed record ProviderDeviceAuthorizationStatus
{
    /// <summary>Gets the opaque transaction identity.</summary>
    public required string TransactionId { get; init; }
    /// <summary>Gets the current progression state.</summary>
    public required ProviderDeviceAuthorizationStatusKind Status { get; init; }
    /// <summary>Gets the earliest next permitted advance time.</summary>
    public DateTimeOffset? NextPollAt { get; init; }
    /// <summary>Gets an optional redacted provider diagnostic code.</summary>
    public string? DiagnosticCode { get; init; }
}

/// <summary>Base class for an owned provider device progression result.</summary>
public abstract record ProviderDeviceAuthorizationProgress : IAsyncDisposable
{
    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();

    /// <summary>Preserves a live transaction with updated provider state.</summary>
    public sealed record Pending : ProviderDeviceAuthorizationProgress
    {
        /// <summary>Gets the owned replacement transaction.</summary>
        public required ProviderAuthorizationTransactionState Transaction { get; init; }
        /// <summary>Gets whether the provider requested slow-down behavior.</summary>
        public bool IsSlowDown { get; init; }
        /// <summary>Gets an optional redacted retryable diagnostic code.</summary>
        public string? DiagnosticCode { get; init; }
        /// <inheritdoc />
        public override ValueTask DisposeAsync() => Transaction.DisposeAsync();
    }

    /// <summary>Completes device authorization with an owned session.</summary>
    public sealed record Authorized : ProviderDeviceAuthorizationProgress
    {
        /// <summary>Gets the final provider transaction state retained until session commit succeeds.</summary>
        public required ProviderAuthorizationTransactionState Transaction { get; init; }
        /// <summary>Gets the owned authorized session.</summary>
        public required ProviderAuthorizationSession Session { get; init; }
        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync().ConfigureAwait(false);
            await Session.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Terminates device authorization without a session.</summary>
    public sealed record Terminal : ProviderDeviceAuthorizationProgress
    {
        /// <summary>Gets the denied or expired terminal state.</summary>
        public required ProviderDeviceAuthorizationStatusKind Status { get; init; }
        /// <summary>Gets an optional redacted provider diagnostic code.</summary>
        public string? DiagnosticCode { get; init; }
        /// <inheritdoc />
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Defines how a refresh response changes renewable refresh-token state.</summary>
public enum ProviderRefreshTokenDisposition
{
    /// <summary>Copy the current refresh token into the replacement session.</summary>
    RetainCurrent,
    /// <summary>Replace the current refresh token with the returned token.</summary>
    Replace,
    /// <summary>Remove refresh capability.</summary>
    Remove
}

/// <summary>Owns access-token and optional replacement refresh-token buffers.</summary>
public interface IProviderRefreshSecretSet : IAsyncDisposable
{
    /// <summary>Gets the refreshed access token.</summary>
    IProviderSecretBuffer AccessToken { get; }
    /// <summary>Gets the replacement refresh token.</summary>
    IProviderSecretBuffer? ReplacementRefreshToken { get; }
}

/// <summary>Contains an ownership-safe provider refresh result.</summary>
public sealed record ProviderAuthorizationRefreshResult : IAsyncDisposable
{
    /// <summary>Gets the owned returned secrets.</summary>
    public required IProviderRefreshSecretSet Secrets { get; init; }
    /// <summary>Gets the token type.</summary>
    public required string TokenType { get; init; }
    /// <summary>Gets the new expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>Gets the newly granted scopes.</summary>
    public IReadOnlyList<string>? GrantedScopes { get; init; }
    /// <summary>Gets the refresh-token transition.</summary>
    public required ProviderRefreshTokenDisposition RefreshTokenDisposition { get; init; }
    /// <summary>Gets non-secret provider state.</summary>
    public IReadOnlyDictionary<string, string>? ProviderState { get; init; }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => Secrets.DisposeAsync();
}

/// <summary>Describes the result of remote token revocation.</summary>
public sealed record ProviderRevocationResult
{
    /// <summary>Gets whether the provider confirmed revocation.</summary>
    public required bool Revoked { get; init; }
    /// <summary>Gets a redacted provider diagnostic code.</summary>
    public string? DiagnosticCode { get; init; }
}

/// <summary>Describes the local lifecycle state of a prepared OAuth account.</summary>
public enum ProviderAuthorizationStatusKind
{
    /// <summary>No durable session exists.</summary>
    Disconnected,
    /// <summary>A durable session can currently create credentials.</summary>
    Authorized,
    /// <summary>The session expired and cannot refresh.</summary>
    ReauthorizationRequired
}

/// <summary>Contains redacted status for one exact authorization identity.</summary>
public sealed record ProviderAuthorizationStatus
{
    /// <summary>Gets the lifecycle state.</summary>
    public required ProviderAuthorizationStatusKind Status { get; init; }
    /// <summary>Gets the stable non-secret credential identity.</summary>
    public required string CredentialIdentity { get; init; }
    /// <summary>Gets token expiry when known.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>Contains the outcome of a local disconnect and optional remote revocation.</summary>
public sealed record ProviderDisconnectResult
{
    /// <summary>Gets whether the exact local revision was deleted.</summary>
    public required bool LocalStateDeleted { get; init; }
    /// <summary>Gets the optional remote revocation result.</summary>
    public ProviderRevocationResult? RemoteRevocation { get; init; }
}

/// <summary>Provider-package SPI for OAuth normalization and protocol operations.</summary>
public interface IProviderAuthenticationStrategy
{
    /// <summary>Gets immutable strategy metadata.</summary>
    ProviderAuthenticationStrategyDescriptor Descriptor { get; }
    /// <summary>Normalizes a credential request into authorization identity and grant.</summary>
    ValueTask<NormalizedProviderAuthorizationRequest> NormalizeAsync(
        ProviderCredentialRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>Begins an interactive authorization flow.</summary>
    ValueTask<ProviderAuthorizationStart> BeginAuthorizationAsync(
        ProviderAuthorizationBeginContext context,
        CancellationToken cancellationToken = default);
    /// <summary>Validates a browser response without provider network I/O before transaction claim.</summary>
    ValueTask ValidateBrowserAuthorizationResponseAsync(
        ProviderAuthorizationTransactionState transaction,
        BrowserAuthorizationResponse response,
        CancellationToken cancellationToken = default);
    /// <summary>Exchanges one browser authorization result after the coordinator wins the transaction claim.</summary>
    ValueTask<ProviderAuthorizationSession> CompleteBrowserAuthorizationAsync(
        ProviderAuthorizationTransactionState transaction,
        BrowserAuthorizationResponse response,
        CancellationToken cancellationToken = default);
    /// <summary>Performs at most one bounded device authorization progression step.</summary>
    ValueTask<ProviderDeviceAuthorizationProgress> AdvanceDeviceAuthorizationAsync(
        ProviderAuthorizationTransactionState transaction,
        CancellationToken cancellationToken = default);
    /// <summary>Refreshes an existing session without mutating durable storage.</summary>
    ValueTask<ProviderAuthorizationRefreshResult> RefreshAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationSession current,
        CancellationToken cancellationToken = default);
    /// <summary>Creates an independently owned request-ready credential.</summary>
    ValueTask<ProviderCredential> CreateCredentialAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationSession current,
        CancellationToken cancellationToken = default);
    /// <summary>Attempts remote revocation without deleting local state.</summary>
    ValueTask<ProviderRevocationResult> RevokeAsync(
        ProviderAuthorizationIdentity identity,
        ProviderAuthorizationSession current,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves one exact provider/backend authentication strategy.</summary>
public interface IProviderAuthenticationStrategyRegistry
{
    /// <summary>Finds a strategy for the exact provider, backend, and authentication kind.</summary>
    IProviderAuthenticationStrategy? Find(
        string providerKey,
        string backendKey,
        ProviderAuthenticationKind kind);
}
