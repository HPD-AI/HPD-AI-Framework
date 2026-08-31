using HPD.Agent.Middleware;
using HPD.Agent.Permissions;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HPD.Agent.Serialization;

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
    // Batch mediation is owned by one AgentContext (one RunAsync execution). A middleware
    // instance may be shared by concurrent runs, so provider call IDs are not globally unique.
    private readonly ConditionalWeakTable<AgentContext, ConcurrentDictionary<string, BatchPermissionOutcome>>
        _batchOutcomes = new();

    /// <summary>
    /// Creates a new permission middleware.
    /// </summary>
    /// <param name="config">Optional agent configuration for default messages</param>
    /// <param name="middlewareName">Optional name for this middleware instance (for event correlation)</param>
    /// <param name="overrideRegistry">Optional registry for runtime permission overrides</param>
    /// <remarks>
    /// Persistent choices require a session store implementing
    /// <see cref="IPermissionPreferenceStore"/>.
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

    /// <summary>Performs no persisted-state mutation at iteration start.</summary>
    public Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
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

        // Loop through each function and check permission individually
        // This matches the old PermissionManager.CheckPermissionsAsync behavior
        foreach (var funcInfo in parallelFunctions)
        {
            var function = funcInfo.Function;
            var functionName = funcInfo.FunctionName;
            if (!TryGetPermissionKey(
                    function,
                    functionName,
                    funcInfo.Arguments,
                    funcInfo.ResolvedInvocation?.ValidatedAction,
                    out var permissionKey,
                    out _))
            {
                continue;
            }

            // Check if permission is required (run config + builder override + attribute)
            var attributeRequiresPermission = GetDeclaredPermissionRequirement(
                function,
                funcInfo.Arguments,
                funcInfo.ResolvedInvocation?.ValidatedAction);

            var effectiveRequiresPermission = GetEffectivePermissionRequirement(
                context.RunConfig,
                functionName,
                permissionKey,
                attributeRequiresPermission,
                funcInfo.ResolvedInvocation?.Action);

            // No permission required - auto-approve
            if (!effectiveRequiresPermission)
            {
                continue;
            }

            // Check individual permission using the same logic as BeforeFunctionAsync
            var permissionResult = await CheckSinglePermissionAsync(
                context,
                function,
                permissionKey,
                funcInfo.CallId,
                funcInfo.Arguments,
                funcInfo.ResolvedInvocation,
                cancellationToken).ConfigureAwait(false);

            GetBatchOutcomes(context.Base)[funcInfo.CallId] = new BatchPermissionOutcome(
                permissionKey,
                permissionResult.IsApproved,
                permissionResult.DenialReason,
                permissionResult.DeniedBehavior,
                permissionResult.ChoiceId,
                permissionResult.PermissionId,
                permissionResult.Source,
                permissionResult.Evaluation,
                CanonicalizeArguments(funcInfo.Arguments));
        }
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
            context.Arguments,
            context.InvocationMode?.ValidatedAction);

        var effectiveRequiresPermission = GetEffectivePermissionRequirement(
            context.RunConfig,
            functionName,
            permissionKey,
            attributeRequiresPermission,
            context.InvocationMode?.Action);

        // No permission required - allow execution
        if (!effectiveRequiresPermission)
            return;

        var evaluated = await EvaluatePermissionAsync(
            function,
            permissionKey,
            context.InvocationMode,
            context.Arguments,
            context.FunctionCallId,
            context.RunConfig,
            context.Services,
            cancellationToken).ConfigureAwait(false);
        var evaluation = RestrictPersistence(
            evaluated.Envelope,
            context.Session?.Store is IPermissionPreferenceStore &&
            context.Base.ThreadEvents is not null &&
            context.SessionId is not null && context.ThreadId is not null);
        await context.PublishAsync(new PermissionEvaluatedEvent(
            context.FunctionCallId, evaluation.Key, evaluation.Risk, evaluation.RequestFingerprint), cancellationToken).ConfigureAwait(false);

        if (context.RunConfig.Security.Approval == AgentApprovalPolicy.AutoApprove)
        {
            context.PermissionGrant = CreateGrant(
                context,
                function,
                permissionKey,
                PermissionGrantSource.HostAutoApprove,
                choiceId: "host_auto_approve",
                evaluation: evaluation);
            await PublishGrantAsync(context, context.PermissionGrant, cancellationToken).ConfigureAwait(false);
            return;
        }

        var conversationId = context.ConversationId;
        var callId = context.FunctionCallId;

        //     
        // CHECK BATCH PERMISSION STATE (for parallel execution optimization)
        //     

        if (GetBatchOutcomes(context.Base).TryRemove(callId, out var batchOutcome))
        {
            if (!string.Equals(batchOutcome.PermissionKey, permissionKey, StringComparison.Ordinal) ||
                !JsonElement.DeepEquals(batchOutcome.CanonicalArguments, CanonicalizeArguments(context.Arguments)))
                throw new InvalidOperationException("permission_authority_drift: prepared batch authority no longer matches admission.");
            if (batchOutcome.IsApproved)
            {
                context.PermissionGrant = CreateGrant(
                    context, function, permissionKey, batchOutcome.Source,
                    batchOutcome.ChoiceId, batchOutcome.PermissionId, batchOutcome.Evaluation);
                await PublishGrantAsync(context, context.PermissionGrant, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                context.BlockExecution = true;
                context.OverrideResult = BlockedResult("denied", permissionKey, batchOutcome.DenialReason);
                await context.PublishAsync(new PermissionDeniedEvent(
                    callId, evaluation.Key, batchOutcome.ChoiceId, batchOutcome.DenialReason), cancellationToken).ConfigureAwait(false);
                if (batchOutcome.DeniedBehavior == PermissionDeniedBehavior.InterruptTurn)
                    await InterruptDeniedPermissionAsync(context, batchOutcome.DenialReason, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        var storedPreference = await FindStoredPreferenceAsync(
            context.Session?.Store,
            context.SessionId,
            evaluated with { Envelope = evaluation },
            cancellationToken).ConfigureAwait(false);
        if (storedPreference is not null)
        {
            if (storedPreference.Decision == PermissionDecisionKind.Allow)
            {
                context.PermissionGrant = CreateGrant(
                    context,
                    function,
                    permissionKey,
                    PermissionGrantSource.StoredPreference,
                    choiceId: "always_allow",
                    evaluation: evaluation);
                await PublishGrantAsync(context, context.PermissionGrant, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (storedPreference.Decision == PermissionDecisionKind.Deny)
            {
                var denialReason = $"Execution of '{permissionKey}' was denied by a stored user preference.";
                context.BlockExecution = true;
                context.OverrideResult = BlockedResult("denied", permissionKey, denialReason);
                await context.PublishAsync(new PermissionDeniedEvent(
                    callId, evaluation.Key, "always_deny", denialReason), cancellationToken).ConfigureAwait(false);
                await InterruptDeniedPermissionAsync(context, denialReason, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        //
        // REQUEST PERMISSION VIA REQUEST SESSION
        //

        var permissionId = Guid.NewGuid().ToString();
        await context.PublishAsync(new PermissionRequestedAuditEvent(
            permissionId, callId, evaluation.Key), cancellationToken).ConfigureAwait(false);
        // Wait for response from external handler
        PermissionResponseEvent response;
        try
        {
            response = await RequestDecisionAsync(
                context.Base,
                function,
                context.InvocationMode?.Action,
                callId,
                permissionId,
                evaluation,
                evaluated.LiveEvaluation,
                context.Services,
                cancellationToken).ConfigureAwait(false);
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

        var selectedChoice = ResolveChoice(evaluation, response);
        await context.PublishAsync(new PermissionDecidedEvent(
            permissionId, callId, evaluation.Key, selectedChoice.Decision, selectedChoice.Id), cancellationToken).ConfigureAwait(false);
        if (selectedChoice.Decision == PermissionDecisionKind.Allow)
        {
            context.PermissionGrant = CreateGrant(
                context,
                function,
                permissionKey,
                PermissionGrantSource.UserDecision,
                choiceId: selectedChoice.Id,
                permissionId,
                evaluation);
            await PublishGrantAsync(context, context.PermissionGrant, cancellationToken).ConfigureAwait(false);
            if (selectedChoice.Persistence is not null)
                await PersistPreferenceAsync(
                    context.Session?.Store,
                    context.SessionId,
                    context.ThreadId,
                    context.Base.ThreadEvents,
                    evaluation,
                    selectedChoice,
                    permissionId,
                    cancellationToken).ConfigureAwait(false);
            // Allow execution (don't set BlockFunctionExecution)
        }
        else
        {
            // Determine denial reason
            var denialReason = response.Feedback
                ?? _config?.Messages?.PermissionDeniedDefault
                ?? "Permission denied by user.";

            // Block execution with denial reason
            context.BlockExecution = true;
            context.OverrideResult = BlockedResult("denied", permissionKey, denialReason);
            await context.PublishAsync(new PermissionDeniedEvent(
                callId, evaluation.Key, selectedChoice.Id, denialReason), cancellationToken).ConfigureAwait(false);
            if (selectedChoice.DeniedBehavior == PermissionDeniedBehavior.InterruptTurn)
                await InterruptDeniedPermissionAsync(context, denialReason, cancellationToken).ConfigureAwait(false);
            if (selectedChoice.Persistence is not null)
                await PersistPreferenceAsync(
                    context.Session?.Store,
                    context.SessionId,
                    context.ThreadId,
                    context.Base.ThreadEvents,
                    evaluation,
                    selectedChoice,
                    permissionId,
                    cancellationToken).ConfigureAwait(false);
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

    private static async ValueTask PublishGrantAsync(
        BeforeFunctionContext context,
        FunctionPermissionGrant grant,
        CancellationToken cancellationToken) =>
        _ = await context.PublishAsync(new PermissionGrantIssuedEvent(
            grant.FunctionCallId, grant.Key, grant.Source, grant.ChoiceId), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Helper method that checks permission for a single function.
    /// Returns approval status and denial reason (if denied).
    /// Used by BeforeParallelBatchAsync to batch check permissions.
    /// </summary>
    private async Task<(bool IsApproved, string DenialReason, PermissionDeniedBehavior DeniedBehavior,
        string ChoiceId, string? PermissionId, PermissionGrantSource Source, PermissionEvaluationEnvelope Evaluation)> CheckSinglePermissionAsync(
        BeforeParallelBatchContext context,
        AIFunction function,
        string permissionKey,
        string callId,
        IReadOnlyDictionary<string, object?> arguments,
        ResolvedFunctionInvocation? invocation,
        CancellationToken cancellationToken)
    {
        var evaluated = await EvaluatePermissionAsync(
            function,
            permissionKey,
            invocation,
            arguments,
            callId,
            context.RunConfig,
            context.Services,
            cancellationToken).ConfigureAwait(false);
        var evaluation = RestrictPersistence(
            evaluated.Envelope,
            context.Session?.Store is IPermissionPreferenceStore &&
            context.Base.ThreadEvents is not null &&
            context.SessionId is not null && context.ThreadId is not null);
        var storedPreference = await FindStoredPreferenceAsync(
            context.Session?.Store,
            context.SessionId,
            evaluated with { Envelope = evaluation },
            cancellationToken).ConfigureAwait(false);
        if (storedPreference is not null)
        {
            if (storedPreference.Decision == PermissionDecisionKind.Allow)
            {
                return (true, string.Empty, PermissionDeniedBehavior.InterruptTurn,
                    "always_allow", null, PermissionGrantSource.StoredPreference, evaluation);
            }

            if (storedPreference.Decision == PermissionDecisionKind.Deny)
            {
                return (false, $"Execution of '{permissionKey}' was denied by a stored user preference.",
                    PermissionDeniedBehavior.InterruptTurn, "always_deny", null,
                    PermissionGrantSource.StoredPreference, evaluation);
            }
        }

        // Request permission via a request session
        var permissionId = Guid.NewGuid().ToString();
        // Wait for response from external handler
        PermissionResponseEvent response;
        try
        {
            response = await RequestDecisionAsync(
                context.Base,
                function,
                invocation?.Action,
                callId,
                permissionId,
                evaluation,
                evaluated.LiveEvaluation,
                context.Services,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return (false, "Permission request timed out. Please respond to permission requests promptly.",
                PermissionDeniedBehavior.InterruptTurn, "timed_out", permissionId,
                PermissionGrantSource.UserDecision, evaluation);
        }
        catch (OperationCanceledException)
        {
            return (false, "Permission request was cancelled.", PermissionDeniedBehavior.InterruptTurn,
                "cancelled", permissionId, PermissionGrantSource.UserDecision, evaluation);
        }

        // Process response
        var selectedChoice = ResolveChoice(evaluation, response);
        if (selectedChoice.Decision == PermissionDecisionKind.Allow)
        {
            // Store persistent choice if requested (AlwaysAllow or AlwaysDeny)
            if (selectedChoice.Persistence is not null)
                await PersistPreferenceAsync(
                    context.Session?.Store,
                    context.SessionId,
                    context.ThreadId,
                    context.Base.ThreadEvents,
                    evaluation,
                    selectedChoice,
                    permissionId,
                    cancellationToken).ConfigureAwait(false);

            return (true, string.Empty, PermissionDeniedBehavior.InterruptTurn,
                selectedChoice.Id, permissionId, PermissionGrantSource.UserDecision, evaluation);
        }
        else
        {
            // Determine denial reason
            var denialReason = response.Feedback
                ?? _config?.Messages?.PermissionDeniedDefault
                ?? "Permission denied by user.";

            if (selectedChoice.Persistence is not null)
                await PersistPreferenceAsync(
                    context.Session?.Store,
                    context.SessionId,
                    context.ThreadId,
                    context.Base.ThreadEvents,
                    evaluation,
                    selectedChoice,
                    permissionId,
                    cancellationToken).ConfigureAwait(false);
            return (false, denialReason, selectedChoice.DeniedBehavior,
                selectedChoice.Id, permissionId, PermissionGrantSource.UserDecision, evaluation);
        }
    }

    private static JsonElement CanonicalizeArguments(IReadOnlyDictionary<string, object?> arguments)
        => JsonSerializer.SerializeToElement(
            arguments.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            HPDJsonContext.Default.DictionaryStringObject);

    private sealed record BatchPermissionOutcome(
        string PermissionKey,
        bool IsApproved,
        string DenialReason,
        PermissionDeniedBehavior DeniedBehavior,
        string ChoiceId,
        string? PermissionId,
        PermissionGrantSource Source,
        PermissionEvaluationEnvelope Evaluation,
        JsonElement CanonicalArguments);

    private bool GetEffectivePermissionRequirement(
        AgentRunConfig runConfig,
        string functionName,
        string permissionScope,
        bool attributeRequiresPermission,
        string? action)
    {
        var selector = new PermissionOverrideSelector(functionName, action, permissionScope);
        var scopedRunOverride = runConfig.Security.PermissionOverrides?
            .LastOrDefault(value => value.Selector == selector);
        if (scopedRunOverride is not null)
            return scopedRunOverride.RequiresPermission;
        var functionRunOverride = runConfig.Security.PermissionOverrides?
            .LastOrDefault(value => value.Selector == new PermissionOverrideSelector(functionName));
        if (functionRunOverride is not null)
            return functionRunOverride.RequiresPermission;

        var scopedBuilderOverride = _overrideRegistry?.Resolve(selector);
        if (scopedBuilderOverride is not null)
            return scopedBuilderOverride.Value;
        return attributeRequiresPermission;
    }

    private bool TryGetPermissionKey(
        AIFunction function,
        string functionName,
        IReadOnlyDictionary<string, object?>? arguments,
        ValidatedFunctionAction? validatedAction,
        out string permissionKey,
        out string? validationError)
    {
        if (function.AdditionalProperties?.TryGetValue(
                "ClientToolDefinition", out var clientValue) == true &&
            clientValue is HPD.Agent.ClientTools.ClientToolDefinition clientDefinition)
        {
            var resolved = arguments is null ? null : clientDefinition.ResolveOperation(arguments);
            var declaration = resolved?.Policy.Permission ??
                HPD.Agent.ClientTools.ClientToolPolicy.Resolve(clientDefinition.DefaultPolicy).Permission;
            permissionKey = declaration?.Scope ?? $"function/{Uri.EscapeDataString(functionName)}";
            validationError = null;
            return true;
        }

        if (function is HPDAIFunctionFactory.HPDAIFunction hpdFunction)
        {
            if (validatedAction is not null &&
                hpdFunction.HPDOptions.OperationContract is { } operationContract &&
                operationContract.Actions.TryGetValue(validatedAction.Action, out var actionPolicy))
            {
                permissionKey = actionPolicy.Permission.Scope;
                validationError = null;
                return true;
            }
            if (hpdFunction.HPDOptions.FunctionPermission is { } functionPermission)
            {
                permissionKey = functionPermission.Scope;
                validationError = null;
                return true;
            }
            permissionKey = $"function/{Uri.EscapeDataString(functionName)}";
            validationError = null;
            return true;
        }

        if (arguments is null)
        {
            permissionKey = functionName;
            validationError = null;
            return true;
        }

        permissionKey = $"function/{Uri.EscapeDataString(functionName)}";
        validationError = null;
        return true;
    }

    private static bool GetDeclaredPermissionRequirement(
        AIFunction function,
        IReadOnlyDictionary<string, object?> arguments,
        ValidatedFunctionAction? validatedAction = null)
    {
        if (function.AdditionalProperties?.TryGetValue(
                "ClientToolDefinition",
                out var value) == true &&
            value is HPD.Agent.ClientTools.ClientToolDefinition definition)
        {
            return (definition.ResolveOperation(arguments)?.Policy.Permission ??
                HPD.Agent.ClientTools.ClientToolPolicy.Resolve(
                    definition.DefaultPolicy).Permission)?.RequiresPermission == true;
        }

        if (function is not HPDAIFunctionFactory.HPDAIFunction hpdFunction)
            return false;

        if (validatedAction is not null &&
            hpdFunction.HPDOptions.OperationContract is { } contract &&
            contract.Actions.TryGetValue(validatedAction.Action, out var actionPolicy))
        {
            return actionPolicy.Permission.RequiresPermission;
        }

        return hpdFunction.HPDOptions.FunctionPermission?.RequiresPermission == true;
    }

    private static PermissionEvaluationEnvelope CreateDefaultEvaluation(
        AIFunction function,
        string scope,
        string? action)
    {
        const string policyId = "hpd.permission.default";
        const string policyRevision = "1";
        var choices = new PermissionChoiceSet
        {
            Items =
            [
                new PermissionChoiceDescriptor
                {
                    Id = "allow_once", Label = "Allow once", Decision = PermissionDecisionKind.Allow
                },
                new PermissionChoiceDescriptor
                {
                    Id = "always_allow", Label = "Always allow", Decision = PermissionDecisionKind.Allow,
                    Persistence = new PermissionPersistenceProposal { Kind = PermissionPersistenceKind.SessionKey }
                },
                new PermissionChoiceDescriptor
                {
                    Id = "deny_once", Label = "Deny", Decision = PermissionDecisionKind.Deny
                },
                new PermissionChoiceDescriptor
                {
                    Id = "always_deny", Label = "Always deny", Decision = PermissionDecisionKind.Deny,
                    Persistence = new PermissionPersistenceProposal { Kind = PermissionPersistenceKind.SessionKey }
                },
                new PermissionChoiceDescriptor
                {
                    Id = "feedback", Label = "Tell the agent what to do instead",
                    Decision = PermissionDecisionKind.Feedback
                }
            ]
        };
        return new PermissionEvaluationEnvelope
        {
            PolicyId = policyId,
            PolicyRevision = policyRevision,
            Key = new PermissionKey(function.Name, action, scope, policyId, policyRevision),
            Title = $"Allow {function.Name}{(action is null ? string.Empty : $" ({action})")}?",
            Summary = function.Description,
            Risk = PermissionRisk.Medium,
            Choices = choices
        };
    }

    private static async ValueTask<EvaluatedPermission> EvaluatePermissionAsync(
        AIFunction function,
        string scope,
        ResolvedFunctionInvocation? invocation,
        IReadOnlyDictionary<string, object?> arguments,
        string functionCallId,
        AgentRunConfig runConfig,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        var declaration = ResolveDeclaration(function, invocation?.Action);
        if (declaration?.PolicyDescriptorId is null)
            return new EvaluatedPermission(
                CreateDefaultEvaluation(function, scope, invocation?.Action), null, null, null, null);
        if (function is not HPDAIFunctionFactory.HPDAIFunction hpdFunction ||
            !hpdFunction.HPDOptions.PermissionDescriptors.TryGetValue(
                declaration.PolicyDescriptorId,
                out var descriptor) || descriptor.PolicyFactory is null)
            throw new InvalidOperationException(
                $"Permission policy descriptor '{declaration.PolicyDescriptorId}' is not installed.");
        if (services is null)
            throw new InvalidOperationException(
                $"Permission policy descriptor '{declaration.PolicyDescriptorId}' requires invocation services.");
        invocation ??= new ResolvedFunctionInvocation
        {
            Mode = AgentInvocationMode.Synchronous,
            Policy = AgentInvocationModePolicy.SynchronousOnly,
            Handling = AgentInvocationModeHandling.Runtime
        };
        var canonicalArguments = JsonSerializer.SerializeToElement(
            arguments.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            HPDJsonContext.Default.DictionaryStringObject);
        var input = new ValidatedPermissionInput(canonicalArguments, invocation);
        var policy = descriptor.PolicyFactory(services);
        var evaluationContext = new PermissionEvaluationContext
            {
                FunctionName = function.Name,
                Action = invocation.Action,
                FunctionCallId = functionCallId,
                Scope = declaration.Scope,
                Input = input,
                RunConfig = runConfig,
                Services = services
            };
        var evaluation = await policy.EvaluateAsync(
            evaluationContext,
            cancellationToken).ConfigureAwait(false);
        ValidateEvaluation(evaluation, declaration.Scope);
        PermissionPresentationEnvelope? presentation = null;
        if (evaluation.Presentation is not null)
        {
            var presentationDescriptor = descriptor.Presentation ??
                throw new InvalidOperationException(
                    "The permission policy returned a presentation without generated presentation metadata.");
            if (evaluation.Presentation.GetType() != presentationDescriptor.PresentationType)
                throw new InvalidOperationException(
                    $"Permission policy presentation type '{evaluation.Presentation.GetType()}' does not match generated type '{presentationDescriptor.PresentationType}'.");
            presentation = new PermissionPresentationEnvelope(
                presentationDescriptor.PresentationId,
                presentationDescriptor.Serialize(evaluation.Presentation));
        }
        return new EvaluatedPermission(new PermissionEvaluationEnvelope
        {
            PolicyId = evaluation.PolicyId,
            PolicyRevision = evaluation.PolicyRevision,
            Key = new PermissionKey(
                function.Name,
                invocation.Action,
                declaration.Scope,
                evaluation.PolicyId,
                evaluation.PolicyRevision),
            Title = evaluation.Title,
            Summary = evaluation.Summary,
            Risk = evaluation.Risk,
            Choices = evaluation.Choices,
            RequestFingerprint = evaluation.RequestFingerprint,
            Presentation = presentation
        }, policy, input, evaluationContext, evaluation);
    }

    private static void ValidateEvaluation(PermissionEvaluation evaluation, string scope)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (string.IsNullOrWhiteSpace(evaluation.PolicyId) ||
            string.IsNullOrWhiteSpace(evaluation.PolicyRevision) ||
            !string.Equals(evaluation.Scope, scope, StringComparison.Ordinal) ||
            evaluation.Choices.Items.Count == 0)
            throw new InvalidOperationException("Permission policy returned an invalid evaluation.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var choice in evaluation.Choices.Items)
        {
            if (string.IsNullOrWhiteSpace(choice.Id) || !ids.Add(choice.Id))
                throw new InvalidOperationException("Permission choices require unique non-empty IDs.");
            if (choice.Decision == PermissionDecisionKind.Feedback && choice.Persistence is not null)
                throw new InvalidOperationException("Feedback choices cannot be persisted.");
            if (choice.Persistence?.Kind == PermissionPersistenceKind.ExactRequest &&
                string.IsNullOrWhiteSpace(evaluation.RequestFingerprint))
                throw new InvalidOperationException("Exact-request persistence requires a request fingerprint.");
            if (choice.Persistence?.Kind == PermissionPersistenceKind.ValidatedRule &&
                (string.IsNullOrWhiteSpace(choice.Persistence.RuleTypeId) ||
                 choice.Persistence.CanonicalRule is null))
                throw new InvalidOperationException(
                    "Validated-rule persistence requires a rule type ID and canonical payload.");
        }
    }

    private static PermissionChoiceDescriptor ResolveChoice(
        PermissionEvaluationEnvelope evaluation,
        PermissionResponseEvent response)
    {
        var selected = evaluation.Choices.Items.FirstOrDefault(choice =>
            string.Equals(choice.Id, response.ChoiceId, StringComparison.Ordinal));
        return selected ?? throw new InvalidOperationException(
            $"Permission response selected unknown choice '{response.ChoiceId}'.");
    }

    private static PermissionEvaluationEnvelope RestrictPersistence(
        PermissionEvaluationEnvelope evaluation,
        bool persistenceAvailable)
    {
        if (persistenceAvailable) return evaluation;
        return evaluation with
        {
            Choices = new PermissionChoiceSet
            {
                Items = evaluation.Choices.Items
                    .Where(static choice => choice.Persistence is null)
                    .ToArray()
            }
        };
    }

    private static async ValueTask<PermissionPreferenceRecord?> FindStoredPreferenceAsync(
        ISessionStore? sessionStore,
        string? sessionId,
        EvaluatedPermission evaluated,
        CancellationToken cancellationToken)
    {
        if (sessionStore is not IPermissionPreferenceStore preferences || sessionId is null)
            return null;
        var evaluation = evaluated.Envelope;
        var snapshot = await preferences.ReadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return snapshot.Records
            .Where(record => record.Key == evaluation.Key &&
                (record.ExpiresAt is null || record.ExpiresAt > now) &&
                (record.Kind != PermissionPersistenceKind.ExactRequest ||
                    string.Equals(record.RequestFingerprint, evaluation.RequestFingerprint, StringComparison.Ordinal)) &&
                (record.Kind != PermissionPersistenceKind.ValidatedRule ||
                    MatchesValidatedRule(evaluated, record)))
            .OrderByDescending(static record => record.CreatedAt)
            .FirstOrDefault();
    }

    private static bool MatchesValidatedRule(
        EvaluatedPermission evaluated,
        PermissionPreferenceRecord record) =>
        evaluated.Policy is IValidatedPermissionRulePolicy validator &&
        evaluated.Input is not null &&
        evaluated.Context is not null &&
        !string.IsNullOrWhiteSpace(record.RuleTypeId) &&
        record.CanonicalRule is { } rule &&
        validator.MatchesValidatedRule(
            evaluated.Input,
            evaluated.Context,
            record.RuleTypeId,
            rule,
            record.Decision);

    private sealed record EvaluatedPermission(
        PermissionEvaluationEnvelope Envelope,
        IPermissionPolicy? Policy,
        ValidatedPermissionInput? Input,
        PermissionEvaluationContext? Context,
        PermissionEvaluation? LiveEvaluation);

    private static async ValueTask PersistPreferenceAsync(
        ISessionStore? sessionStore,
        string? sessionId,
        string? threadId,
        IAgentEventPublisher? publisher,
        PermissionEvaluationEnvelope evaluation,
        PermissionChoiceDescriptor choice,
        string permissionId,
        CancellationToken cancellationToken)
    {
        if (sessionStore is not IPermissionPreferenceStore || publisher is null ||
            sessionId is null || threadId is null || choice.Persistence is null)
            throw new InvalidOperationException("Permission persistence is unavailable for this invocation.");
        var proposal = choice.Persistence;
        var identity = $"{evaluation.Key.FunctionName}\n{evaluation.Key.Action}\n{evaluation.Key.Scope}\n" +
            $"{evaluation.Key.PolicyId}\n{evaluation.Key.PolicyRevision}\n{proposal.Kind}\n" +
            $"{proposal.ResourceScope}\n{proposal.RequestFingerprint}\n{proposal.RuleTypeId}";
        var preferenceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var snapshot = await ((IPermissionPreferenceStore)sessionStore)
                .ReadAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var record = new PermissionPreferenceRecord
            {
                PreferenceId = preferenceId,
                Key = evaluation.Key with { ResourceScope = proposal.ResourceScope },
                Decision = choice.Decision,
                Kind = proposal.Kind,
                RequestFingerprint = proposal.RequestFingerprint ?? evaluation.RequestFingerprint,
                ExpiresAt = proposal.ExpiresAt,
                RuleTypeId = proposal.RuleTypeId,
                CanonicalRule = proposal.CanonicalRule?.Clone(),
                CreatedAt = DateTimeOffset.UtcNow
            };
            var replacement = snapshot.Records
                .Where(existing => !string.Equals(existing.PreferenceId, preferenceId, StringComparison.Ordinal))
                .Append(record)
                .OrderBy(static existing => existing.PreferenceId, StringComparer.Ordinal)
                .ToArray();
            var commit = new PermissionPreferenceCommit
            {
                SessionId = sessionId,
                AuditThread = new ThreadKey(sessionId, threadId),
                ExpectedVersion = snapshot.Version,
                Replacement = new PermissionPreferenceSnapshot(snapshot.Version + 1, replacement),
                Event = new PermissionPreferenceChangedEvent(
                    preferenceId,
                    record.Key,
                    record.Decision,
                    record.Kind),
                IdempotencyKey = $"{permissionId}:{choice.Id}:{preferenceId}",
                PublisherClaimantId = permissionId
            };
            var result = await publisher.CommitPermissionPreferenceAsync(commit, cancellationToken)
                .ConfigureAwait(false);
            if (result.Status is PermissionPreferenceCommitStatus.Committed or
                PermissionPreferenceCommitStatus.AlreadyCommitted)
                return;
        }
        throw new InvalidOperationException("Permission preference commit exceeded its concurrency retry bound.");
    }

    private async ValueTask<PermissionResponseEvent> RequestDecisionAsync(
        AgentContext agentContext,
        AIFunction function,
        string? action,
        string callId,
        string permissionId,
        PermissionEvaluationEnvelope evaluation,
        PermissionEvaluation? liveEvaluation,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        var declaration = ResolveDeclaration(function, action);
        if (declaration?.InteractionDescriptorId is null)
        {
            return await agentContext.RequestAsync<PermissionRequestEvent, PermissionResponseEvent>(
                new PermissionRequestEvent(
                    permissionId, _middlewareName, function.Name, action, callId, evaluation))
                .ConfigureAwait(false);
        }
        if (function is not HPDAIFunctionFactory.HPDAIFunction hpdFunction ||
            !hpdFunction.HPDOptions.PermissionDescriptors.TryGetValue(
                declaration.InteractionDescriptorId,
                out var descriptor) || descriptor.InteractionFactory is null)
            throw new InvalidOperationException(
                $"Permission interaction descriptor '{declaration.InteractionDescriptorId}' is not installed.");
        if (services is null)
            throw new InvalidOperationException(
                $"Permission interaction descriptor '{declaration.InteractionDescriptorId}' requires invocation services.");
        var interaction = descriptor.InteractionFactory(services);
        var decision = await interaction.RequestAsync(
            new PermissionInteractionContext(new PermissionRequestDispatcher(agentContext))
            {
                PermissionId = permissionId,
                FunctionCallId = callId,
                FunctionName = function.Name,
                Action = action,
                Services = services
            },
            liveEvaluation ?? new PermissionEvaluation
            {
                PolicyId = evaluation.PolicyId,
                PolicyRevision = evaluation.PolicyRevision,
                Scope = evaluation.Key.Scope,
                Title = evaluation.Title,
                Summary = evaluation.Summary,
                Risk = evaluation.Risk,
                Choices = evaluation.Choices,
                RequestFingerprint = evaluation.RequestFingerprint
            },
            cancellationToken).ConfigureAwait(false);
        var selected = evaluation.Choices.Items.FirstOrDefault(choice =>
            string.Equals(choice.Id, decision.ChoiceId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Permission interaction selected unknown choice '{decision.ChoiceId}'.");
        if (selected.Decision != decision.Kind)
            throw new InvalidOperationException("Permission interaction returned a decision kind that does not match its choice.");
        return new PermissionResponseEvent(
            permissionId,
            _middlewareName,
            decision.ChoiceId,
            decision.Feedback ?? decision.Reason);
    }

    private sealed class PermissionRequestDispatcher(AgentContext context) : IPermissionRequestDispatcher
    {
        public async ValueTask<TResponse> RequestAsync<TRequest, TResponse>(
            TRequest request,
            CancellationToken cancellationToken)
            where TRequest : AgentEvent, IAgentRequestEvent<TResponse>
            where TResponse : AgentEvent, IAgentResponseEvent
        {
            cancellationToken.ThrowIfCancellationRequested();
            var codec = context.Config?.EventComposition?.Codec
                ?? throw new InvalidOperationException("Custom permission interaction requires an application event composition.");
            if (!codec.TryGetByType(typeof(TRequest), out var requestDescriptor) ||
                requestDescriptor.Durability != AgentEventDurability.Durable ||
                !codec.TryGetByType(typeof(TResponse), out var responseDescriptor) ||
                responseDescriptor.Durability != AgentEventDurability.Durable)
                throw new InvalidOperationException(
                    $"Custom permission event pair '{typeof(TRequest).FullName}'/'{typeof(TResponse).FullName}' must be present as durable events in the application composition.");
            return await context.RequestAsync<TRequest, TResponse>(request).ConfigureAwait(false);
        }
    }

    private static FunctionPermissionGrant CreateGrant(
        BeforeFunctionContext context,
        AIFunction function,
        string scope,
        PermissionGrantSource source,
        string choiceId,
        string? permissionId = null,
        PermissionEvaluationEnvelope? evaluation = null)
    {
        var action = context.InvocationMode?.Action;
        var declaration = ResolveDeclaration(function, action) ?? new AIFunctionPermissionDeclaration
        {
            RequiresPermission = true,
            Scope = scope,
            Source = PermissionDeclarationSource.FrameworkDefault
        };
        var canonicalArguments = JsonSerializer.SerializeToElement(
            context.Arguments.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            HPDJsonContext.Default.DictionaryStringObject);
        const string defaultPolicyId = "hpd.permission.default";
        const string defaultPolicyRevision = "1";
        var key = evaluation?.Key ?? new PermissionKey(
            function.Name, action, declaration.Scope,
            declaration.PolicyDescriptorId ?? defaultPolicyId, defaultPolicyRevision);
        return new FunctionPermissionGrant
        {
            PermissionId = permissionId,
            FunctionCallId = context.FunctionCallId,
            FunctionName = function.Name,
            Action = action,
            Key = key,
            RequestFingerprint = evaluation?.RequestFingerprint,
            ChoiceId = choiceId,
            GrantedAt = DateTimeOffset.UtcNow,
            Source = source,
            Authority = new PermissionAuthorityStamp
            {
                CanonicalArguments = canonicalArguments,
                Declaration = declaration,
                PolicyId = key.PolicyId,
                PolicyRevision = key.PolicyRevision,
                RequestFingerprint = evaluation?.RequestFingerprint
            }
        };
    }

    private static AIFunctionPermissionDeclaration? ResolveDeclaration(AIFunction function, string? action)
    {
        if (function is not HPDAIFunctionFactory.HPDAIFunction hpdFunction) return null;
        if (action is not null && hpdFunction.HPDOptions.OperationContract is { } contract &&
            contract.Actions.TryGetValue(action, out var policy))
            return policy.Permission;
        return hpdFunction.HPDOptions.FunctionPermission;
    }

    private ConcurrentDictionary<string, BatchPermissionOutcome> GetBatchOutcomes(AgentContext context) =>
        _batchOutcomes.GetValue(context, static _ => new ConcurrentDictionary<string, BatchPermissionOutcome>(StringComparer.Ordinal));
}
