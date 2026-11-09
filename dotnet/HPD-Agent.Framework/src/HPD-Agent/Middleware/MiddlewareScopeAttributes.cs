using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Specifies the scope at which a middleware operates.
/// Used for filtering middleware execution based on context.
/// </summary>
public enum MiddlewareScope
{
    /// <summary>Middleware applies to all functions globally</summary>
    Global = 0,

    /// <summary>Middleware applies to all functions from a specific ToolHarness</summary>
    ToolHarness = 1,

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
    public string? Target { get; init; } // ToolHarness type name, skill name, or function name

    public MiddlewareScopeMetadata(MiddlewareScope scope, string? target = null)
    {
        Scope = scope;
        Target = target;
    }

    /// <summary>
    /// Determines if this middleware should apply to the given BeforeFunction context.
    /// </summary>
    public bool AppliesTo(BeforeFunctionContext context)
        => AppliesTo(
            context.Function,
            context.ToolHarnessName,
            context.SkillName,
            context.Base.State);

    /// <summary>
    /// Determines if this middleware should apply to the given AfterFunction context.
    /// </summary>
    public bool AppliesTo(AfterFunctionContext context)
        => AppliesTo(
            context.Function,
            context.ToolHarnessName,
            context.SkillName,
            context.Base.State);

    /// <summary>
    /// Determines if this middleware should apply to the given function request.
    /// </summary>
    internal bool AppliesTo(FunctionRequest request)
        => AppliesTo(
            request.Function,
            request.ToolHarnessName,
            request.SkillName,
            request.State);

    private bool AppliesTo(
        AIFunction? function,
        string? toolName,
        string? skillName,
        AgentLoopState state)
    {
        var functionName = function?.Name;
        return Scope switch
        {
            MiddlewareScope.Global => true,
            MiddlewareScope.ToolHarness => !string.IsNullOrEmpty(toolName) &&
                string.Equals(Target, toolName, StringComparison.Ordinal),
            MiddlewareScope.Skill => IsInSkillScope(function, skillName, state),
            MiddlewareScope.Function => !string.IsNullOrEmpty(functionName) &&
                string.Equals(Target, functionName, StringComparison.Ordinal),
            _ => false
        };
    }

    private bool IsInSkillScope(
        AIFunction? function,
        string? skillName,
        AgentLoopState state)
    {
        if (function is null || string.IsNullOrEmpty(Target))
            return false;

        if (!string.IsNullOrEmpty(skillName) &&
            string.Equals(Target, skillName, StringComparison.Ordinal))
        {
            return true;
        }

        var capability = GetCapabilityMetadata(function);
        if (capability?.Kind == HPDCapabilityKind.SkillActivation &&
            string.Equals(Target, function.Name, StringComparison.Ordinal))
        {
            return true;
        }

        if (capability is null)
            return false;

        var containerState = state.MiddlewareState.GetState<ContainerMiddlewareState>(
            typeof(ContainerMiddlewareState).FullName!);
        if (containerState is null || !containerState.ExpandedContainers.Contains(Target))
            return false;

        var activeSkill = containerState.TurnCapabilityFunctions
            .Select(GetCapabilityMetadata)
            .FirstOrDefault(metadata =>
                metadata is { Kind: HPDCapabilityKind.SkillActivation } &&
                string.Equals(metadata.OwnerHarnessIdentity, Target, StringComparison.Ordinal));

        // Generated skill activation functions are named for the skill even when their owner
        // identity stays on the generated harness.
        if (activeSkill is null)
        {
            var activation = containerState.TurnCapabilityFunctions.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, Target, StringComparison.Ordinal));
            activeSkill = GetCapabilityMetadata(activation);
        }

        return activeSkill is { Kind: HPDCapabilityKind.SkillActivation } &&
            activeSkill.Reveals.Contains(capability.Id);
    }

    private static HPDCapabilityMetadata? GetCapabilityMetadata(AIFunction? function)
        => function?.AdditionalProperties?.TryGetValue(
                HPDCapabilityMetadata.AdditionalPropertiesKey,
                out var value) == true &&
            value is HPDCapabilityMetadata metadata
                ? metadata
                : null;
}

/// <summary>
/// Extension methods for scoped middleware registration.
/// Provides fluent API for attaching scope metadata to middleware instances.
/// </summary>
/// <remarks>
/// Scope filtering applies to <see cref="IAgentMiddleware.BeforeFunctionAsync"/>,
/// <see cref="IAgentMiddleware.WrapFunctionCallAsync"/>, and
/// <see cref="IAgentMiddleware.AfterFunctionAsync"/>. Model- and turn-level hooks remain
/// global because they do not target one function invocation.
/// </remarks>
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
    /// Marks this middleware as ToolHarness-scoped (applies only to functions from the specified ToolHarness).
    /// </summary>
    /// <param name="middleware">The middleware instance</param>
    /// <param name="toolTypeName">The ToolHarness type name (e.g., "FileSystemToolHarness")</param>
    public static IAgentMiddleware ForToolHarness(this IAgentMiddleware middleware, string toolTypeName)
    {
        if (string.IsNullOrWhiteSpace(toolTypeName))
            throw new ArgumentException("ToolHarness type name cannot be null or empty", nameof(toolTypeName));

        _scopeMetadata.AddOrUpdate(middleware, new MiddlewareScopeMetadata(MiddlewareScope.ToolHarness, toolTypeName));
        return middleware;
    }

    /// <summary>
    /// Marks this middleware as skill-scoped (applies to skill container and functions called by the skill).
    /// </summary>
    /// <remarks>
    /// Referenced-function matching uses the skill's typed capability graph and the
    /// active expanded-container state. It does not depend on legacy
    /// <c>IsSkillContainer</c> metadata.
    /// </remarks>
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

    /// <summary>
    /// Checks if the middleware should execute for the given function request.
    /// </summary>
    internal static bool ShouldExecute(this IAgentMiddleware middleware, FunctionRequest request)
    {
        var metadata = middleware.GetScopeMetadata();
        return metadata.AppliesTo(request);
    }
}
