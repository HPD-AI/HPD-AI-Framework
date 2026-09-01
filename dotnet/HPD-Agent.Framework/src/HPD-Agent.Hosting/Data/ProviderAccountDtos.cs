using HPD.Agent.Providers;

namespace HPD.Agent.Hosting.Data;

/// <summary>Portable input for one host-authorized provider account operation.</summary>
public sealed record ProviderAccountOperationRequest
{
    /// <summary>Gets the canonical provider key requested by the caller.</summary>
    public required string ProviderKey { get; init; }
    /// <summary>Gets the concrete provider backend key.</summary>
    public required string BackendKey { get; init; }
    /// <summary>Gets the client family whose grant is being managed.</summary>
    public required ProviderClientFamily Family { get; init; }
    /// <summary>Gets the portable authentication reference; never a token or literal credential.</summary>
    public required ProviderAuthentication Authentication { get; init; }
    /// <summary>Gets the caller trust-domain boundary.</summary>
    public required ProviderAuthorizationScope AuthorizationScope { get; init; }
    /// <summary>Gets the requested resource, audience, and scopes.</summary>
    public required ProviderCredentialAudience Audience { get; init; }
}

/// <summary>Selects one flow for a host-authorized provider account connection attempt.</summary>
public sealed record BeginProviderAuthorizationHostRequest
{
    /// <summary>Gets the exact account operation identity.</summary>
    public required ProviderAccountOperationRequest Account { get; init; }
    /// <summary>Gets the authorization flow selected by the host.</summary>
    public required ProviderAuthorizationFlow Flow { get; init; }
}

/// <summary>Correlates a protected authorization transaction with a transient host response.</summary>
public sealed record CompleteProviderAuthorizationRequest
{
    /// <summary>Gets the account operation identity used to begin authorization.</summary>
    public required ProviderAccountOperationRequest Account { get; init; }
    /// <summary>Gets the transient browser callback response.</summary>
    public required BrowserAuthorizationResponse Response { get; init; }
}

/// <summary>Identifies one existing device authorization transaction.</summary>
public sealed record ProviderDeviceAuthorizationOperationRequest
{
    /// <summary>Gets the exact account operation identity.</summary>
    public required ProviderAccountOperationRequest Account { get; init; }
    /// <summary>Gets the opaque device transaction identity.</summary>
    public required string TransactionId { get; init; }
}
