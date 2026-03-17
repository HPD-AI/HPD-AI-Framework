# Middleware

> Control and customize agent execution at every step

Middleware provides hooks into the agent execution pipeline, allowing you to intercept and modify behavior at key points. Middleware is useful for:

- **Tracking** - Logging, analytics, and debugging agent behavior
- **Transforming** - Modifying prompts, tool selection, and output formatting
- **Controlling** - Adding retries, fallbacks, and early termination logic
- **Guarding** - Applying permissions, rate limits, guardrails, and validation
- **Optimizing** - Caching LLM/function results, batching operations, resource management

Add middleware via the `AgentBuilder`:

```csharp
var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .WithToolkit<MyTools>()
    .WithMiddleware(new LoggingMiddleware())
    .WithMiddleware(new CircuitBreakerMiddleware())
    .BuildAsync();
```

## The Agent Loop

The core agent loop involves calling the LLM, letting it choose tools to execute, and finishing when no more tools are needed:

```
User Message
    ↓
Call LLM
    ↓
Execute Tools (if LLM requested any)
    ↓
Call LLM again (if needed)
    ↓
Final Response
```

Middleware exposes hooks **before and after each of these steps**, plus fine-grained hooks for individual function execution.

## Middleware Hooks

Middleware can intercept at multiple levels:

### Message Turn Level
Run once per user message. Use for context injection, memory retrieval, logging.

### Iteration Level  
Run per LLM call (loops if agent re-thinks). Use for history reduction, dynamic instructions, caching.

### Function Level
Run per function execution. Use for permissions, argument validation, retry logic, logging.

See [05.1 Middleware Lifecycle](../Middleware/04.1%20Middleware%20Lifecycle.md) for complete hook reference.

## Complete Middleware Guide

### 1. [Middleware Lifecycle](../Middleware/04.1%20Middleware%20Lifecycle.md)
Complete reference of all available hooks and context properties. Understand when each hook fires and what data is available.

### 2. [Middleware State](../Middleware/04.2%20Middleware%20State.md)
Manage state across function calls and iterations using a **strongly-typed, immutable state system**. Create custom state, access it, and update it.

### 3. [Middleware Events](../Middleware/04.3%20Middleware%20Events.md)
Emit events and wait for responses. Build interactive middleware patterns like permissions, approvals, and human-in-the-loop workflows.

### 4. [Built-in Middleware](../Middleware/04.4%20Built-in%20Middleware.md)
Ready-to-use middleware for common use cases: rate limiting, circuit breakers, error tracking, permissions, and more.

### 5. [Custom Middleware](../Middleware/04.5%20Custom%20Middleware.md)
Build your own middleware from scratch. Learn patterns, state management, event coordination, and best practices.

## Quick Example

```csharp
public class SimpleRetryMiddleware : IAgentMiddleware
{
    public async IAsyncEnumerable<IStreamEvent> WrapModelCallStreamingAsync(
        ModelRequest request,
        Func<ModelRequest, IAsyncEnumerable<IStreamEvent>> handler,
        IAgentContext context)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await foreach (var evt in handler(request))
                    yield return evt;
                yield break;
            }
            catch (HttpRequestException) when (i < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)), context.CancellationToken);
            }
        }
        await foreach (var evt in handler(request))
            yield return evt;
    }
}
```

Register it:

```csharp
var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .WithToolkit<MyTools>()
    .WithMiddleware(new SimpleRetryMiddleware())
    .WithMiddleware(new CircuitBreakerMiddleware())
    .BuildAsync();
```

## Key Features

  **Typed contexts** - Compile-time safety, zero NULL properties
  **Immutable requests** - `ModelRequest`, `FunctionRequest` preserve originals
  **Dual pattern** - Simple + streaming LLM hooks
  **Centralized errors** - `OnErrorAsync` handles all error types
  **Immediate state updates** - Updates visible instantly

## Next Steps

- **Start here:** [05.1 Middleware Lifecycle](../Middleware/04.1%20Middleware%20Lifecycle.md) - See all available hooks
- **Store data:** [05.2 Middleware State](../Middleware/04.2%20Middleware%20State.md) - Create custom state with `[MiddlewareState]`
- **Build interactive:** [05.3 Middleware Events](../Middleware/04.3%20Middleware%20Events.md) - Emit events and wait for responses
- **Use ready-made:** [05.4 Built-in Middleware](../Middleware/04.4%20Built-in%20Middleware.md) - Plug in predefined middleware
- **Build custom:** [05.5 Custom Middleware](../Middleware/04.5%20Custom%20Middleware.md) - Implement your own logic
- **Related:** [03 Tool Calling.md](03%20Tool%20Calling.md) - Tools that middleware can control
- **Related:** [02 Multi-Turn Conversations.md](02%20Multi-Turn%20Conversations.md) - State persistence across sessions
