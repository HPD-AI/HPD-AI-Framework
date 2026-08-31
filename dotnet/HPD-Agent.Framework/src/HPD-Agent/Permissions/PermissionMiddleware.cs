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
/// <item>Check if function has a run-config override, builder override, or [RequiresPermission] attribute</item>
/// <item>Check conversation-Collapsed stored permission (if available)</item>
/// <item>Check global stored permission (fallback)</item>
/// <item>If no stored permission, emit PermissionRequestEvent and wait for response</item>
/// </list>
///
/// <para><b>Request Session Events:</b></para>
/// <list type="bullet">
/// <item><see cref="PermissionRequestEvent"/>: Emitted to request user permission</item>
/// <item><see cref="PermissionResponseEvent"/>: Expected response from UI handler</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var agent = new AgentBuilder()
///     .WithMiddleware(new PermissionMiddleware(storage))
///     .Build();
/// </code>
/// </example>
public class PermissionMiddleware : IAgentPermissionMiddleware
{
    private readonly AgentConfig? _config;
    private readonly string _middlewareName;
    private readonly PermissionOverrideRegistry? _overrideRegistry;
    private readonly IReadOnlyList<IFunctionPermissionScopeResolver> _scopeResolvers;

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
        PermissionOverrideRegistry? overrideRegistry = null,
        IEnumerable<IFunctionPermissionScopeResolver>? scopeResolvers = null)
    {
        _config = config;
        _middlewareName = middlewareName ?? "PermissionMiddleware";
        _overrideRegistry = overrideRegistry;
        _scopeResolvers = scopeResolvers?.ToArray() ??
        [
            new BoundActionScopedPermissionResolver(),
            new ClientToolOperationPermissionScopeResolver()
        ];
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
        if (context.RunConfig.Security.Approval == AgentApprovalPolicy.AutoApprove)
            return;

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
            // Action-contracted functions are admitted only after canonical preparation has
            // produced ValidatedFunctionAction. Their per-call hook is the sole permission
            // authority; the batch hook must not derive a competing scope from raw arguments.
            if (function is HPDAIFunctionFactory.HPDAIFunction
                {
                    HPDOptions.OperationContract: not null
                })
            {
                continue;
            }
            if (!TryGetPermissionKey(
                    function,
                    functionName,
                    funcInfo.Arguments,
                    validatedAction: null,
                    out var permissionKey,
                    out _))
            {
                continue;
            }

            // Check if permission is required (run config + builder override + attribute)
            var attributeRequiresPermission = GetDeclaredPermissionRequirement(
                function,
                funcInfo.Arguments);

            var effectiveRequiresPermission = GetEffectivePermissionRequirement(
                context.RunConfig,
                functionName,
                permissionKey,
                attributeRequiresPermission);

            // No permission required - auto-approve
            if (!effectiveRequiresPermission)
            {
                batchState = batchState.RecordApproval(permissionKey);
                continue;
            }

            // Check individual permission using the same logic as BeforeFunctionAsync
            var permissionResult = await CheckSinglePermissionAsync(
                context,
                function,
                permissionKey,
                funcInfo.CallId,
                funcInfo.Arguments,
                cancellationToken).ConfigureAwait(false);

            if (permissionResult.IsApproved)
            {
                batchState = batchState.RecordApproval(permissionKey);
            }
            else
            {
                batchState = batchState.RecordDenial(
                    permissionKey,
                    permissionResult.DenialReason,
                    permissionResult.DeniedBehavior);
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
        if (context.RunConfig.Security.Approval == AgentApprovalPolicy.AutoApprove)
            return;

        var function = context.Function;
        
        // Guard against null function
        if (function == null)
            return;
        
        var functionName = function.Name;
        if (!TryGetPermissionKey(
                function,
                functionName,
                context.Arguments,
                context.InvocationMode?.ValidatedAction,
                out var permissionKey,
                out var resolutionError))
        {
            context.BlockExecution = true;
            context.OverrideResult = $"Client tool request rejected: {resolutionError}";
            return;
        }

        // Check if permission is required (run config + builder override + attribute)
        var attributeRequiresPermission = GetDeclaredPermissionRequirement(
            function,
            context.Arguments);

        var effectiveRequiresPermission = GetEffectivePermissionRequirement(
            context.RunConfig,
            functionName,
            permissionKey,
            attributeRequiresPermission);

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
        if (batchState.ApprovedFunctions.Contains(permissionKey))
        {
            return;
        }

        // If already denied in batch, block execution immediately
        if (batchState.DeniedFunctions.Contains(permissionKey))
        {
            context.BlockExecution = true;
            var denialReason = batchState.DenialReasons.GetValueOrDefault(
                permissionKey,
                "Permission denied in batch approval");
            context.OverrideResult = BlockedResult("denied", permissionKey, denialReason);
            var deniedBehavior = batchState.DenialBehaviors.GetValueOrDefault(
                permissionKey,
                PermissionDeniedBehavior.InterruptTurn);
            if (deniedBehavior == PermissionDeniedBehavior.InterruptTurn)
                await InterruptDeniedPermissionAsync(context, denialReason, cancellationToken).ConfigureAwait(false);
            return;
        }

        //
        // STORED PERMISSION LOOKUP (from MiddlewareState)
        //

        var permState = context.Analyze(s => s.MiddlewareState.PermissionPersistent());
        if (permState != null)
        {
            // Check for stored permission choice (session-scoped)
            var storedChoice = permState.GetPermission(permissionKey);

            // Apply stored choice if found
            if (storedChoice == PermissionChoice.AlwaysAllow)
            {
                // Record approval in batch state for parallel optimization
                var updatedBatchState = batchState.RecordApproval(permissionKey);
                context.UpdateState(s => s with
                {
                    MiddlewareState = s.MiddlewareState.WithBatchPermission(updatedBatchState)
                });

                // Approved via stored preference - allow execution
                return;
            }

            if (storedChoice == PermissionChoice.AlwaysDeny)
            {
                var denialReason = $"Execution of '{permissionKey}' was denied by a stored user preference.";

                // Record denial in batch state for parallel optimization
                var updatedBatchState = batchState.RecordDenial(permissionKey, denialReason);
                context.UpdateState(s => s with
                {
                    MiddlewareState = s.MiddlewareState.WithBatchPermission(updatedBatchState)
                });

                // Denied via stored preference - block execution
                context.BlockExecution = true;
                context.OverrideResult = BlockedResult("denied", permissionKey, denialReason);
                await InterruptDeniedPermissionAsync(context, denialReason, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        //
        // REQUEST PERMISSION VIA REQUEST SESSION
        //

        var permissionId = Guid.NewGuid().ToString();
        // Wait for response from external handler
        PermissionResponseEvent response;
        try
        {
            response = await context.Base.RequestAsync<PermissionRequestEvent, PermissionResponseEvent>(
                new PermissionRequestEvent(
                    permissionId,
                    _middlewareName,
                    permissionKey,
                    function.Description ?? "No description available",
                    callId,
                    context.Arguments != null ? new Dictionary<string, object?>(context.Arguments) : null))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            context.BlockExecution = true;
            const string timeoutReason = "Permission request timed out. Please respond to permission requests promptly.";
            context.OverrideResult = BlockedResult("timed_out", permissionKey, timeoutReason);
            await InterruptDeniedPermissionAsync(context, timeoutReason, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException)
        {
            context.BlockExecution = true;
            const string cancelledReason = "Permission request was cancelled.";
            context.OverrideResult = BlockedResult("cancelled", permissionKey, cancelledReason);
            await InterruptDeniedPermissionAsync(context, cancelledReason, cancellationToken).ConfigureAwait(false);
            return;
        }

        //     
        // PROCESS RESPONSE
        //     

        if (response.Approved)
        {
            // Store persistent choice if requested
            // Save permission choice to persistent state (if user chose to remember)
            if (response.Choice != PermissionChoice.Ask)
            {
                // Update both batch state AND persistent state atomically
                context.UpdateState(s =>
                {
                    var currentPermState = s.MiddlewareState.PermissionPersistent() ?? new();
                    var updatedPermState = currentPermState.WithPermission(permissionKey, response.Choice);
                    var updatedBatchState = batchState.RecordApproval(permissionKey);

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
                var updatedBatchState = batchState.RecordApproval(permissionKey);
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

            // Record denial in batch state (for parallel execution optimization)
            var updatedBatchState = batchState.RecordDenial(
                permissionKey,
                denialReason,
                response.DeniedBehavior);
            context.UpdateState(s => s with
            {
                MiddlewareState = s.MiddlewareState.WithBatchPermission(updatedBatchState)
            });

            // Block execution with denial reason
            context.BlockExecution = true;
            context.OverrideResult = BlockedResult("denied", permissionKey, denialReason);
            if (response.DeniedBehavior == PermissionDeniedBehavior.InterruptTurn)
                await InterruptDeniedPermissionAsync(context, denialReason, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BlockedResult(
        string outcome,
        string permissionKey,
        string? reason)
    {
        var safeReason = string.IsNullOrWhiteSpace(reason)
            ? "Permission denied by user."
            : reason.Trim();
        return $"<tool_permission outcome=\"{System.Security.SecurityElement.Escape(outcome)}\" " +
            $"permission=\"{System.Security.SecurityElement.Escape(permissionKey)}\" " +
            $"executed=\"false\">{System.Security.SecurityElement.Escape(safeReason)}</tool_permission>";
    }

    private static async Task InterruptDeniedPermissionAsync(
        BeforeFunctionContext context,
        string reason,
        CancellationToken cancellationToken)
    {
        context.EventFlows?.InterruptFlow(context.FunctionCallId);
        await context.PublishAsync(
                new InterruptionHandledEvent(
                    context.FunctionCallId,
                    reason,
                    InterruptionSource.Middleware),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Helper method that checks permission for a single function.
    /// Returns approval status and denial reason (if denied).
    /// Used by BeforeParallelBatchAsync to batch check permissions.
    /// </summary>
    private async Task<(bool IsApproved, string DenialReason, PermissionDeniedBehavior DeniedBehavior)> CheckSinglePermissionAsync(
        BeforeParallelBatchContext context,
        AIFunction function,
        string permissionKey,
        string callId,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        // Check stored permissions from MiddlewareState
        var permState = context.Analyze(s => s.MiddlewareState.PermissionPersistent());
        if (permState != null)
        {
            var storedChoice = permState.GetPermission(permissionKey);

            if (storedChoice == PermissionChoice.AlwaysAllow)
            {
                return (true, string.Empty, PermissionDeniedBehavior.InterruptTurn);
            }

            if (storedChoice == PermissionChoice.AlwaysDeny)
            {
                return (false, $"Execution of '{permissionKey}' was denied by a stored user preference.", PermissionDeniedBehavior.InterruptTurn);
            }
        }

        // Request permission via a request session
        var permissionId = Guid.NewGuid().ToString();
        // Wait for response from external handler
        PermissionResponseEvent response;
        try
        {
            response = await context.Base.RequestAsync<PermissionRequestEvent, PermissionResponseEvent>(
                new PermissionRequestEvent(
                    permissionId,
                    _middlewareName,
                    permissionKey,
                    function.Description ?? "No description available",
                    callId,
                    arguments != null ? new Dictionary<string, object?>(arguments) : null))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return (false, "Permission request timed out. Please respond to permission requests promptly.", PermissionDeniedBehavior.InterruptTurn);
        }
        catch (OperationCanceledException)
        {
            return (false, "Permission request was cancelled.", PermissionDeniedBehavior.InterruptTurn);
        }

        // Process response
        if (response.Approved)
        {
            // Store persistent choice if requested (AlwaysAllow or AlwaysDeny)
            if (response.Choice != PermissionChoice.Ask)
            {
                // Read state INSIDE UpdateState lambda for thread safety
                context.UpdateState(s =>
                {
                    var currentPermState = s.MiddlewareState.PermissionPersistent() ?? new();
                    var updatedPermState = currentPermState.WithPermission(permissionKey, response.Choice);

                    return s with
                    {
                        MiddlewareState = s.MiddlewareState.WithPermissionPersistent(updatedPermState)
                    };
                });
            }

            return (true, string.Empty, PermissionDeniedBehavior.InterruptTurn);
        }
        else
        {
            // Determine denial reason
            var denialReason = response.Reason
                ?? _config?.Messages?.PermissionDeniedDefault
                ?? "Permission denied by user.";

            return (false, denialReason, response.DeniedBehavior);
        }
    }

    private bool GetEffectivePermissionRequirement(
        AgentRunConfig runConfig,
        string functionName,
        string permissionKey,
        bool attributeRequiresPermission)
    {
        if (runConfig.Security.PermissionOverrides?.TryGetValue(permissionKey, out var scopedRunOverride) == true)
            return scopedRunOverride;

        if (runConfig.Security.PermissionOverrides?.TryGetValue(functionName, out var runOverride) == true)
            return runOverride;

        var scopedBuilderOverride = _overrideRegistry?.TryGetOverride(permissionKey);
        if (scopedBuilderOverride is not null)
            return scopedBuilderOverride.Value;

        return _overrideRegistry?.GetEffectivePermissionRequirement(
            functionName,
            attributeRequiresPermission)
            ?? attributeRequiresPermission;
    }

    private bool TryGetPermissionKey(
        AIFunction function,
        string functionName,
        IReadOnlyDictionary<string, object?>? arguments,
        ValidatedFunctionAction? validatedAction,
        out string permissionKey,
        out string? validationError)
    {
        if (validatedAction is not null)
        {
            permissionKey = $"{functionName}:{validatedAction.Action}";
            validationError = null;
            return true;
        }

        if (arguments is null)
        {
            permissionKey = functionName;
            validationError = null;
            return true;
        }

        try
        {
            foreach (var resolver in _scopeResolvers)
            {
                if (resolver.TryResolveScope(function, arguments, out var scope) &&
                    !string.IsNullOrWhiteSpace(scope))
                {
                    permissionKey = $"{functionName}:{scope}";
                    validationError = null;
                    return true;
                }
            }
        }
        catch (ArgumentException exception)
        {
            permissionKey = string.Empty;
            validationError = exception.Message;
            return false;
        }

        permissionKey = functionName;
        validationError = null;
        return true;
    }

    private static bool GetDeclaredPermissionRequirement(
        AIFunction function,
        IReadOnlyDictionary<string, object?> arguments)
    {
        if (function.AdditionalProperties?.TryGetValue(
                "ClientToolDefinition",
                out var value) == true &&
            value is HPD.Agent.ClientTools.ClientToolDefinition definition)
        {
            return definition.ResolveOperation(arguments)?.Policy.RequiresPermission ??
                HPD.Agent.ClientTools.ClientToolPolicy.Resolve(
                    definition.DefaultPolicy).RequiresPermission!.Value;
        }

        return function is HPDAIFunctionFactory.HPDAIFunction hpdFunction &&
            hpdFunction.HPDOptions.RequiresPermission;
    }
}
