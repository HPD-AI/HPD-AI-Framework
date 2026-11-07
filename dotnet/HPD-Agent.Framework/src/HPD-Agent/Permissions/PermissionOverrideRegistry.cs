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
        return Resolve(_overrides.Select(pair => new PermissionOverride(pair.Key, pair.Value)), selector);

    }

    internal static bool? Resolve(IEnumerable<PermissionOverride>? overrides, PermissionOverrideSelector call)
    {
        var match = overrides?.Where(value => value.Selector.Matches(call))
            .OrderBy(value => value.Selector.Specificity).LastOrDefault();
        return match?.RequiresPermission;
    }

    internal void ValidateFunctions(IEnumerable<Microsoft.Extensions.AI.AIFunction> functions)
    {
        var declared = functions.OfType<HPDAIFunctionFactory.HPDAIFunction>().SelectMany(function =>
        {
            var selectors = new List<PermissionOverrideSelector>
            { new(function.Name, null, function.PermissionDeclaration?.Authority ?? $"function/{Uri.EscapeDataString(function.Name)}") };
            if (function.OperationContract is { } contract)
                selectors.AddRange(contract.Actions.Select(pair => new PermissionOverrideSelector(function.Name, pair.Key, pair.Value.Permission.Authority)));
            return selectors;
        }).ToArray();
        foreach (var selector in _overrides.Keys)
            if (!declared.Any(selector.Matches))
                throw new InvalidOperationException($"PermissionSelectorUnknown: {selector}");
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
public sealed record PermissionOverrideSelector(string? FunctionName = null, string? Action = null, string? Authority = null)
{
    internal int Specificity => Authority is not null ? Action is not null ? 3 : 2 : Action is not null ? 1 : 0;
    internal bool Matches(PermissionOverrideSelector call) =>
        (FunctionName is null || FunctionName == call.FunctionName) &&
        (Authority is null || Authority == call.Authority) &&
        (Action is null || Action == call.Action);

    internal void Validate()
    {
        if (FunctionName is null && Authority is null)
            throw new ArgumentException("A permission override needs an authority or function selector.");
        if (FunctionName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(FunctionName);
        if (Action is not null) ArgumentException.ThrowIfNullOrWhiteSpace(Action);
        if (Authority is not null) ArgumentException.ThrowIfNullOrWhiteSpace(Authority);
    }
}

/// <summary>Pairs one typed override selector with its required/not-required decision.</summary>
public sealed record PermissionOverride(PermissionOverrideSelector Selector, bool RequiresPermission);
