# Writing Middleware

> Intercept, observe, and control the agent at every step — from a single hook to stateful multi-hook logic.

Middleware sits inside the agent loop. Every LLM call, every tool execution, every turn — middleware sees it and can act on it. The interface is `IAgentMiddleware`. Implement only the hooks you need; everything else is a no-op by default.

---

## Section 1 — Simple middleware

One hook, one job. No state, no events — just intercept and act.

### Register middleware

```csharp
var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithMiddleware(new MyMiddleware())
    .BuildAsync();
```

That's the registration pattern for everything that follows.

---

### Example 1: Turn logger

Log when a turn starts and ends. Uses two hooks — `BeforeMessageTurnAsync` fires once before the agent processes the user's message, `AfterMessageTurnAsync` fires once after the final response is ready.

```csharp
public class TurnLoggerMiddleware : IAgentMiddleware
{
    public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken ct)
    {
        Console.WriteLine($"→ Turn started: \"{context.UserMessage.Text}\"");
        return Task.CompletedTask;
    }

    public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken ct)
    {
        Console.WriteLine($"← Turn finished: \"{context.FinalResponse.Text?[..Math.Min(80, context.FinalResponse.Text.Length)]}...\"");
        return Task.CompletedTask;
    }
}
```

`AfterMessageTurnAsync` always runs — even if the turn failed. Safe to use for logging and cleanup.

---

### Example 2: RAG injection

Fetch relevant context from a store and inject it into the conversation before the agent sees the user's message. One hook, one job.

```csharp
public class RAGMiddleware : IAgentMiddleware
{
    private readonly IVectorStore _store;

    public RAGMiddleware(IVectorStore store) => _store = store;

    public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken ct)
    {
        var results = await _store.SearchAsync(context.UserMessage.Text, topK: 3, ct);

        if (results.Count == 0)
            return;

        var context_text = string.Join("\n\n", results.Select(r => r.Content));

        context.ConversationHistory.Insert(0, new ChatMessage(
            ChatRole.System,
            $"Relevant context:\n{context_text}"
        ));
    }
}
```

`ConversationHistory` is mutable — anything you insert here is visible to the LLM for this turn.

---

### Example 3: Dynamic retry instructions

The agent sometimes gets stuck repeating the same failed approach. Inject a nudge on the second iteration onwards. One hook.

```csharp
public class RetryInstructionMiddleware : IAgentMiddleware
{
    public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken ct)
    {
        if (context.Iteration == 0)
            return Task.CompletedTask;

        context.Messages.Insert(0, new ChatMessage(
            ChatRole.System,
            $"Attempt {context.Iteration + 1}: your previous approach didn't work. Try something different."
        ));

        return Task.CompletedTask;
    }
}
```

`BeforeIterationAsync` fires before every LLM call within a turn — `Iteration` is 0-based, so `Iteration == 0` is the first call, `Iteration == 1` is the first retry.

---

### Picking the right hook

| I want to... | Hook |
|---|---|
| Inject context once per user message | `BeforeMessageTurnAsync` |
| Extract memory or log after the full response | `AfterMessageTurnAsync` |
| Modify the prompt before each LLM call | `BeforeIterationAsync` |
| Validate or block tool calls as a batch | `BeforeToolExecutionAsync` |
| Check permissions per function | `BeforeFunctionAsync` |
| Add retry/timeout around a function | `WrapFunctionCallAsync` |
| React to any error, anywhere | `OnErrorAsync` |

Full hook reference: [04.1 Middleware Lifecycle](../Middleware/04.1%20Middleware%20Lifecycle.md).

---

## Section 2 — Stateful middleware

Some problems need hooks to talk to each other. The state has to live somewhere.

### Why you can't use instance fields

The obvious approach doesn't work:

```csharp
public class BrokenMiddleware : IAgentMiddleware
{
    private int _errorCount = 0; //  shared across all parallel RunAsync calls

    public Task OnErrorAsync(ErrorContext context, CancellationToken ct)
    {
        _errorCount++; // race condition
        return Task.CompletedTask;
    }
}
```

