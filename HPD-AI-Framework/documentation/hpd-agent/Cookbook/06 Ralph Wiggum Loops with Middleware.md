# Ralph Wiggum Loops with Middleware

> Run the agent until something external says it's done.

> **This is an illustrative example.** The code below is intentionally simplified — `RunTestsAsync`, `CodingTools`, and similar calls are placeholders. Wiring up a real validator, a sandboxed execution environment, and file system access requires infrastructure that will vary significantly by project. Use this as a pattern reference, not a drop-in solution.

The Ralph Wiggum loop is a pattern named after the Simpsons character — naive, persistent, unstoppable. The original form is a bash one-liner:

```bash
while :; do cat PROMPT.md | agent; done
```

The agent runs, does some work, exits. The loop restarts it with the same prompt. Files on disk persist between iterations — that's the memory. You Ctrl+C when you're satisfied.

It's the simplest possible autonomous loop. No complex orchestration, no self-assessment — just iteration until something external (tests, a build, a linter) says the job is done.

---

## The middleware version

The bash version has a problem: you lose all visibility into what each attempt did, and you have no programmatic way to stop it. You're also starting from a completely blank context each time.

With middleware you can implement the same pattern *inside* the agent, which gives you:

- **Per-attempt observability** — you see every turn as it happens
- **Programmatic stopping** — a validator decides when to terminate, not a human
- **Failure context carried forward** — the previous attempt's failure output is injected into the next turn, so the agent knows what went wrong

The structure is two hooks and state:

- `AfterMessageTurnAsync` — runs the external validator after each turn; stores the result
- `BeforeMessageTurnAsync` — injects the previous failure into the next turn's context

```
Turn 1: agent writes code
         ↓ AfterMessageTurnAsync: run tests → FAIL → store output
Turn 2: BeforeMessageTurnAsync: inject failure output
         agent reads failure, fixes code
         ↓ AfterMessageTurnAsync: run tests → FAIL → store output
Turn 3: BeforeMessageTurnAsync: inject failure output
         agent reads failure, fixes code
         ↓ AfterMessageTurnAsync: run tests → PASS → terminate
```

---

## Building it

### Step 1 — Define the state

The failure output has to survive between turns. That means persistent state — the `Persistent = true` flag tells the framework to save and restore it across `RunAsync` calls.

```csharp
[MiddlewareState(Persistent = true)]
public sealed record RalphLoopState
{
    public string? LastFailureOutput { get; init; }
    public int Attempts { get; init; }
}
```

### Step 2 — The middleware

```csharp
public class RalphLoopMiddleware : IAgentMiddleware
{
    private readonly Func<CancellationToken, Task<(bool passed, string output)>> _validator;
    private readonly int _maxAttempts;

    public RalphLoopMiddleware(
        Func<CancellationToken, Task<(bool passed, string output)>> validator,
        int maxAttempts = 10)
    {
        _validator = validator;
        _maxAttempts = maxAttempts;
    }

    // Inject the previous failure before the agent starts each turn
    public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken ct)
    {
        var state = context.GetMiddlewareState<RalphLoopState>();

        if (state?.LastFailureOutput is not null)
        {
            context.ConversationHistory.Add(new ChatMessage(
                ChatRole.System,
                $"""
                Previous attempt #{state.Attempts} failed. Output:

                {state.LastFailureOutput}

                Fix the issue and try again.
                """
            ));
        }

        return Task.CompletedTask;
    }

    // Run the validator after each turn
    public async Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken ct)
    {
        var (passed, output) = await _validator(ct);

        var attempts = (context.GetMiddlewareState<RalphLoopState>()?.Attempts ?? 0) + 1;

        if (passed)
        {
            // Done — terminate cleanly
            context.UpdateMiddlewareState<RalphLoopState>(s => s with
            {
                LastFailureOutput = null,
                Attempts = attempts
            });

            context.UpdateState(s => s with
            {
                IsTerminated = true,
                TerminationReason = $"Validator passed after {attempts} attempt(s)"
            });

            return;
        }

        if (attempts >= _maxAttempts)
        {
            context.UpdateState(s => s with
            {
                IsTerminated = true,
                TerminationReason = $"Gave up after {attempts} failed attempt(s)"
            });

            return;
        }

        // Store failure output for the next turn
        context.UpdateMiddlewareState<RalphLoopState>(s => s with
        {
            LastFailureOutput = output,
            Attempts = attempts
        });
    }
}
```

