using HPD.Agent.Middleware;

namespace HPD.Agent.Permissions;

/// <summary>
/// Marker contract for middleware that enforces agent permission policy.
/// </summary>
/// <remarks>
/// Permission metadata such as <c>[RequiresPermission]</c> describes a capability.
/// Implementations of this interface decide what that metadata means, how permission
/// requests are presented, and how grants are remembered in middleware state.
/// </remarks>
public interface IAgentPermissionMiddleware : IAgentMiddleware;

/// <summary>
/// Supplies a stable permission scope for a bound compound function argument.
/// The permission middleware uses the scope when storing and reusing decisions,
/// preventing approval for one operation in a union from authorizing another.
/// </summary>
public interface IActionScopedPermission
{
    string PermissionScope { get; }
}
