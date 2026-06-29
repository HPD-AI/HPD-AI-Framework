using ModelContextProtocol.Authentication;

namespace HPD.Agent.MCP;

/// <summary>
/// Provides application-owned OAuth runtime hooks for HTTP MCP servers.
/// </summary>
/// <remarks>
/// Implement this in the host application to control user-facing auth behavior
/// such as browser redirects, token persistence, scope selection, and dynamic
/// client registration persistence. This runtime is code-injected through
/// <see cref="MCPOptions"/> and is not serialized into MCP manifests.
/// </remarks>
public interface IMcpOAuthRuntime
{
    /// <summary>
    /// Gets persisted client registration credentials for this MCP server, if available.
    /// Manifest values still take precedence when explicitly configured.
    /// </summary>
    McpOAuthClientRegistration? GetClientRegistration(MCPServerConfig server);

    /// <summary>
    /// Creates the redirect handler used when an MCP server requires interactive OAuth authorization.
    /// </summary>
    AuthorizationRedirectDelegate? CreateAuthorizationRedirectDelegate(MCPServerConfig server);

    /// <summary>
    /// Creates the token cache used by the SDK OAuth provider for this MCP server.
    /// </summary>
    ITokenCache? CreateTokenCache(MCPServerConfig server);

    /// <summary>
    /// Creates the selector used when a protected MCP resource advertises multiple authorization servers.
    /// </summary>
    Func<IReadOnlyList<Uri>, Uri?>? CreateAuthServerSelector(MCPServerConfig server);

    /// <summary>
    /// Creates the selector used to filter or augment OAuth scopes before authorization.
    /// </summary>
    ScopeSelectorDelegate? CreateScopeSelector(MCPServerConfig server);

    /// <summary>
    /// Creates the callback used to persist dynamic client registration responses.
    /// </summary>
    Func<DynamicClientRegistrationResponse, CancellationToken, Task>? CreateDynamicClientRegistrationResponseDelegate(MCPServerConfig server);
}
