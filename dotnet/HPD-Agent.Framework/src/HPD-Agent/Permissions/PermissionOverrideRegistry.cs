using System.Collections.Concurrent;
namespace HPD.Agent.Permissions;

/// <summary>
/// Registry for overriding permission requirements at runtime.
/// Allows users to force or disable permission checks for specific functions,
/// regardless of the [RequiresPermission] attribute value.
/// </summary>
public class PermissionOverrideRegistry
{
    private readonly ConcurrentDictionary<PermissionOverrideSelector, bool> _overrides = new();

    /// <summary>
    /// Forces a function to require permission, overriding its attribute.
    /// </summary>
    /// <param name="functionName">The name of the function</param>
    public void RequirePermission(string functionName)
    {
        Set(new PermissionOverrideSelector(functionName), true);
    }

    /// <summary>
    /// Forces a function to NOT require permission, overriding its attribute.
    /// </summary>
    /// <param name="functionName">The name of the function</param>
    public void DisablePermission(string functionName)
    {
        Set(new PermissionOverrideSelector(functionName), false);
    }

    /// <summary>
    /// Removes any override for a function, restoring attribute-based behavior.
    /// </summary>
    /// <param name="functionName">The name of the function</param>
    public void ClearOverride(string functionName)
    {
        _overrides.TryRemove(new PermissionOverrideSelector(functionName), out _);
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
        if (_overrides.TryGetValue(new PermissionOverrideSelector(functionName), out var overrideValue))
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
        return _overrides.ContainsKey(new PermissionOverrideSelector(functionName));
    }

    /// <summary>Gets an override only when one was explicitly registered.</summary>
    public bool? TryGetOverride(string functionName)
    {
        return _overrides.TryGetValue(new PermissionOverrideSelector(functionName), out var value)
            ? value
            : null;
    }

    /// <summary>Sets an exact typed function/action/authority override.</summary>
    public void Set(PermissionOverrideSelector selector, bool requiresPermission)
    {
        ArgumentNullException.ThrowIfNull(selector);
        selector.Validate();
        _overrides[selector] = requiresPermission;
    }

    /// <summary>Gets an exact override, then falls back to the function selector.</summary>
    public bool? Resolve(PermissionOverrideSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        selector.Validate();
        if (_overrides.TryGetValue(selector, out var exact)) return exact;
        return _overrides.TryGetValue(new PermissionOverrideSelector(selector.FunctionName), out var fallback)
            ? fallback
            : null;
    }

    /// <summary>
    /// Clears all permission overrides.
    /// </summary>
    public void ClearAll()
    {
        _overrides.Clear();
    }
}

/// <summary>Identifies one generated permission declaration without concatenated security keys.</summary>
public sealed record PermissionOverrideSelector(string FunctionName, string? Action = null, string? Authority = null)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FunctionName);
        if (Action is not null) ArgumentException.ThrowIfNullOrWhiteSpace(Action);
        if (Authority is not null) ArgumentException.ThrowIfNullOrWhiteSpace(Authority);
    }
}

/// <summary>Pairs one typed override selector with its required/not-required decision.</summary>
public sealed record PermissionOverride(PermissionOverrideSelector Selector, bool RequiresPermission);
