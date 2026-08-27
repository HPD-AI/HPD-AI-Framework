using HPD.Environment.Contracts;
using System.Text.Json;

namespace HPD.Agent.MCP;

/// <summary>Configures SDK-owned MCP protocol negotiation.</summary>
public sealed record McpProtocolOptions
{
    /// <summary>Gets an optional exact protocol revision; null enables SDK negotiation and fallback.</summary>
    public string? ExactVersion { get; set; }
    /// <summary>Gets the maximum duration of discovery-first negotiation.</summary>
    public TimeSpan DiscoveryTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Configures policy surrounding ordinary, MRTR, and task-enabled invocations.</summary>
public sealed class McpInvocationOptions
{
    /// <summary>Gets or sets the maximum accepted input payload size.</summary>
    public int MaxInputPayloadCharacters { get; set; } = 200_000;
    /// <summary>Gets or sets the maximum duration of one client-handler callback.</summary>
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromMinutes(10);
    /// <summary>Gets or sets whether negotiated remote Tasks support may be used.</summary>
    public bool EnableRemoteTasks { get; set; }
    /// <summary>Gets or sets the application-owned resolver for MCP input requests.</summary>
    public IMcpInputResolver? InputResolver { get; set; }
    /// <summary>Gets or sets the application-owned authority check for sensitive MCP input requests.</summary>
    public IMcpInputAuthorizer? InputAuthorizer { get; set; }
    /// <summary>Gets or sets the application-owned protector for durable task recovery references.</summary>
    public IMcpRecoveryReferenceProtector? RecoveryReferenceProtector { get; set; }
    internal IMcpRemoteTaskAdapter? RemoteTaskAdapter { get; set; }
}

/// <summary>Protects and restores non-secret MCP task identity persisted in an operation journal.</summary>
public interface IMcpRecoveryReferenceProtector
{
    /// <summary>Protects a serialized recovery reference before it is journaled.</summary>
    ValueTask<string> ProtectAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Restores a protected recovery reference before task reconciliation.</summary>
    ValueTask<string> UnprotectAsync(string protectedReference, CancellationToken cancellationToken);
}

/// <summary>Controls initial MCP catalog loading and cache-aware refresh.</summary>
public sealed class McpCatalogOptions
{
    /// <summary>Gets or sets eager versus deferred initial loading.</summary>
    public McpCatalogLoadMode LoadMode { get; set; } = McpCatalogLoadMode.Eager;
    /// <summary>Gets or sets how long expired data may serve only after transient refresh failure.</summary>
    public TimeSpan StaleRetention { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>Gets or sets the application ceiling applied to protocol TTL values.</summary>
    public TimeSpan MaximumTtl { get; set; } = TimeSpan.FromHours(1);
    /// <summary>Gets or sets whether invalidation starts coalesced refresh.</summary>
    public bool RefreshOnInvalidation { get; set; } = true;
    /// <summary>Gets or sets whether scope-compatible stale data may survive transient failure.</summary>
    public bool ServeStaleOnTransientFailure { get; set; } = true;
}

/// <summary>Selects initial MCP catalog materialization behavior.</summary>
public enum McpCatalogLoadMode
{
    /// <summary>Load and validate the catalog during agent construction.</summary>
    Eager,
    /// <summary>Load at the first safe capability boundary.</summary>
    Deferred
}

/// <summary>Controls negotiated MCP notification delivery.</summary>
public sealed class McpSubscriptionOptions
{
    /// <summary>Gets or sets whether catalog invalidation notifications are requested.</summary>
    public bool EnableCatalogInvalidation { get; set; }
    /// <summary>Gets or sets explicit resource URIs requested for updates.</summary>
    public IReadOnlyList<string> ResourceUris { get; set; } = [];
    /// <summary>Gets or sets listener failure behavior.</summary>
    public McpSubscriptionFailurePolicy FailurePolicy { get; set; } =
        McpSubscriptionFailurePolicy.ReportAndContinue;
}

/// <summary>Controls behavior when a requested subscription cannot remain active.</summary>
public enum McpSubscriptionFailurePolicy
{
    /// <summary>Emit bounded diagnostics and retain remaining capabilities.</summary>
    ReportAndContinue,
    /// <summary>Fail the owning MCP source revision.</summary>
    FailSource
}

/// <summary>Selects the OAuth client-registration mechanism.</summary>
public enum McpOAuthClientRegistrationMode
{
    /// <summary>Use a Client ID Metadata Document.</summary>
    ClientIdMetadataDocument,
    /// <summary>Use application-provided registration credentials.</summary>
    PreRegistered,
    /// <summary>Use compatibility-only Dynamic Client Registration.</summary>
    DynamicRegistration
}

/// <summary>Configures OAuth registration without containing persisted token state.</summary>
public sealed class McpOAuthOptions
{
    /// <summary>Gets or sets the registration mechanism.</summary>
    public McpOAuthClientRegistrationMode RegistrationMode { get; set; } =
        McpOAuthClientRegistrationMode.ClientIdMetadataDocument;
    /// <summary>Gets or sets whether compatibility-only DCR is explicitly authorized.</summary>
    public bool AllowDynamicRegistration { get; set; }
    /// <summary>Gets or sets the application redirect URI.</summary>
    public Uri? RedirectUri { get; set; }
    /// <summary>Gets or sets the HTTPS Client ID Metadata Document URI.</summary>
    public Uri? ClientIdMetadataDocument { get; set; }
    /// <summary>Gets or sets a pre-registered client identifier.</summary>
    public string? ClientId { get; set; }
    /// <summary>Gets or sets a secret-store key for a pre-registered client secret.</summary>
    public string? ClientSecretKey { get; set; }
    /// <summary>Gets or sets requested scopes.</summary>
    public IReadOnlyList<string> Scopes { get; set; } = [];
}

/// <summary>Contains final application-level MCP runtime dependencies and policy.</summary>
public sealed class McpOptions
{
    /// <summary>Gets or sets whether failure of one configured server fails the complete source load.</summary>
    public bool FailOnServerError { get; set; } = true;
    /// <summary>Gets protocol negotiation options.</summary>
    public McpProtocolOptions Protocol { get; } = new();
    /// <summary>Gets invocation and MRTR options.</summary>
    public McpInvocationOptions Invocation { get; } = new();
    /// <summary>Gets catalog cache and refresh options.</summary>
    public McpCatalogOptions Catalog { get; } = new();
    /// <summary>Gets subscription options.</summary>
    public McpSubscriptionOptions Subscriptions { get; } = new();
    /// <summary>Gets OAuth options.</summary>
    public McpOAuthOptions OAuth { get; } = new();
    /// <summary>Gets or sets the application-owned isolated-process provider.</summary>
    public IProcessProvider? ProcessProvider { get; set; }
    /// <summary>Gets or sets an application-owned HTTP client factory for MCP transports.</summary>
    /// <remarks>The returned client is not disposed by HPD; revision disposal releases only the MCP transport.</remarks>
    public Func<McpServerConfig, HttpClient>? HttpClientFactory { get; set; }
    /// <summary>Gets or sets durable, issuer-bound authorization storage.</summary>
    public IMcpAuthorizationStore? AuthorizationStore { get; set; }

