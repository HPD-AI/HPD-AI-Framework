using System;
using HPD.Agent;

/// <summary>Declares how an action overrides its containing function's invocation-mode policy.</summary>
public enum AIFunctionActionInvocationModePolicy
{
    /// <summary>Uses the policy declared by the containing function.</summary>
    Inherit,
    /// <summary>The action always completes before returning.</summary>
    SynchronousOnly,
    /// <summary>The action always runs as background work.</summary>
    BackgroundOnly,
    /// <summary>The model may choose synchronous or background execution for the action.</summary>
    ModelChoice
}

/// <summary>Declares how an action overrides its containing function's invocation-mode handling.</summary>
public enum AIFunctionActionInvocationModeHandling
{
    /// <summary>Uses the handling declared by the containing function.</summary>
    Inherit,
    /// <summary>The HPD runtime owns background registration.</summary>
    Runtime,
    /// <summary>The function body owns background registration.</summary>
    ToolBody
}

/// <summary>Declares how an action overrides its containing function's permission requirement.</summary>
public enum PermissionRequirement
{
    /// <summary>Uses the permission requirement declared by the containing function.</summary>
    Inherit,
    /// <summary>Requires permission for this action.</summary>
    Required,
    /// <summary>Does not require permission for this action unless configuration overrides it.</summary>
    NotRequired
}

/// <summary>Associates one closed-union discriminator with invocation-mode overrides.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AIFunctionActionAttribute : Attribute
{
    /// <summary>Initializes an action declaration.</summary>
    /// <param name="action">The exact serializer discriminator for the action.</param>
    public AIFunctionActionAttribute(string action) => Action = action;

    /// <summary>Gets the exact serializer discriminator for the action.</summary>
    public string Action { get; }

    /// <summary>Gets or sets the action's policy override.</summary>
    public AIFunctionActionInvocationModePolicy InvocationModePolicy { get; set; }
        = AIFunctionActionInvocationModePolicy.Inherit;

    /// <summary>Gets or sets the action's handling override.</summary>
    public AIFunctionActionInvocationModeHandling InvocationModeHandling { get; set; }
        = AIFunctionActionInvocationModeHandling.Inherit;

    /// <summary>Gets or sets this action's permission-requirement override.</summary>
    public PermissionRequirement Permission { get; set; }
        = PermissionRequirement.Inherit;

    /// <summary>Gets or sets an application-owned stable scope for this action.</summary>
    public string? PermissionScope { get; set; }

    /// <summary>Gets or sets the permission policy implementation for this action.</summary>
    public Type? PermissionPolicy { get; set; }

    /// <summary>Gets or sets the permission interaction implementation for this action.</summary>
    public Type? PermissionInteraction { get; set; }
}

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
    /// Used with structured output for typed responses.
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

    /// <summary>
    /// Defines whether this function runs synchronously, in the background, or lets the model choose per call.
    /// </summary>
    public AgentInvocationModePolicy InvocationModePolicy { get; set; } =
        AgentInvocationModePolicy.SynchronousOnly;

    /// <summary>
    /// Defines whether HPD runtime or the function body handles invocation mode.
    /// </summary>
    public AgentInvocationModeHandling InvocationModeHandling { get; set; } =
        AgentInvocationModeHandling.Runtime;

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

    /// <summary>
    /// Defines whether this function runs synchronously, in the background, or lets the model choose per call.
    /// </summary>
    public AgentInvocationModePolicy InvocationModePolicy { get; set; } =
        AgentInvocationModePolicy.SynchronousOnly;

    /// <summary>
    /// Defines whether HPD runtime or the function body handles invocation mode.
    /// </summary>
    public AgentInvocationModeHandling InvocationModeHandling { get; set; } =
        AgentInvocationModeHandling.Runtime;

}