Multiple `RunAsync` calls can run in parallel on the same agent instance. An instance field is shared across all of them — you get race conditions and incorrect counts.

The fix is middleware state: typed, immutable, scoped per run.

---

### Defining state

Mark a record with `[MiddlewareState]`. The source generator wires it up automatically:

```csharp
[MiddlewareState]
public sealed record ErrorCountState
{
    public int ConsecutiveFailures { get; init; }
}
```

Requirements:
- Must be a `record`
- Use `{ get; init; }` properties (immutable)
- All properties must be JSON-serializable

---

### Example: Error tracker

Track consecutive failures. Terminate the agent if too many happen in a row. Reset the count when a turn succeeds.

This genuinely requires two hooks and state — you can't do it with one:

- `OnErrorAsync` increments the count when something goes wrong
- `AfterIterationAsync` resets the count when everything succeeds

```csharp
[MiddlewareState]
public sealed record ErrorCountState
{
    public int ConsecutiveFailures { get; init; }
}

public class ErrorTrackerMiddleware : IAgentMiddleware
{
    private readonly int _maxFailures;

    public ErrorTrackerMiddleware(int maxFailures = 3)
    {
        _maxFailures = maxFailures;
    }

    // Fires on any error — model call, tool call, anything
    public Task OnErrorAsync(ErrorContext context, CancellationToken ct)
    {
        context.UpdateMiddlewareState<ErrorCountState>(s => s with
        {
            ConsecutiveFailures = s.ConsecutiveFailures + 1
        });

        var failures = context.GetMiddlewareState<ErrorCountState>()!.ConsecutiveFailures;

        if (failures >= _maxFailures)
        {
            context.UpdateState(s => s with
            {
                IsTerminated = true,
                TerminationReason = $"Too many consecutive failures ({failures})"
            });
        }

        return Task.CompletedTask;
    }

    // Fires after all tools complete in an iteration
    public Task AfterIterationAsync(AfterIterationContext context, CancellationToken ct)
    {
        if (context.AllToolsSucceeded)
        {
            context.UpdateMiddlewareState<ErrorCountState>(s => s with
            {
                ConsecutiveFailures = 0
            });
        }

        return Task.CompletedTask;
    }
}
```

**How `UpdateMiddlewareState` works:**
- Auto-instantiates the state record if it doesn't exist yet — no `?? new()` needed
- Uses `with` expressions for immutable updates — the original record is never mutated
- Updates are immediately visible to subsequent hooks in the same turn

**How `UpdateState` works:**
- For changes to core agent state — `IsTerminated`, `TerminationReason`, and similar
- Use this when you need to affect agent control flow, not just track your own data

Register it:

```csharp
var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithToolkit<MyTools>()
    .WithMiddleware(new ErrorTrackerMiddleware(maxFailures: 3))
    .BuildAsync();
```

---

### State across turns

By default, middleware state resets at the end of each `RunAsync` call. If you need state to survive across turns — a cache, user preferences, long-term metrics — mark it persistent:

```csharp
[MiddlewareState(Persistent = true)]
public sealed record UserPreferencesState
{
    public string? PreferredLanguage { get; init; }
}
```

The framework saves and restores it automatically between runs on the same session. Use transient (the default) for safety state like error counts — you want those to reset clean every run.

---

## Going further

- **Events** — middleware can emit events to the UI and wait for responses (human-in-the-loop, permission prompts). See [04.3 Middleware Events](../Middleware/04.3%20Middleware%20Events.md).
- **Built-in middleware** — circuit breakers, PII redaction, history reduction, retry, logging — ready to register. See [04.4 Built-in Middleware](../Middleware/04.4%20Built-in%20Middleware.md).
- **Complete hook reference** — every hook, every context property, execution order. See [04.1 Middleware Lifecycle](../Middleware/04.1%20Middleware%20Lifecycle.md).
