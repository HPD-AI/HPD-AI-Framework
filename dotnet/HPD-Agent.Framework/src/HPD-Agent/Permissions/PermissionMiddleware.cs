using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Permissions;

/// <summary>The framework-owned gate for every governed function invocation.</summary>
public sealed class PermissionMiddleware : IAgentMiddleware
{
    internal const string DefaultPolicyId = "hpd.permission.default.v1";
    private readonly PermissionOverrideRegistry? _overrides;
    private readonly IPermissionDescriptorCatalog _catalog;
    private readonly string _source;
    private readonly string? _stableAgentId;
    private readonly string _agentId;
    private readonly AuthorizationMode _mode;
    private readonly ConditionalWeakTable<AgentContext, ConcurrentDictionary<string, PreparedAdmission>> _batches = new();

    internal PermissionMiddleware(AgentConfig? config = null, string? middlewareName = null,
        PermissionOverrideRegistry? overrideRegistry = null, IPermissionDescriptorCatalog? descriptorCatalog = null,
        AuthorizationMode authorizationMode = AuthorizationMode.Enforce)
    {
        if (!Enum.IsDefined(authorizationMode)) throw new ArgumentOutOfRangeException(nameof(authorizationMode));
        _stableAgentId = string.IsNullOrWhiteSpace(config?.AgentId) ? null : config.AgentId;
        _agentId = _stableAgentId ?? config?.Name ?? "Agent";
        _source = middlewareName ?? "PermissionMiddleware";
        PermissionValidation.Text(_source, 256, "PermissionRequestInvalid", required: true);
        _overrides = overrideRegistry;
        _catalog = descriptorCatalog ?? PermissionDescriptorComposition.CreateCatalog();
        _mode = authorizationMode;
    }

    internal AuthorizationMode Mode => _mode;

    internal PermissionMiddleware ForAgent(AgentConfig config) =>
        new(config, _source, _overrides, _catalog, _mode);

    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task BeforeParallelBatchAsync(BeforeParallelBatchContext context, CancellationToken cancellationToken)
    {
        var cache = _batches.GetValue(context.Base, static _ => new(StringComparer.Ordinal));
        foreach (var call in context.ParallelFunctions)
        {
            var result = await AuthorizeCallAsync(context, call.ResolvedFunction
                ?? throw new InvalidOperationException("Permission binding was not resolved before batch admission."), call.CallId, call.Arguments,
                call.ResolvedInvocation, context.RunConfig, cancellationToken).ConfigureAwait(false);
            if (!cache.TryAdd(call.CallId, new(call.Function, Canonicalize(call.Arguments), result)))
                throw new InvalidOperationException("PermissionRequestInvalid: duplicate function call ID.");
            if (result is Reject) break;
        }
    }

    public async Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
    {
        if (context.Function is null) return;
        CallDisposition result;
        if (_batches.TryGetValue(context.Base, out var cache) && cache.TryRemove(context.FunctionCallId, out var prepared))
        {
            if (!ReferenceEquals(prepared.Function, context.Function) ||
                !JsonElement.DeepEquals(prepared.Arguments, Canonicalize(context.Arguments)))
                throw new InvalidOperationException("permission_authority_drift: prepared invocation changed.");
            result = prepared.Disposition;
        }
        else
            result = await AuthorizeCallAsync(context, context.ResolvedFunction
                ?? throw new InvalidOperationException("Permission binding was not resolved before admission."), context.FunctionCallId,
                context.Arguments, context.InvocationMode, context.RunConfig, cancellationToken).ConfigureAwait(false);
        context.PermissionRequired = result is not Invoke { Grant: null };
        switch (result)
        {
            case Invoke invoke:
                context.PermissionGrant = invoke.Grant;
                break;
            case Reject reject:
                context.BlockExecution = true;
                context.OverrideResult = reject.Result;
                context.UpdateState(state => state with { IsTerminated = true, TerminationReason = "permission_denied" });
                context.EventFlows?.InterruptFlow(context.FunctionCallId);
                await context.PublishAsync(new InterruptionHandledEvent(context.FunctionCallId,
                    reject.Result, InterruptionSource.Middleware), cancellationToken).ConfigureAwait(false);
                break;
            case ContinueAfterFeedback feedback:
                context.BlockExecution = true;
                context.OverrideResult = feedback.Result;
                break;
        }
    }

