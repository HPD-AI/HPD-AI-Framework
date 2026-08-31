using HPD.Agent.Middleware;

namespace HPD.Agent.Permissions;

/// <summary>
/// Marker contract for middleware that enforces agent permission policy.
/// </summary>
/// <remarks>
/// Permission metadata such as <c>[RequiresPermission]</c> describes a capability.
/// Implementations consume generated declarations, policy evaluations, interactions,
/// invocation grants, and versioned preference storage.
/// </remarks>
public interface IAgentPermissionMiddleware : IAgentMiddleware;
