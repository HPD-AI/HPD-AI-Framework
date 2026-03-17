using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Permissions;

/// <summary>
/// Unified permission middleware that works with any protocol (Console, AGUI, Web, etc.).
/// Emits standardized permission events that can be handled by application-specific UI code.
/// </summary>
/// <remarks>
/// <para><b>How It Works:</b></para>
/// <para>
/// This middleware uses the <see cref="IAgentMiddleware.BeforeFunctionAsync"/> hook to check
/// permissions before each function executes. If a function requires permission and the user
/// hasn't granted it, the middleware blocks execution and sets the result to the denial reason.
/// </para>
///
/// <para><b>Permission Checking Order:</b></para>
/// <list type="number">
/// <item>Check if function has [RequiresPermission] attribute (or runtime override)</item>
/// <item>Check conversation-Collapsed stored permission (if available)</item>
/// <item>Check global stored permission (fallback)</item>
/// <item>If no stored permission, emit PermissionRequestEvent and wait for response</item>
/// </list>
///
/// <para><b>Bidirectional Events:</b></para>
/// <list type="bullet">
/// <item><see cref="PermissionRequestEvent"/>: Emitted to request user permission</item>
/// <item><see cref="PermissionResponseEvent"/>: Expected response from UI handler</item>
/// <item><see cref="PermissionApprovedEvent"/>: Emitted for observability when approved</item>
/// <item><see cref="PermissionDeniedEvent"/>: Emitted for observability when denied</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var agent = new AgentBuilder()
///     .WithMiddleware(new PermissionMiddleware(storage))
///     .Build();
/// </code>
/// </example>
public class PermissionMiddleware : IAgentMiddleware
{
    private readonly AgentConfig? _config;
    private readonly string _middlewareName;
    private readonly PermissionOverrideRegistry? _overrideRegistry;

    /// <summary>
    /// Creates a new permission middleware.
    /// </summary>
    /// <param name="config">Optional agent configuration for default messages</param>
    /// <param name="middlewareName">Optional name for this middleware instance (for event correlation)</param>
    /// <param name="overrideRegistry">Optional registry for runtime permission overrides</param>
    /// <remarks>
    /// Permission choices are automatically persisted in MiddlewareState
    /// (PermissionPersistentStateData) and saved to Session. No external
    /// storage is needed.
    /// </remarks>
    public PermissionMiddleware(
        AgentConfig? config = null,
        string? middlewareName = null,
        PermissionOverrideRegistry? overrideRegistry = null)
    {
        _config = config;
        _middlewareName = middlewareName ?? "PermissionMiddleware";
        _overrideRegistry = overrideRegistry;
    }