### Step 3 — The outer loop

The middleware handles termination, but the outer loop is what keeps calling `RunAsync`. When `IsTerminated` is set, the agent stops emitting events — the loop exits naturally.

```csharp
var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithToolkit<CodingTools>()             // placeholder — your toolkit with file read/write tools
    .WithMiddleware(new RalphLoopMiddleware(
        validator: async ct =>
        {
            var result = await RunTestsAsync(ct); // placeholder — your test runner integration
            return (result.ExitCode == 0, result.Output);
        },
        maxAttempts: 10
    ))
    .BuildAsync();

var sessionId = await agent.CreateSessionAsync();

// Keep running until the validator passes or maxAttempts is hit
while (true)
{
    var terminated = false;

    await foreach (var evt in agent.RunAsync(
        "Fix the code until all tests pass.",
        sessionId: sessionId))
    {
        switch (evt)
        {
            case TextDeltaEvent delta:
                Console.Write(delta.Text);
                break;

            case MessageTurnFinishedEvent finished when finished.TerminationReason is not null:
                Console.WriteLine($"\n\n[{finished.TerminationReason}]");
                terminated = true;
                break;
        }
    }

    if (terminated) break;
}
```

---

## What makes this different from the bash version

| | Bash `while` loop | Middleware loop |
|---|---|---|
| Context per attempt | Fresh (blank) | Carries failure output forward |
| Visibility | None | Full event stream per attempt |
| Stopping | Ctrl+C | Validator result or attempt limit |
| Attempt count | No | Tracked in state |
| Failure reason | Lost | Injected into next turn |

The middleware version gives the agent something the bash version doesn't: **it knows why the previous attempt failed**. That's often the difference between an agent that converges in 3 attempts and one that spins for 10.

---

## Variations

**Different validators** — swap `RunTestsAsync` for anything with a pass/fail signal:

```csharp
// Build
validator: async ct => {
    var r = await RunBuildAsync(ct);
    return (r.ExitCode == 0, r.Output);
}

// Linter
validator: async ct => {
    var r = await RunLinterAsync(ct);
    return (!r.HasErrors, r.Report);
}

// Type checker
validator: async ct => {
    var r = await RunTypeCheckAsync(ct);
    return (r.ErrorCount == 0, r.Errors);
}
```

**Escalating pressure** — increase urgency as attempts mount:

```csharp
public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken ct)
{
    var state = context.GetMiddlewareState<RalphLoopState>();
    if (state?.LastFailureOutput is null) return Task.CompletedTask;

    var urgency = state.Attempts >= 5
        ? "You have been failing for several attempts. Be methodical — read the error carefully before changing anything."
        : "Previous attempt failed.";

    context.ConversationHistory.Add(new ChatMessage(
        ChatRole.System,
        $"{urgency}\n\nOutput:\n{state.LastFailureOutput}"
    ));

    return Task.CompletedTask;
}
```

---

## When to use it

Ralph loops work best when:

- The success condition is **externally verifiable** — tests pass, build succeeds, linter is clean
- The task is **iterative by nature** — writing code, fixing bugs, generating configs
- **Failure output is meaningful** — the agent can read an error message and act on it

They work poorly when:

- There's no external validator — the agent has to judge its own output (use MultiAgent with a Verifier instead)
- Each attempt is expensive — add a low `maxAttempts` and be conservative
- The task isn't self-correcting — some problems require a fundamentally different approach, not another retry
