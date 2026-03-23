using System.Threading.Tasks;
using HPD.Agent;
using System.Collections.Concurrent;

public static class AgentBuilderPermissionExtensions
{
    /// <summary>
    /// Adds the unified permission Middleware that works with any protocol (Console, AGUI, Web, etc.).
    /// Permission requests are emitted as events that you handle in your application code.
    /// This gives you full control over how permission prompts are displayed to users.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The agent builder for chaining</returns>
    /// <example>
    /// // In your Program.cs:
    /// var agent = new AgentBuilder()
    ///     .WithPermissions()  // Permissions automatically persist in session
    ///     .Build();
    ///
    /// // Then handle events in your event loop (see Middleware_EVENTS_USAGE.md)
    /// </example>
    /// <remarks>
    /// Permission choices are automatically persisted in MiddlewareState
    /// (PermissionPersistentStateData) and saved to Session. No external
    /// storage is needed - permissions are session-scoped and persist across runs.
    /// </remarks>
    public static AgentBuilder WithPermissions(this AgentBuilder builder)
    {
        var middleware = new HPD.Agent.Permissions.PermissionMiddleware(
            config: builder.Config,
            overrideRegistry: builder._permissionOverrides);
        builder._middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds an auto-approve permission Middleware for testing and automation scenarios.
    /// </summary>
    public static AgentBuilder WithAutoApprovePermissions(this AgentBuilder builder)
    {
        var middleware = new HPD.Agent.Permissions.AutoApprovePermissionMiddleware();
        builder._middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Forces a specific function to require permission, overriding the [RequiresPermission] attribute.
    /// Useful when using third-party Toolkits that don't have permission checks on sensitive functions.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="functionName">The name of the function to require permission for</param>
    /// <returns>The agent builder for chaining</returns>
    /// <example>
    /// var agent = new AgentBuilder()
    ///     .WithTools&lt;ThirdPartyDatabaseToolkit&gt;()
    ///     .RequirePermissionFor("DeleteAllData")  // Force permission even if attribute says no
    ///     .Build();
    /// </example>
    public static AgentBuilder RequirePermissionFor(this AgentBuilder builder, string functionName)
    {
        builder._permissionOverrides.RequirePermission(functionName);
        return builder;
    }

    /// <summary>
    /// Disables permission requirement for a specific function, overriding the [RequiresPermission] attribute.
    /// Useful when you trust a function and don't want to be prompted.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="functionName">The name of the function to disable permission for</param>
    /// <returns>The agent builder for chaining</returns>
    /// <example>
    /// var agent = new AgentBuilder()
    ///     .WithTools&lt;FileSystemToolkit&gt;()
    ///     .DisablePermissionFor("ReadFile")  // Remove permission requirement
    ///     .Build();
    /// </example>
    public static AgentBuilder DisablePermissionFor(this AgentBuilder builder, string functionName)
    {
        builder._permissionOverrides.DisablePermission(functionName);
        return builder;
    }

    /// <summary>
    /// Clears any permission override for a function, restoring attribute-based behavior.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="functionName">The name of the function to clear override for</param>
    /// <returns>The agent builder for chaining</returns>
    public static AgentBuilder ClearPermissionOverride(this AgentBuilder builder, string functionName)
    {
        builder._permissionOverrides.ClearOverride(functionName);
        return builder;
    }
}
