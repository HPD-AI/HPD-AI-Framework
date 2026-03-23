using System;
using HPD.Agent;

/// <summary>
/// Specifies the kind of tool a function represents.
/// </summary>
public enum ToolKind
{
    /// <summary>
    /// Regular tool - executed, result returned to LLM.
    /// </summary>
    Function = 0,

    /// <summary>
    /// Output tool - calling terminates the agent run.
    /// The tool's arguments ARE the structured output, and the tool is never executed.
    /// Used with RunStructuredAsync&lt;T&gt;() for typed responses.
    /// </summary>
    Output = 1
}

/// <summary>
/// Marks a method as an AI function with a specific context type.
/// The generic version enables compile-time validation and is required for conditional logic or dynamic descriptions.
/// </summary>
/// <typeparam name="TMetadata">The context type providing properties for conditions and templates</typeparam>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AIFunctionAttribute<TMetadata> : Attribute where TMetadata : IToolMetadata
{
    /// <summary>
    /// The context type used by this function for compile-time validation.
    /// </summary>
    public Type ContextType => typeof(TMetadata);

    /// <summary>
    /// Custom name for the function. If not specified, uses the method name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// The kind of tool this function represents. Default: Function.
    /// Set to Output for structured output tools.
    /// </summary>
    public ToolKind Kind { get; set; } = ToolKind.Function;
}

/// <summary>
/// Non-generic version for simple functions without conditional logic or dynamic descriptions.
/// Use AIFunction&lt;TMetadata&gt; for advanced features.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AIFunctionAttribute : Attribute
{
    /// <summary>
    /// Custom name for the function. If not specified, uses the method name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Static description of the function. For dynamic descriptions, use AIDescription with AIFunction&lt;TMetadata&gt;.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The kind of tool this function represents. Default: Function.
    /// Set to Output for structured output tools.
    /// </summary>
    public ToolKind Kind { get; set; } = ToolKind.Function;
}

