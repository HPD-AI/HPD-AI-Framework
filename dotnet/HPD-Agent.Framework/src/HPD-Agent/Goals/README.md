# Persistent Goals

Goals preserve an explicitly requested outcome on a thread. Configure the capability with
`WithGoals()` or `AgentConfig.Goals`, and start the ordinary agent runtime with `StartAsync()`
when automatic continuation is wanted.

```csharp
using HPD.Agent;
using HPD.Agent.Goals;

// Add your normal provider/client and event composition to this builder.
await using var agent = await builder.WithGoals().BuildAsync();
await agent.CreateSessionAsync("work");
using var subscription = agent.Subscribe<GoalCompletedEvent>(completed =>
{
    Console.WriteLine(completed.AcceptedProposal?.Summary);
    return ValueTask.CompletedTask;
});
await agent.StartAsync();
await agent.RunAsync(new CreateGoalInputEvent
{
    SessionId = "work",
    ThreadId = "main",
    Objective = "Implement the requested change and verify the acceptance checks."
});
// RunAsync covers this submitted input. Observe Goal events for overall completion.
// Keep the application/runtime alive for automatic continuation, then StopAsync on exit.
```

Without `StartAsync`, a successful unfinished direct turn leaves the Goal active with no
reserved continuation. No Goal API starts the runtime implicitly. Call `await agent.RestoreThreadAsync(sessionId, threadId)` to load an existing thread into
an agent for reconciliation when that runtime starts or is already running;
recovery does not scan unrelated threads.

The single `goal` tool exposes `create`, `get`, `proposeCompletion`, `reportBlocker`, `pause`,
`resume`, `edit`, and `clear`. Creation and control mutations require explicit user intent.
The model proposes success with evidence; policy accepts it only after successful terminal
closure. `remainingWork` records admitted unfinished or unverified work and prevents success.
The default blocker policy requires three matching reports in distinct consecutive executions.

`AgentRunConfig.Goals` contains nullable tool access and policy-key overrides, independently
inherited from the agent defaults. `ReadOnly` exposes only `get` and rejects forged mutations;
`Hidden` removes the function. Policy implementations live in keyed dependency-injection
services, not serialized configuration. Continuations capture the submitting execution's
run configuration; restored work uses the current runtime configuration. Provider settings
and credentials are never stored in Goal state.

Goal state and lifecycle events commit together in the thread journal. Accounting consumes
closed provider-operation usage once, with explicit exact/partial/unavailable quality.
Cancellation through the caller or exact execution controller pauses work. Runtime shutdown
preserves an active Goal for recovery. Cancellation cause, reason, and source remain in the
terminal journal event. Interrupted recovery excludes offline downtime from execution time.

Hosting accepts `CREATE_GOAL_INPUT`; internal continuation inputs are rejected at public
admission. The TypeScript client exposes `chat.startGoal(objective, { runConfig })` and typed
Goal lifecycle events. HPD-OS uses `/goal <outcome>` and a separate panel above the editor;
normal chat and Goal creation use the same provider, workspace, permission, and subagent
configuration composer.

Verification projects are `HPD-Agent.Goals.Tests` and `HPD-Agent.Goals.AotSmoke`. Publish the
latter for the local RID and run the native executable to verify generated contracts with
reflection-based JSON serialization disabled.
