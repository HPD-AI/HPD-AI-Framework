namespace HPD.Agent.Middleware;

/// <summary>
/// Specifies the scope at which a middleware operates.
/// Used for filtering middleware execution based on context.
/// </summary>
public enum MiddlewareScope
{
    /// <summary>Middleware applies to all functions globally</summary>
    Global = 0,

    /// <summary>Middleware applies to all functions from a specific Toolkit</summary>
    Toolkit = 1,

    /// <summary>Middleware applies to skill container and functions called by a specific skill</summary>
    Skill = 2,

    /// <summary>Middleware applies to a specific function only</summary>
    Function = 3
}

/// <summary>
/// Internal metadata for scoped middleware registration.
/// Stores scope information attached to middleware instances.
/// </summary>
internal class MiddlewareScopeMetadata
{
    public MiddlewareScope Scope { get; init; } = MiddlewareScope.Global;
    public string? Target { get; init; } // Toolkit type name, skill name, or function name

    public MiddlewareScopeMetadata(MiddlewareScope scope, string? target = null)
    {
        Scope = scope;
        Target = target;
    }

    /// <summary>
    /// Determines if this middleware should apply to the given BeforeFunction context.
    /// </summary>
    public bool AppliesTo(BeforeFunctionContext context)
    {
        // Handle unknown functions (Function can be null)
        var functionName = context.Function?.Name;
        var toolName = context.ToolkitName;
        var skillName = context.SkillName;

        // Check if this is a skill container by looking at AdditionalProperties
        var isSkillContainer = context.Function?.AdditionalProperties?.ContainsKey("IsSkillContainer") == true &&
                               context.Function.AdditionalProperties["IsSkillContainer"] is true;

        return Scope switch
        {
            MiddlewareScope.Global => true,

            MiddlewareScope.Toolkit => !string.IsNullOrEmpty(toolName) &&
                                       string.Equals(Target, toolName, StringComparison.Ordinal),

            MiddlewareScope.Skill =>
                // Apply if this function IS the skill container itself
                (isSkillContainer && !string.IsNullOrEmpty(functionName) && string.Equals(Target, functionName, StringComparison.Ordinal)) ||
                // OR if this function is called FROM this skill (via mapping)
                (!string.IsNullOrEmpty(skillName) && string.Equals(Target, skillName, StringComparison.Ordinal)),

            MiddlewareScope.Function => !string.IsNullOrEmpty(functionName) && string.Equals(Target, functionName, StringComparison.Ordinal),

            _ => false
        };
    }

    /// <summary>
    /// Determines if this middleware should apply to the given AfterFunction context.
    /// </summary>
    public bool AppliesTo(AfterFunctionContext context)
    {
        // Handle unknown functions (Function can be null)
        var functionName = context.Function?.Name;
        var toolName = context.ToolkitName;
        var skillName = context.SkillName;

        // Check if this is a skill container by looking at AdditionalProperties
        var isSkillContainer = context.Function?.AdditionalProperties?.ContainsKey("IsSkillContainer") == true &&
                               context.Function.AdditionalProperties["IsSkillContainer"] is true;

        return Scope switch
        {
            MiddlewareScope.Global => true,

            MiddlewareScope.Toolkit => !string.IsNullOrEmpty(toolName) &&
                                       string.Equals(Target, toolName, StringComparison.Ordinal),

            MiddlewareScope.Skill =>
                // Apply if this function IS the skill container itself
                (isSkillContainer && !string.IsNullOrEmpty(functionName) && string.Equals(Target, functionName, StringComparison.Ordinal)) ||
                // OR if this function is called FROM this skill (via mapping)
                (!string.IsNullOrEmpty(skillName) && string.Equals(Target, skillName, StringComparison.Ordinal)),

            MiddlewareScope.Function => !string.IsNullOrEmpty(functionName) && string.Equals(Target, functionName, StringComparison.Ordinal),

            _ => false
        };
    }
}

/// <summary>
/// Extension methods for scoped middleware registration.
/// Provides fluent API for attaching scope metadata to middleware instances.
/// </summary>
public static class MiddlewareScopeExtensions
{
    // Internal dictionary to store scope metadata per middleware instance
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IAgentMiddleware, MiddlewareScopeMetadata>
        _scopeMetadata = new();

    /// <summary>
    /// Marks this middleware as global (applies to all functions).
    /// This is the default if no scope is specified.
    /// </summary>
    public static IAgentMiddleware AsGlobal(this IAgentMiddleware middleware)
    {
        _scopeMetadata.AddOrUpdate(middleware, new MiddlewareScopeMetadata(MiddlewareScope.Global));
        return middleware;
    }

    /// <summary>
    /// Marks this middleware as Toolkit-scoped (applies only to functions from the specified Toolkit).
    /// </summary>
    /// <param name="middleware">The middleware instance</param>
    /// <param name="toolTypeName">The Toolkit type name (e.g., "FileSystemToolkit")</param>
    public static IAgentMiddleware ForToolkit(this IAgentMiddleware middleware, string toolTypeName)
    {
        if (string.IsNullOrWhiteSpace(toolTypeName))
            throw new ArgumentException("Toolkit type name cannot be null or empty", nameof(toolTypeName));

        _scopeMetadata.AddOrUpdate(middleware, new MiddlewareScopeMetadata(MiddlewareScope.Toolkit, toolTypeName));
        return middleware;
    }

    /// <summary>
    /// Marks this middleware as skill-scoped (applies to skill container and functions called by the skill).
    /// </summary>
    /// <param name="middleware">The middleware instance</param>
    /// <param name="skillName">The skill name (e.g., "analyze_codebase")</param>
    public static IAgentMiddleware ForSkill(this IAgentMiddleware middleware, string skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
            throw new ArgumentException("Skill name cannot be null or empty", nameof(skillName));

        _scopeMetadata.AddOrUpdate(middleware, new MiddlewareScopeMetadata(MiddlewareScope.Skill, skillName));
        return middleware;
    }

    /// <summary>
    /// Marks this middleware as function-scoped (applies only to the specified function).
    /// </summary>
    /// <param name="middleware">The middleware instance</param>
    /// <param name="functionName">The function name (e.g., "ReadFile")</param>
    public static IAgentMiddleware ForFunction(this IAgentMiddleware middleware, string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            throw new ArgumentException("Function name cannot be null or empty", nameof(functionName));

        _scopeMetadata.AddOrUpdate(middleware, new MiddlewareScopeMetadata(MiddlewareScope.Function, functionName));
        return middleware;
    }

    /// <summary>
    /// Gets the scope metadata for a middleware instance.
    /// Returns global scope if no metadata is attached.
    /// </summary>
    internal static MiddlewareScopeMetadata GetScopeMetadata(this IAgentMiddleware middleware)
    {
        if (_scopeMetadata.TryGetValue(middleware, out var metadata))
            return metadata;

        // Default to global scope if no metadata is attached
        return new MiddlewareScopeMetadata(MiddlewareScope.Global);
    }

    /// <summary>
    /// Checks if the middleware should execute in the given BeforeFunction context.
    /// </summary>
    internal static bool ShouldExecute(this IAgentMiddleware middleware, BeforeFunctionContext context)
    {
        var metadata = middleware.GetScopeMetadata();
        return metadata.AppliesTo(context);
    }

    /// <summary>
    /// Checks if the middleware should execute in the given AfterFunction context.
    /// </summary>
    internal static bool ShouldExecute(this IAgentMiddleware middleware, AfterFunctionContext context)
    {
        var metadata = middleware.GetScopeMetadata();
        return metadata.AppliesTo(context);
    }
}
