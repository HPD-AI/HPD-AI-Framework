// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent;

/// <summary>
/// Registers middleware owned by this ToolHarness at accepted-input admission,
/// independently of ToolHarness collapsing.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ToolHarnessMiddlewareAttribute : Attribute
{
    private readonly Type[] _middlewareTypes;

    /// <summary>Creates an eager ToolHarness middleware declaration.</summary>
    public ToolHarnessMiddlewareAttribute(params Type[] middlewareTypes)
    {
        if (middlewareTypes is not { Length: > 0 })
            throw new ArgumentException("At least one ToolHarness middleware type is required.", nameof(middlewareTypes));
        if (middlewareTypes.Any(static type => type is null))
            throw new ArgumentException("ToolHarness middleware types cannot contain null.", nameof(middlewareTypes));

        _middlewareTypes = (Type[])middlewareTypes.Clone();
    }

    /// <summary>Gets the ordered middleware types owned by the ToolHarness.</summary>
    public IReadOnlyList<Type> MiddlewareTypes => Array.AsReadOnly(_middlewareTypes);
}