    internal void Validate()
    {
        if (Protocol.DiscoveryTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Protocol.DiscoveryTimeout));
        if (Invocation.MaxInputPayloadCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(Invocation.MaxInputPayloadCharacters));
        if (Invocation.HandlerTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Invocation.HandlerTimeout));
        if (Catalog.StaleRetention < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Catalog.StaleRetention));
        if (Catalog.MaximumTtl < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Catalog.MaximumTtl));
        if (OAuth.RegistrationMode == McpOAuthClientRegistrationMode.DynamicRegistration &&
            !OAuth.AllowDynamicRegistration)
            throw new InvalidOperationException("Dynamic MCP client registration requires AllowDynamicRegistration.");
    }
}

/// <summary>Describes one protocol-neutral request for user or host input.</summary>
public sealed record McpInputResolutionContext
{
    /// <summary>Gets the server registration name.</summary>
    public required string ServerName { get; init; }
    /// <summary>Gets the original tool name when reliably known.</summary>
    public string? ToolName { get; init; }
    /// <summary>Gets a generated HPD invocation identifier.</summary>
    public string? InvocationId { get; init; }
    /// <summary>Gets the bounded user-facing request description.</summary>
    public required string Description { get; init; }
    /// <summary>Gets the bounded JSON schema text supplied by the server.</summary>
    public required string Schema { get; init; }
    /// <summary>Gets whether resolving the request requires sensitive-input authority.</summary>
    public bool IsSensitive { get; init; }
}