    internal async ValueTask<CallDisposition> AuthorizeCallAsync(HookContext context, ResolvedFunction resolvedFunction,
        string callId, IReadOnlyDictionary<string, object?> arguments, ResolvedFunctionInvocation? invocation,
        AgentRunConfig runConfig, CancellationToken cancellationToken)
    {
        var function = resolvedFunction.Function;
        if (resolvedFunction.Permission is not { } binding) return new Invoke(null);
        var originalDeclaration = ResolveDeclaration(function, invocation?.Action);
        if (context.Thread is null || context.Base.ThreadEvents is null)
            throw new InvalidOperationException("PermissionJournalUnavailable: governed calls require a durable thread publisher.");
        PermissionValidation.Text(binding.Authority, 512, "PermissionBindingConflict", required: true);
        PermissionValidation.Text(binding.PolicyDescriptorId, 512, "PermissionPolicyMissing", required: true);
        var permissionId = Guid.NewGuid().ToString("N");
        PermissionValidation.Text(permissionId, 128, "PermissionRequestInvalid", required: true);
        PermissionValidation.Text(callId, 256, "PermissionRequestInvalid", required: true);
        var key = new PermissionDecisionKey(_agentId, binding.Authority, invocation?.Action, binding.PolicyDescriptorId!);
        var canonical = Canonicalize(arguments);
        var source = PermissionGrantSource.PolicyDecision;
        PermissionPolicyResult evaluation;
        IPermissionPolicy? policy = null;
        PermissionEvaluationContext? evaluationContext = null;
        var input = new ValidatedPermissionInput(canonical, invocation ?? new ResolvedFunctionInvocation
        {
            Mode = AgentInvocationMode.Synchronous, Policy = AgentInvocationModePolicy.SynchronousOnly,
            Handling = AgentInvocationModeHandling.Runtime
        });
        if (_mode == AuthorizationMode.Bypass)
        {
            evaluation = new() { Outcome = new PermissionAllow() };
            source = PermissionGrantSource.HostBypass;
        }
        else
        {
            if (binding.PolicyDescriptorId == DefaultPolicyId)
                evaluation = DefaultResult();
            else
            {
                if (!_catalog.TryGet(binding.PolicyDescriptorId!, out var descriptor))
                    throw new InvalidOperationException($"PermissionPolicyMissing: {binding.PolicyDescriptorId}");
                var services = context.Services ?? throw new InvalidOperationException("Permission policy requires invocation services.");
                policy = descriptor.PolicyFactory(services);
                evaluationContext = new()
                {
                    FunctionName = function.Name, Action = invocation?.Action, FunctionCallId = callId,
                    Authority = binding.Authority, Input = input, RunConfig = runConfig, Services = services
                };
                evaluation = await policy.EvaluateAsync(input, evaluationContext, cancellationToken).ConfigureAwait(false);
            }
            PermissionValidation.Validate(evaluation, policy);
            await context.PublishAsync(new PermissionEvaluatedEvent(permissionId, callId, _agentId,
                PermissionDomains.ToolExecution, key.Authority, key.Action, key.PolicyDescriptorId,
                evaluation.Outcome switch { PermissionAllow => "Allow", PermissionDeny => "Deny", _ => "Ask" }), cancellationToken).ConfigureAwait(false);
        }
        if (evaluation.Outcome is PermissionDeny deny)
            return await Resolve(PermissionDecisionKind.Deny, null, PermissionGrantSource.PolicyDecision,
                deny.Reason, deny.DeniedBehavior).ConfigureAwait(false);
        if (evaluation.Outcome is PermissionAllow)
            return await Resolve(PermissionDecisionKind.Allow, null, source).ConfigureAwait(false);
        var ask = (PermissionAsk)evaluation.Outcome;
        if (_stableAgentId is not null)
        {
            var available = ReadState(context);
            foreach (var record in available.Decisions.Where(record => record.Key == key))
            {
                var matches = record.Persistence == PermissionPersistenceKind.Session;
                if (!matches && policy is IValidatedPermissionRulePolicy rules && evaluationContext is not null)
                {
                    try { matches = rules.MatchesStoredDecision(input, evaluationContext, record); }
                    catch (Exception) { matches = false; }
                }
                if (matches) return await Resolve(PermissionDecisionKind.Allow, null, PermissionGrantSource.StoredDecision).ConfigureAwait(false);
            }
        }
        var choices = ask.Choices.ToList();
        PersistenceUnavailableReason? unavailable = _stableAgentId is null ? PersistenceUnavailableReason.NoStableAgentId
            : context.Session?.Store is null ? PersistenceUnavailableReason.NoSessionStore : null;
        var candidates = new Dictionary<string, PermissionDecisionRecord>(StringComparer.Ordinal);
        foreach (var choice in ask.Choices)
        {
            if (evaluation.PersistenceByChoice is null ||
                !evaluation.PersistenceByChoice.TryGetValue(choice.Id, out var persistence)) continue;
            if (unavailable is PersistenceUnavailableReason.NoStableAgentId or PersistenceUnavailableReason.NoSessionStore)
            { choices.Remove(choice); continue; }
            var candidate = new PermissionDecisionRecord
            {
                RecordId = PermissionRecordIdentity.Create(key, persistence.Kind, persistence.RuleTypeId, persistence.CanonicalRule),
                PermissionId = permissionId, Key = key, Persistence = persistence.Kind,
                RuleTypeId = persistence.RuleTypeId, CanonicalRule = persistence.CanonicalRule?.Clone(), CreatedAt = DateTimeOffset.UtcNow
            };
            _ = PermissionDecisionIndex.Apply(ReadState(context), candidate);
            candidates.Add(choice.Id, candidate);
        }
        if (!choices.Any(choice => choice.Kind == PermissionDecisionKind.Allow))
        {
            return await Resolve(PermissionDecisionKind.Deny, null, PermissionGrantSource.FrameworkDecision,
                "PermissionPersistenceUnavailable: reusable approval is unavailable.",
                PermissionDeniedBehavior.ReturnToModel).ConfigureAwait(false);
        }
        if (runConfig.Security.Approval == AgentApprovalPolicy.AutoApprove)
            return await Resolve(PermissionDecisionKind.Allow, null, PermissionGrantSource.HostAutoApprove).ConfigureAwait(false);
        PermissionChoice selected;
        string? feedback = null;
        var resolutionSource = PermissionGrantSource.UserDecision;
        try
        {
            var response = await context.Base.RequestAsync<PermissionRequestEvent, PermissionResponseEvent>(new(
                permissionId, callId, _source, PermissionDomains.ToolExecution, key.Authority, key.Action,
                ask.Resource, ask.Reason, ask.Risk, choices, evaluation.Evidence, unavailable,
                function.Name, canonical.Clone())).ConfigureAwait(false);
            if (response.PermissionId != permissionId || response.SourceName != _source)
                throw new InvalidOperationException("PermissionResponseUnbound: response does not own this request.");
            selected = choices.SingleOrDefault(choice => choice.Id == response.ChoiceId)
                ?? throw new InvalidOperationException("PermissionResponseUnbound: unknown choice.");
            PermissionValidation.Text(response.Feedback, 8192, "PermissionResponseUnbound");
            feedback = response.Feedback;
        }
        catch (TimeoutException)
        { selected = choices.First(choice => choice.Kind == PermissionDecisionKind.Deny); resolutionSource = PermissionGrantSource.TimedOut; feedback = "Permission request timed out."; }
        catch (OperationCanceledException)
        { selected = choices.First(choice => choice.Kind == PermissionDecisionKind.Deny); resolutionSource = PermissionGrantSource.Cancelled; feedback = "Permission request was cancelled."; }
        PermissionDecisionRecord? candidateRecord = null;
        if (selected.Kind == PermissionDecisionKind.Allow && candidates.TryGetValue(selected.Id, out var candidateValue))
            candidateRecord = candidateValue;
        return await Resolve(selected.Kind, selected.Id, resolutionSource, feedback,
            selected.DeniedBehavior ?? PermissionDeniedBehavior.InterruptTurn, candidateRecord).ConfigureAwait(false);

        async ValueTask<CallDisposition> Resolve(PermissionDecisionKind decision, string? choiceId,
            PermissionGrantSource decisionSource, string? reason = null,
            PermissionDeniedBehavior deniedBehavior = PermissionDeniedBehavior.InterruptTurn,
            PermissionDecisionRecord? record = null)
        {
            var token = decisionSource is PermissionGrantSource.Cancelled or PermissionGrantSource.TimedOut
                ? CancellationToken.None : cancellationToken;
            await context.PublishAsync(new PermissionResolvedEvent(permissionId, callId, _agentId,
                PermissionDomains.ToolExecution, key.Authority, key.Action, key.PolicyDescriptorId,
                (evaluation.Outcome as PermissionAsk)?.Resource, decision, choiceId, decisionSource,
                decision == PermissionDecisionKind.Allow ? null : reason ?? "Permission denied."), token).ConfigureAwait(false);
            if (record is not null)
            {
                context.UpdateState(state => state with
                {
                    MiddlewareState = state.MiddlewareState.SetState(PermissionDecisionIndex.StateKey,
                        PermissionDecisionIndex.Apply(state.MiddlewareState.GetState<PermissionDecisionStateData>(PermissionDecisionIndex.StateKey), record))
                });
                PermissionStateCheckpoint.MarkPending(context.Base);
            }
            if (decision == PermissionDecisionKind.Allow)
                return new Invoke(new FunctionPermissionGrant
                {
                    PermissionId = permissionId, FunctionCallId = callId, FunctionName = function.Name,
                    Action = key.Action, Key = key, ChoiceId = choiceId, Source = decisionSource, GrantedAt = DateTimeOffset.UtcNow,
                    Authority = new() { CanonicalArguments = canonical, Declaration = originalDeclaration, PolicyDescriptorId = key.PolicyDescriptorId }
                });
            var result = reason ?? "Permission denied by user.";
            var reasonCode = decisionSource == PermissionGrantSource.FrameworkDecision
                ? "PermissionPersistenceUnavailable" : "permission_denied";
            return deniedBehavior == PermissionDeniedBehavior.ReturnToModel
                ? new ContinueAfterFeedback(result, reasonCode) : new Reject(result, reasonCode);
        }
    }

