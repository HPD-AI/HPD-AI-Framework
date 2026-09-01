namespace HPD.Agent.Providers;

/// <summary>Classifies provider-authentication failures without exposing credential material.</summary>
public enum ProviderAuthenticationFailureKind
{
    /// <summary>The provider/backend does not support the selected mechanism.</summary>
    UnsupportedAuthentication,
    /// <summary>The authentication configuration is malformed or incomplete.</summary>
    ConfigurationError,
    /// <summary>Host interaction is required.</summary>
    InteractionRequired,
    /// <summary>Additional scope consent is required.</summary>
    ConsentRequired,
    /// <summary>The authorization session expired.</summary>
    AuthorizationExpired,
    /// <summary>The renewable grant is invalid.</summary>
    InvalidGrant,
    /// <summary>The grant does not contain required scopes.</summary>
    InsufficientScope,
    /// <summary>The authorization issuer does not match.</summary>
    IssuerMismatch,
    /// <summary>The authorization audience does not match.</summary>
    AudienceMismatch,
    /// <summary>The authorization store is unavailable.</summary>
    StoreUnavailable,
    /// <summary>The provider is temporarily unavailable.</summary>
    TemporarilyUnavailable,
    /// <summary>The authorization was revoked.</summary>
    Revoked,
    /// <summary>The provider returned an invalid protocol response.</summary>
    ProtocolError
}

/// <summary>Represents a redacted typed provider-authentication failure.</summary>
public class ProviderAuthenticationException : InvalidOperationException
{
    /// <summary>Initializes a redacted provider-authentication failure.</summary>
    public ProviderAuthenticationException(
        ProviderAuthenticationFailureKind failureKind,
        string providerKey,
        string backendKey,
        ProviderClientFamily family,
        string credentialIdentity,
        string message,
        bool isRetryable = false,
        bool interactionCanResolve = false,
        string? diagnosticCode = null) : base(message)
    {
        FailureKind = failureKind;
        ProviderKey = providerKey;
        BackendKey = backendKey;
        Family = family;
        CredentialIdentity = credentialIdentity;
        IsRetryable = isRetryable;
        InteractionCanResolve = interactionCanResolve;
        DiagnosticCode = diagnosticCode;
    }

    /// <summary>Gets the failure category.</summary>
    public ProviderAuthenticationFailureKind FailureKind { get; }
    /// <summary>Gets the provider key.</summary>
    public string ProviderKey { get; }
    /// <summary>Gets the backend key.</summary>
    public string BackendKey { get; }
    /// <summary>Gets the client family.</summary>
    public ProviderClientFamily Family { get; }
    /// <summary>Gets the stable non-secret credential identity.</summary>
    public string CredentialIdentity { get; }
    /// <summary>Gets whether retrying later can succeed without interaction.</summary>
    public bool IsRetryable { get; }
    /// <summary>Gets whether host interaction can resolve the failure.</summary>
    public bool InteractionCanResolve { get; }
    /// <summary>Gets a redacted provider diagnostic code.</summary>
    public string? DiagnosticCode { get; }
}

/// <summary>Signals that explicit host authorization must occur before client acquisition.</summary>
public sealed class ProviderInteractionRequiredException : ProviderAuthenticationException
{
    /// <summary>Initializes an interaction-required failure.</summary>
    public ProviderInteractionRequiredException(
        string providerKey,
        string backendKey,
        ProviderClientFamily family,
        string credentialIdentity,
        string? transactionId = null,
        DateTimeOffset? nextPollAt = null)
        : base(
            ProviderAuthenticationFailureKind.InteractionRequired,
            providerKey,
            backendKey,
            family,
            credentialIdentity,
            $"Provider/backend '{providerKey}/{backendKey}' requires explicit host authorization.",
            interactionCanResolve: true)
    {
        TransactionId = transactionId;
        NextPollAt = nextPollAt;
    }

    /// <summary>Gets the opaque resumable transaction identity, when authorization already began.</summary>
    public string? TransactionId { get; }

    /// <summary>Gets the earliest next device progression time, when known.</summary>
    public DateTimeOffset? NextPollAt { get; }
}