/// <summary>Contains an application decision for one MCP input request.</summary>
public sealed record McpInputResolution
{
    /// <summary>Gets whether the request was resolved.</summary>
    public required bool Resolved { get; init; }
    /// <summary>Gets the resolved protocol-neutral JSON value.</summary>
    public JsonElement? Value { get; init; }
    /// <summary>Gets a bounded rejection reason.</summary>
    public string? RejectionReason { get; init; }
}

/// <summary>Resolves SDK-owned MRTR input requests through application policy.</summary>
public interface IMcpInputResolver
{
    /// <summary>Resolves or rejects one requested input.</summary>
    ValueTask<McpInputResolution> ResolveAsync(
        McpInputResolutionContext context,
        CancellationToken cancellationToken);
}

/// <summary>Authorizes an MCP input request before any application resolver receives it.</summary>
public interface IMcpInputAuthorizer
{
    /// <summary>Returns whether the current invocation may expand authority to resolve the request.</summary>
    ValueTask<bool> AuthorizeAsync(
        McpInputResolutionContext context,
        CancellationToken cancellationToken);
}

/// <summary>Identifies one normalized MCP resource registration independently from display name.</summary>
/// <param name="Value">The normalized registration identifier.</param>
public readonly record struct McpResourceRegistrationId(string Value)
{
    /// <summary>Creates a validated resource-registration identifier.</summary>
    public static McpResourceRegistrationId Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(value);
    }
}

/// <summary>Stores a versioned SDK token envelope bound to resource, issuer, client, and scope.</summary>
public sealed record McpAuthorizationRecord
{
    /// <summary>Gets the storage schema version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the normalized MCP resource-registration identity bound to this envelope.</summary>
    public required string ResourceRegistrationId { get; init; }
    /// <summary>Gets the normalized authorization-server issuer.</summary>
    public required string Issuer { get; init; }
    /// <summary>Gets the OAuth client identifier.</summary>
    public required string ClientId { get; init; }
    /// <summary>Gets normalized scopes.</summary>
    public required IReadOnlyList<string> Scopes { get; init; }
    /// <summary>Gets the protected SDK token-container payload.</summary>
    public required byte[] ProtectedTokenContainer { get; init; }
}

/// <summary>Persists issuer-bound MCP authorization state for one resource registration.</summary>
public interface IMcpAuthorizationStore
{
    /// <summary>Loads the current final record for a resource registration.</summary>
    ValueTask<McpAuthorizationRecord?> LoadAsync(
        McpResourceRegistrationId resource,
        CancellationToken cancellationToken);
    /// <summary>Atomically replaces the final record for a resource registration.</summary>
    ValueTask SaveAsync(
        McpResourceRegistrationId resource,
        McpAuthorizationRecord record,
        CancellationToken cancellationToken);
    /// <summary>Deletes authorization state for a resource registration.</summary>
    ValueTask DeleteAsync(
        McpResourceRegistrationId resource,
        CancellationToken cancellationToken);
}