    /// <summary>
    /// Resets batch permission state at the start of each iteration.
    /// </summary>
    public Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        // Reset batch state for new iteration
        var newBatchState = new BatchPermissionStateData().Reset();
        context.UpdateState(s => s with
        {
            MiddlewareState = s.MiddlewareState.WithBatchPermission(newBatchState)
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles batch permission checking for parallel function execution.
    /// Mimics the old PermissionManager.CheckPermissionsAsync behavior:
    /// loops through each function and checks permission sequentially.
    /// Results are stored in BatchPermissionState for BeforeFunctionAsync to check.
    /// </summary>
    public async Task BeforeParallelBatchAsync(
        BeforeParallelBatchContext context,
        CancellationToken cancellationToken)
    {
        var parallelFunctions = context.ParallelFunctions;
        if (parallelFunctions == null || parallelFunctions.Count == 0)
            return;

        var batchState = context.Analyze(s =>
            s.MiddlewareState.BatchPermission() ?? new BatchPermissionStateData()
        );

        // Loop through each function and check permission individually
        // This matches the old PermissionManager.CheckPermissionsAsync behavior
        foreach (var funcInfo in parallelFunctions)
        {
            var function = funcInfo.Function;
            var functionName = funcInfo.FunctionName;

            // Check if permission is required (attribute + overrides)
            var attributeRequiresPermission = function is HPDAIFunctionFactory.HPDAIFunction hpdFunction
                && hpdFunction.HPDOptions.RequiresPermission;

            var effectiveRequiresPermission = _overrideRegistry?.GetEffectivePermissionRequirement(
                functionName, attributeRequiresPermission)
                ?? attributeRequiresPermission;

            // No permission required - auto-approve
            if (!effectiveRequiresPermission)
            {
                batchState = batchState.RecordApproval(functionName);
                continue;
            }

            // Check individual permission using the same logic as BeforeFunctionAsync
            var permissionResult = await CheckSinglePermissionAsync(
                context,
                function,
                functionName,
                funcInfo.CallId,
                funcInfo.Arguments,
                cancellationToken).ConfigureAwait(false);

            if (permissionResult.IsApproved)
            {
                batchState = batchState.RecordApproval(functionName);
            }
            else
            {
                batchState = batchState.RecordDenial(functionName, permissionResult.DenialReason);
            }
        }

        // Update state with all batch approvals/denials
        context.UpdateState(s => s with
        {
            MiddlewareState = s.MiddlewareState.WithBatchPermission(batchState)
        });
    }

    /// <summary>
    /// Checks permissions before a function executes.
    /// Blocks execution if permission is required but not granted.
    /// For parallel execution, checks batch state first to avoid duplicate permission requests.
    /// </summary>
    public async Task BeforeFunctionAsync(
        BeforeFunctionContext context,
        CancellationToken cancellationToken)
    {
        var function = context.Function;
        
        // Guard against null function
        if (function == null)
            return;
        
        var functionName = function.Name;

        // Check if permission is required (attribute + overrides)
        var attributeRequiresPermission = function is HPDAIFunctionFactory.HPDAIFunction hpdFunction
            && hpdFunction.HPDOptions.RequiresPermission;

        var effectiveRequiresPermission = _overrideRegistry?.GetEffectivePermissionRequirement(
            functionName, attributeRequiresPermission)
            ?? attributeRequiresPermission;

        // No permission required - allow execution
        if (!effectiveRequiresPermission)
            return;

        var conversationId = context.ConversationId;
        var callId = context.FunctionCallId;

        //     
        // CHECK BATCH PERMISSION STATE (for parallel execution optimization)
        //     

        var batchState = context.Analyze(s =>
            s.MiddlewareState.BatchPermission() ?? new BatchPermissionStateData()
        );

        // If already approved in batch, allow execution immediately
        if (batchState.ApprovedFunctions.Contains(functionName))
        {
            return;
        }

        // If already denied in batch, block execution immediately
        if (batchState.DeniedFunctions.Contains(functionName))
        {
            context.BlockExecution = true;
            context.OverrideResult = batchState.DenialReasons.GetValueOrDefault(
                functionName,
                "Permission denied in batch approval");
            return;
        }

        //
        // STORED PERMISSION LOOKUP (from MiddlewareState)
        //

        var permState = context.Analyze(s => s.MiddlewareState.PermissionPersistent());
        if (permState != null)
        {
            // Check for stored permission choice (session-scoped)
            var storedChoice = permState.GetPermission(functionName);

            // Apply stored choice if found
            if (storedChoice == PermissionChoice.AlwaysAllow)
            {
                // Record approval in batch state for parallel optimization
                var updatedBatchState = batchState.RecordApproval(functionName);
                context.UpdateState(s => s with
                {
                    MiddlewareState = s.MiddlewareState.WithBatchPermission(updatedBatchState)
                });

                // Approved via stored preference - allow execution
                return;
            }

            if (storedChoice == PermissionChoice.AlwaysDeny)
            {
                var denialReason = $"Execution of '{functionName}' was denied by a stored user preference.";

                // Record denial in batch state for parallel optimization
                var updatedBatchState = batchState.RecordDenial(functionName, denialReason);
                context.UpdateState(s => s with
                {
                    MiddlewareState = s.MiddlewareState.WithBatchPermission(updatedBatchState)
                });

                // Denied via stored preference - block execution
                context.BlockExecution = true;
                context.OverrideResult = denialReason;
                return;
            }
        }

        //     
        // REQUEST PERMISSION VIA BIDIRECTIONAL EVENTS
        //     

        var permissionId = Guid.NewGuid().ToString();

        // Emit permission request event
        context.Emit(new PermissionRequestEvent(
            permissionId,
            _middlewareName,
            functionName,
            function.Description ?? "No description available",
            callId,
            context.Arguments != null ? new Dictionary<string, object?>(context.Arguments) : null));

        // Wait for response from external handler
        PermissionResponseEvent response;
        try
        {
            response = await context.Base.WaitForResponseAsync<PermissionResponseEvent>(permissionId)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            context.Emit(new PermissionDeniedEvent(
                permissionId,
                _middlewareName,
                callId,
                "Permission request timed out after 5 minutes"));

            context.BlockExecution = true;
            context.OverrideResult = "Permission request timed out. Please respond to permission requests promptly.";
            return;
        }
        catch (OperationCanceledException)
        {
            context.Emit(new PermissionDeniedEvent(
                permissionId,
                _middlewareName,
                callId,
                "Permission request was cancelled"));

            context.BlockExecution = true;
            context.OverrideResult = "Permission request was cancelled.";
            return;
        }

        //     
        // PROCESS RESPONSE
        //     

        if (response.Approved)
        {
            // Emit approval event for observability
            context.Emit(new PermissionApprovedEvent(permissionId, _middlewareName));

            // Store persistent choice if requested
            // Save permission choice to persistent state (if user chose to remember)
            if (response.Choice != PermissionChoice.Ask)
            {
                // Update both batch state AND persistent state atomically
                context.UpdateState(s =>
                {
                    var currentPermState = s.MiddlewareState.PermissionPersistent() ?? new();
                    var updatedPermState = currentPermState.WithPermission(functionName, response.Choice);
                    var updatedBatchState = batchState.RecordApproval(functionName);

                    return s with
                    {
                        MiddlewareState = s.MiddlewareState
                            .WithBatchPermission(updatedBatchState)
                            .WithPermissionPersistent(updatedPermState)
                    };
                });
            }
            else
            {
                // Just update batch state (don't persist "Ask" choice)
                var updatedBatchState = batchState.RecordApproval(functionName);
                context.UpdateState(s => s with
                {
                    MiddlewareState = s.MiddlewareState.WithBatchPermission(updatedBatchState)
                });
            }

            // Allow execution (don't set BlockFunctionExecution)
        }
        else
        {
            // Determine denial reason
            var denialReason = response.Reason
                ?? _config?.Messages?.PermissionDeniedDefault
                ?? "Permission denied by user.";

            // Emit denial event for observability
            context.Emit(new PermissionDeniedEvent(
                permissionId,
                _middlewareName,
                callId,
                denialReason));

            // Record denial in batch state (for parallel execution optimization)
            var updatedBatchState = batchState.RecordDenial(functionName, denialReason);
            context.UpdateState(s => s with
            {
                MiddlewareState = s.MiddlewareState.WithBatchPermission(updatedBatchState)
            });

            // Block execution with denial reason
            context.BlockExecution = true;
            context.OverrideResult = denialReason;
        }
    }

    /// <summary>
    /// Helper method that checks permission for a single function.
    /// Returns approval status and denial reason (if denied).
    /// Used by BeforeParallelBatchAsync to batch check permissions.
    /// </summary>
    private async Task<(bool IsApproved, string DenialReason)> CheckSinglePermissionAsync(
        BeforeParallelBatchContext context,
        AIFunction function,
        string functionName,
        string callId,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        // Check stored permissions from MiddlewareState
        var permState = context.Analyze(s => s.MiddlewareState.PermissionPersistent());
        if (permState != null)
        {
            var storedChoice = permState.GetPermission(functionName);

            if (storedChoice == PermissionChoice.AlwaysAllow)
            {
                return (true, string.Empty);
            }

            if (storedChoice == PermissionChoice.AlwaysDeny)
            {
                return (false, $"Execution of '{functionName}' was denied by a stored user preference.");
            }
        }

        // Request permission via bidirectional events
        var permissionId = Guid.NewGuid().ToString();

        context.Emit(new PermissionRequestEvent(
            permissionId,
            _middlewareName,
            functionName,
            function.Description ?? "No description available",
            callId,
            arguments != null ? new Dictionary<string, object?>(arguments) : null));

        // Wait for response from external handler
        PermissionResponseEvent response;
        try
        {
            response = await context.Base.WaitForResponseAsync<PermissionResponseEvent>(permissionId)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            context.Emit(new PermissionDeniedEvent(
                permissionId,
                _middlewareName,
                callId,
                "Permission request timed out after 5 minutes"));

            return (false, "Permission request timed out. Please respond to permission requests promptly.");
        }
        catch (OperationCanceledException)
        {
            context.Emit(new PermissionDeniedEvent(
                permissionId,
                _middlewareName,
                callId,
                "Permission request was cancelled"));

            return (false, "Permission request was cancelled.");
        }

        // Process response
        if (response.Approved)
        {
            // Emit approval event for observability
            context.Emit(new PermissionApprovedEvent(permissionId, _middlewareName));

            // Store persistent choice if requested (AlwaysAllow or AlwaysDeny)
            if (response.Choice != PermissionChoice.Ask)
            {
                // Read state INSIDE UpdateState lambda for thread safety
                context.UpdateState(s =>
                {
                    var currentPermState = s.MiddlewareState.PermissionPersistent() ?? new();
                    var updatedPermState = currentPermState.WithPermission(functionName, response.Choice);

                    return s with
                    {
                        MiddlewareState = s.MiddlewareState.WithPermissionPersistent(updatedPermState)
                    };
                });
            }

            return (true, string.Empty);
        }
        else
        {
            // Determine denial reason
            var denialReason = response.Reason
                ?? _config?.Messages?.PermissionDeniedDefault
                ?? "Permission denied by user.";

            // Emit denial event for observability
            context.Emit(new PermissionDeniedEvent(
                permissionId,
                _middlewareName,
                callId,
                denialReason));

            return (false, denialReason);
        }
    }
}