    internal ResolvedFunction ResolveFunction(AIFunction function, ResolvedFunctionInvocation? invocation, AgentRunConfig config) =>
        PermissionBindingResolver.Resolve(function, invocation, config, _overrides);

    private static AIFunctionPermissionDeclaration? ResolveDeclaration(AIFunction function, string? action) =>
        PermissionBindingResolver.ResolveDeclaration(function, action);

    private static PermissionDecisionStateData ReadState(HookContext context) => context.Analyze(state =>
        PermissionDecisionIndex.Available(state.MiddlewareState.GetState<PermissionDecisionStateData>(PermissionDecisionIndex.StateKey)));
    private static JsonElement Canonicalize(IReadOnlyDictionary<string, object?> arguments) => JsonSerializer.SerializeToElement(
        arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), HPDJsonContext.Default.DictionaryStringObject);
    private static PermissionPolicyResult DefaultResult() => new()
    {
        Outcome = new PermissionAsk(null, PermissionRisk.Medium, null,
        [new("allow_once", "Allow once", PermissionDecisionKind.Allow),
         new("allow_session", "Allow for this session", PermissionDecisionKind.Allow),
         new("deny_once", "Deny", PermissionDecisionKind.Deny),
         new("feedback", "Provide feedback", PermissionDecisionKind.Feedback, PermissionDeniedBehavior.ReturnToModel)]),
        PersistenceByChoice = new Dictionary<string, PermissionPersistenceProposal>
        { ["allow_session"] = new() { Kind = PermissionPersistenceKind.Session } }
    };
    private sealed record PreparedAdmission(AIFunction Function, JsonElement Arguments, CallDisposition Disposition);
}
