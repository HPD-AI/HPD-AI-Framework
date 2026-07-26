using HPD.Agent.ClientTools;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Permissions;

/// <summary>Resolves a permission scope from a function and its bound arguments.</summary>
public interface IFunctionPermissionScopeResolver
{
    bool TryResolveScope(
        AIFunction function,
        IReadOnlyDictionary<string, object?> arguments,
        out string scope);
}

/// <summary>Preserves action scope supplied by bound CLR request contracts.</summary>
public sealed class BoundActionScopedPermissionResolver : IFunctionPermissionScopeResolver
{
    public bool TryResolveScope(
        AIFunction function,
        IReadOnlyDictionary<string, object?> arguments,
        out string scope)
    {
        foreach (var argument in arguments.Values)
        {
            if (argument is IActionScopedPermission scoped &&
                !string.IsNullOrWhiteSpace(scoped.PermissionScope))
            {
                scope = scoped.PermissionScope;
                return true;
            }
        }

        scope = string.Empty;
        return false;
    }
}

/// <summary>Resolves action scope from an external compound client-tool contract.</summary>
public sealed class ClientToolOperationPermissionScopeResolver : IFunctionPermissionScopeResolver
{
    public bool TryResolveScope(
        AIFunction function,
        IReadOnlyDictionary<string, object?> arguments,
        out string scope)
    {
        if (function.AdditionalProperties?.TryGetValue(
                "ClientToolDefinition",
                out var value) != true ||
            value is not ClientToolDefinition { OperationContract: not null } definition)
        {
            scope = string.Empty;
            return false;
        }

        var operation = definition.ResolveOperation(arguments)
            ?? throw new InvalidOperationException("Compound operation could not be resolved.");
        scope = operation.Policy.PermissionScope ?? operation.Action;
        return true;
    }
}
