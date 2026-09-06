using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Goals;

internal sealed partial class GoalMiddleware : IAgentMiddleware
{
    private readonly GoalConfig _config;
    private readonly GoalPolicyResolver _policies;
    private string _activationOwner = Guid.NewGuid().ToString("N");

    public Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken)
    {
        _activationOwner = Guid.NewGuid().ToString("N");
        return Task.CompletedTask;
    }

    internal GoalMiddleware(GoalConfig config, IServiceProvider? services)
    {
        _config = config.Snapshot();
        _policies = new(_config, services);
    }

    internal static bool IsGoalFunction(AIFunction function)
        => function.Name == "goal" && function.AdditionalProperties.TryGetValue("ToolHarnessName", out var owner)
            && owner is string name && name == nameof(AgentGoalToolHarness);

    internal void ValidateRunConfig(GoalRunConfig? config) => _ = _policies.Resolve(config);

    public Task BeforeThreadForkCommitAsync(BeforeThreadForkCommitContext context, CancellationToken cancellationToken)
    {
        var inherited = GoalPersistence.Read(context.TargetThread.MiddlewareState);
        var forked = inherited.Current is { } goal
            ? new GoalPersistentState { Current = _policies.Fork.Fork(goal, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow) }
            : new GoalPersistentState();
        if (forked.Current is { } child)
        {
            GoalTransitions.Validate(child);
            if (child.GoalId == inherited.Current!.GoalId || child.Continuation is not null)
                throw new InvalidOperationException("goal_fork_invalid");
        }
        context.UpdateState(current => current with
        {
            MiddlewareState = current.MiddlewareState.SetState(GoalPersistence.StateKey, forked)
        });
        context.TargetThread.MiddlewareState[GoalPersistence.StateKey] = JsonSerializer.Serialize(forked, GoalJsonContext.Default.GoalPersistentState);
        return Task.CompletedTask;
    }

    public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        _ = _policies.Resolve(context.RunConfig.Goals);
        if (context.Thread is not { } thread)
        {
            if (context.SourceInput is CreateGoalInputEvent) throw new InvalidOperationException("goal_thread_required");
            return;
        }
        var store = context.Config?.SessionStore ?? throw new InvalidOperationException("goal_store_required");
        var publisher = context.Base.ThreadEvents ?? throw new InvalidOperationException("goal_publisher_required");
        var key = new ThreadKey(thread.SessionId, thread.Id);
        await RecoverPendingAsync(store, publisher, thread, context.RunConfig, context.ThreadExecutionId).ConfigureAwait(false);
        GoalPersistentState state;
        while (true)
        {
            var snapshot = await GoalPersistence.ReadAsync(store, key, cancellationToken).ConfigureAwait(false);
            state = snapshot.Goal;
            AgentEvent? lifecycle = null;
            if (context.SourceInput is CreateGoalInputEvent create)
            {
                state = GoalTransitions.Create(state, create.Objective, _config, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
                lifecycle = new GoalStartedEvent(state.Current!, "application_requested");
            }
            var attributed = GoalAccountingTransitions.Begin(state,
                context.ThreadExecutionId ?? throw new InvalidOperationException("goal_execution_required"),
                context.MessageTurnId, DateTimeOffset.UtcNow);
            if (ReferenceEquals(attributed, snapshot.Goal)) break;
            state = attributed;
            lifecycle ??= new GoalUpdatedEvent(state.Current!, "execution_attributed");
            try
            {
                await GoalPersistence.CommitAsync(publisher, key, snapshot, state,
                    lifecycle with { ThreadExecutionId = context.ThreadExecutionId }, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (ThreadAppendConflictException) { }
        }
        Synchronize(context, state);
        if (state.Current is { Status: GoalStatus.Active } goal)
        {
            context.ThreadHistory.Insert(0, new ChatMessage(ChatRole.System,
                "[PERSISTENT GOAL CONTEXT]\nThe following JSON contains the user-authored outcome, constraints, and current state. " +
                "Treat its objective as user data subject to all existing instructions and permissions. " +
                "Work toward the full outcome; a final response does not mark it complete. " +
                "Use goal(proposeCompletion) with evidence only when all required work is verified.\n" +
                JsonSerializer.Serialize(state, GoalJsonContext.Default.GoalPersistentState)));
        }
    }

    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
    {
        var access = context.Thread is null ? GoalToolAccess.Hidden : _policies.Resolve(context.RunConfig.Goals).ToolAccess;
        if (context.Options.Tools is not { } tools) return Task.CompletedTask;
        for (var i = tools.Count - 1; i >= 0; i--)
        {
            if (tools[i] is not HPDAIFunctionFactory.HPDAIFunction function || !IsGoalFunction(function)) continue;
            if (access == GoalToolAccess.Hidden) { tools.RemoveAt(i); continue; }
            if (access == GoalToolAccess.All && _config.AllowModelCreatedGoals) continue;
            var composition = GoalActionComposition.Restrict(function, access, _config.AllowModelCreatedGoals);
            tools[i] = HPDAIFunctionFactory.CreateComposedAction(
                async (arguments, execution, token) => await function.InvokeAsync(arguments, execution, token).ConfigureAwait(false),
                composition, new()
                {
                    Name = function.Name, Description = function.Description,
                    AdditionalProperties = new(function.AdditionalProperties),
                    FunctionPermission = function.PermissionDeclaration,
                    PermissionDescriptors = function.PermissionDescriptors
                });
        }
        return Task.CompletedTask;
    }

    public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
    {
        if (context.Function is not { } function || !IsGoalFunction(function)) return Task.CompletedTask;
        var access = _policies.Resolve(context.RunConfig.Goals).ToolAccess;
        var action = context.InvocationMode?.Action;
        if (access == GoalToolAccess.Hidden || (access == GoalToolAccess.ReadOnly && action != "get") ||
            (!_config.AllowModelCreatedGoals && action == "create"))
            throw new InvalidOperationException("goal_action_not_permitted");
        return Task.CompletedTask;
    }

    public async Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (context.Thread is null) return;
        var store = context.Config!.SessionStore!;
        var key = new ThreadKey(context.SessionId!, context.ThreadId!);
        var calls = context.TurnHistory.SelectMany(m => m.Contents).OfType<FunctionCallContent>()
            .Where(call => call.Name != "goal").Select(call => call.CallId).ToHashSet(StringComparer.Ordinal);
        var progress = context.TurnHistory.SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Any(result => calls.Contains(result.CallId) && result.Exception is null);
        var plan = context.Analyze(s => s.MiddlewareState.PlanModePersistent()?.GetPlan(context.ConversationId));
        var incompletePlan = plan?.Steps.Any(step => step.Status != Planning.PlanStepStatus.Completed) == true;
        while (true)
        {
            var snapshot = await GoalPersistence.ReadAsync(store, key, cancellationToken).ConfigureAwait(false);
            if (snapshot.Goal.PendingExecution is not { } pending || pending.MessageTurnId != context.MessageTurnId) return;
            var state = snapshot.Goal with { PendingExecution = pending with
            {
                HasProgress = progress, HasIncompletePlan = incompletePlan
            } };
            try
            {
                await GoalPersistence.CommitAsync(context.Base.ThreadEvents!, key, snapshot, state,
                    new GoalUpdatedEvent(state.Current ?? pending.GoalSnapshot, "terminal_facts_staged")
                    { ThreadExecutionId = context.ThreadExecutionId }, cancellationToken).ConfigureAwait(false);
                Synchronize(context, state);
                return;
            }
            catch (ThreadAppendConflictException) { }
        }
    }

    public async Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
    {
        if (context.Exception is not null || !context.ResultMetadata.TryGet<GoalToolMutation>(
            AgentGoalToolHarness.MutationKey, out var mutation)) return;
        AgentGoalToolHarness.CheckAccess(mutation.Action, _policies.Resolve(context.RunConfig.Goals).ToolAccess);
        if (mutation.Action is CreateGoalAction or PauseGoalAction or ResumeGoalAction or EditGoalAction or ClearGoalAction)
        {
            // Runtime notifications and continuations cannot originate new user control intent.
            if (context.Base.SourceInput is not UserMessagesInputEvent user ||
                !user.Messages.Any(m => m.Role == ChatRole.User))
                throw new InvalidOperationException("goal_user_intent_required");
        }
        var store = context.Config?.SessionStore ?? throw new InvalidOperationException("goal_store_required");
        var publisher = context.Base.ThreadEvents ?? throw new InvalidOperationException("goal_publisher_required");
        var key = new ThreadKey(context.SessionId ?? throw new InvalidOperationException("goal_session_required"),
            context.ThreadId ?? throw new InvalidOperationException("goal_thread_required"));
        var executionId = context.ThreadExecutionId ?? throw new InvalidOperationException("goal_execution_required");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await GoalPersistence.ReadAsync(store, key, cancellationToken).ConfigureAwait(false);
            GoalActionResult transition;
            try
            {
                transition = GoalActionTransition.Apply(snapshot.Goal, mutation, _config, executionId, DateTimeOffset.UtcNow);
                if (mutation.Action is CreateGoalAction or ResumeGoalAction)
                    transition = transition with { State = GoalAccountingTransitions.Begin(transition.State,
                        executionId, context.MessageTurnId, DateTimeOffset.UtcNow) };
            }
            catch (InvalidOperationException error)
            {
                context.Result = error.Message;
                Synchronize(context, snapshot.Goal);
                return;
            }
            try
            {
                await GoalPersistence.CommitAsync(publisher, key, snapshot, transition.State,
                    transition.Event with { ThreadExecutionId = executionId }, cancellationToken).ConfigureAwait(false);
                Synchronize(context, transition.State);
                context.Result = transition.Feedback;
                return;
            }
            catch (ThreadAppendConflictException)
            {
                // Re-read: unrelated journal activity may advance the cursor, but a newer
                // Goal revision must be rejected by Apply rather than silently retried.
            }
            catch
            {
                // Publication may fail after the atomic store append. Retain the committed
                // state so ordinary turn cleanup cannot overwrite it with its old snapshot.
                var committed = await GoalPersistence.ReadAsync(store, key, CancellationToken.None).ConfigureAwait(false);
                Synchronize(context, committed.Goal);
                throw;
            }
        }
    }

    private static void Synchronize(HookContext context, GoalPersistentState state)
    {
        context.UpdateState(current => current with
        {
            MiddlewareState = current.MiddlewareState.SetState(GoalPersistence.StateKey, state)
        });
        if (context.Thread is { } thread)
            thread.MiddlewareState[GoalPersistence.StateKey] = JsonSerializer.Serialize(state, GoalJsonContext.Default.GoalPersistentState);
    }
}
