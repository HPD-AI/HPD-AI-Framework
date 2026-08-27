namespace HPD.Agent.Providers;

/// <summary>Identifies where an authentication selection entered the runtime.</summary>
public enum ProviderSelectionSource
{
    /// <summary>Directly authored through the local builder.</summary>
    BuilderLocal,
    /// <summary>Supplied by a local per-run configuration.</summary>
    LocalRun,
    /// <summary>Deserialized through Hosting.</summary>
    Hosting,
    /// <summary>Deserialized through FFI.</summary>
    Ffi,
    /// <summary>Received from a remote agent.</summary>
    RemoteAgent,
    /// <summary>Supplied to an evaluation runtime.</summary>
    Evaluation
}

/// <summary>Contains immutable identities for one authentication-selection authorization decision.</summary>
public sealed record ProviderAuthenticationSelectionContext
{
    /// <summary>Gets the caller trust-boundary snapshot.</summary>
    public required ProviderAuthorizationScopeSnapshot Caller { get; init; }
    /// <summary>Gets the selected provider/backend.</summary>
    public required ProviderBackendIdentity Backend { get; init; }
    /// <summary>Gets the selected client family.</summary>
    public required ProviderClientFamily Family { get; init; }
    /// <summary>Gets the complete authentication selection.</summary>
    public required EffectiveProviderAuthentication Authentication { get; init; }
    /// <summary>Gets the untrusted selection provenance.</summary>
    public required ProviderSelectionSource Source { get; init; }
}

/// <summary>Authorizes portable authentication references before credential preparation.</summary>
public interface IProviderAuthenticationSelectionAuthorizer
{
    /// <summary>Authorizes the exact immutable selection and caller scope.</summary>
    ValueTask AuthorizeAsync(
        ProviderAuthenticationSelectionContext context,
        CancellationToken cancellationToken = default);
}
