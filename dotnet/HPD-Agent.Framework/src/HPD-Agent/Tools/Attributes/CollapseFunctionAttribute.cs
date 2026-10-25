// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent;

/// <summary>
/// Hides one generated AI function behind a zero-argument activation function until
/// the activation is called for the current message turn.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CollapseFunctionAttribute : Attribute
{
    /// <summary>Creates a function-collapsing declaration.</summary>
    /// <param name="description">Description that explains when to activate the function.</param>
    /// <remarks>The activation uses the original function's model-facing name.</remarks>
    public CollapseFunctionAttribute(string description)
        : this(null, description)
    {
    }

    /// <summary>Creates a function-collapsing declaration with an explicit activation name.</summary>
    /// <param name="containerName">Model-facing activation function name.</param>
    /// <param name="description">Description that explains when to activate the function.</param>
    public CollapseFunctionAttribute(string? containerName, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ContainerName = containerName;
        Description = description;
    }

    /// <summary>
    /// Gets the optional model-facing activation name. Null uses the original
    /// function's model-facing name.
    /// </summary>
    public string? ContainerName { get; }

    /// <summary>Gets the activation function description.</summary>
    public string Description { get; }

    /// <summary>Gets or sets instructions returned by a successful activation.</summary>
    public string? FunctionResult { get; set; }

    /// <summary>Gets or sets instructions injected while the function is active.</summary>
    public string? SystemPrompt { get; set; }
}
