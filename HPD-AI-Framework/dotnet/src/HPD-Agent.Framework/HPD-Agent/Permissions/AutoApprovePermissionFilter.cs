using System;
using HPD.Agent;
using HPD.Agent.Middleware;
using System.Threading.Tasks;

namespace HPD.Agent.Permissions;

/// <summary>
/// Auto-approve permission middleware for testing and automation scenarios.
/// Automatically approves all function executions that require permission.
/// </summary>
/// <remarks>
/// <para><b>Use Case:</b></para>
/// <para>
/// This middleware is useful for automated testing, CI/CD pipelines, or scenarios where
/// you want to bypass permission checks entirely. Simply don't block execution for any function.
/// </para>
///
/// <para><b>WARNING:</b></para>
/// <para>
/// Do NOT use this in production environments where user consent is required.
/// It bypasses ALL permission checks.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var agent = new AgentBuilder()
///     .WithMiddleware(new AutoApprovePermissionMiddleware())
///     .Build();
/// </code>
/// </example>
public class AutoApprovePermissionMiddleware : IAgentMiddleware
{
    /// <summary>
    /// Called before each function executes. Always allows execution (no permission check).
    /// </summary>
    public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
    {
        // Auto-approve all functions by doing nothing
        // (default behavior is to allow execution)
        return Task.CompletedTask;
    }
}