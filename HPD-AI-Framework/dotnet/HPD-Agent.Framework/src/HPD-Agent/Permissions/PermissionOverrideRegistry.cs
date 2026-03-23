using System.Collections.Concurrent;
namespace HPD.Agent.Permissions;

/// <summary>
/// Registry for overriding permission requirements at runtime.
/// Allows users to force or disable permission checks for specific functions,
/// regardless of the [RequiresPermission] attribute value.
/// </summary>
public class PermissionOverrideRegistry
{
    private readonly ConcurrentDictionary<string, bool> _functionOverrides = new();

    /// <summary>
    /// Forces a function to require permission, overriding its attribute.
    /// </summary>
    /// <param name="functionName">The name of the function</param>
    public void RequirePermission(string functionName)
    {
        _functionOverrides[functionName] = true;
    }

    /// <summary>
    /// Forces a function to NOT require permission, overriding its attribute.
    /// </summary>
    /// <param name="functionName">The name of the function</param>
    public void DisablePermission(string functionName)
    {
        _functionOverrides[functionName] = false;
    }

    /// <summary>
    /// Removes any override for a function, restoring attribute-based behavior.
    /// </summary>
    /// <param name="functionName">The name of the function</param>
    public void ClearOverride(string functionName)
    {
        _functionOverrides.TryRemove(functionName, out _);
    }

    /// <summary>
    /// Gets the effective permission requirement for a function.
    /// Returns override if present, otherwise returns the attribute value.
    /// </summary>
    /// <param name="functionName">The name of the function</param>
    /// <param name="attributeValue">The value from [RequiresPermission] attribute</param>
    /// <returns>True if permission is required, false otherwise</returns>
    public bool GetEffectivePermissionRequirement(string functionName, bool attributeValue)
    {
        // Override takes precedence over attribute
        if (_functionOverrides.TryGetValue(functionName, out var overrideValue))
        {
            return overrideValue;
        }

        // No override, use attribute value
        return attributeValue;
    }

    /// <summary>
    /// Checks if a function has a permission override registered.
    /// </summary>
    public bool HasOverride(string functionName)
    {
        return _functionOverrides.ContainsKey(functionName);
    }

    /// <summary>
    /// Clears all permission overrides.
    /// </summary>
    public void ClearAll()
    {
        _functionOverrides.Clear();
    }
}
