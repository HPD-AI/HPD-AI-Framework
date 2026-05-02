# Event Handling

> Understanding when the agent is thinking, calling tools, and finished responding

Every agent interaction emits a stream of events that tell you exactly what's happening: when the agent is generating text, calling tools, asking for permission, or finished responding. Event handling is essential for building responsive UIs and knowing when the agent is done.

## Why Events Matter

Events let you:
- **Stream responses** - Display text as it's generated, not after it's complete
- **Show progress** - Display "Calling calculator..." when tools are executing
- **Handle permissions** - Prompt users before executing sensitive operations
- **Know when done** - Stop loading spinners and re-enable input when the agent finishes

## Basic Output Handlers

The fundamental pattern is to register output handlers, then send input with `RunAsync(...)`:

```csharp
agent
    .On<TextDeltaEvent>(e =>
    {
        Console.Write(e.Text);
        return ValueTask.CompletedTask;
    })
    .On<MessageTurnFinishedEvent>(_ =>
    {
        Console.WriteLine("\nDone");
        return ValueTask.CompletedTask;
    });

await agent.RunAsync("What is 2+2?");
```

`RunAsync` is the input method. `On<TEvent>()` is the output method. This keeps input events, permission responses, interruptions, and streaming output in the same event model.

**Note:** Observability events (`IObservabilityEvent`) are disabled by default, so normal app handlers only see the agent events they registered for. See [Consuming Events](../Events/05.3%20Consuming%20Events.md#observability-events-disabled-by-default) if you need internal diagnostics.

## The Five Essential Event Types

Every application needs to handle these five categories:

### 1. Text Events
The agent's response to the user:

```csharp
agent.On<TextDeltaEvent>(e =>
{
    Console.Write(e.Text);
    return ValueTask.CompletedTask;
});
```

### 2. Reasoning Events
Extended thinking (when enabled on models like Claude):

```csharp
agent.On<ReasoningDeltaEvent>(e =>
{
    Console.Write($"[Thinking: {e.Text}]");
    return ValueTask.CompletedTask;
});
```

### 3. Tool Events
When the agent calls functions:

```csharp
agent.On<ToolCallStartEvent>(e =>
{
    Console.WriteLine($"\n[Calling: {e.Name}]");
    return ValueTask.CompletedTask;
});

agent.On<ToolCallResultEvent>(e =>
{
    Console.WriteLine($"[Result: {e.Result}]");
    return ValueTask.CompletedTask;
});
```

### 4. Turn Lifecycle Events

**  CRITICAL:** This is how you know when the agent is done:

```csharp
agent.On<MessageTurnFinishedEvent>(_ =>
{
    Console.WriteLine("\nAgent finished");
    // In a web UI: setIsLoading(false), enableInput()
    return ValueTask.CompletedTask;
});

agent.On<MessageTurnErrorEvent>(e =>
{
    Console.WriteLine($"\nError: {e.ErrorMessage}");
    // Show error to user, conversation ends
    return ValueTask.CompletedTask;
});
```

**Common mistake:** Without handling `MessageTurnFinishedEvent`, your UI's loading spinner will never stop!

### 5. Permission Events

**  CRITICAL:** These events require TWO steps - receiving AND responding:

```csharp
agent.On<PermissionRequestEvent>(async e =>
{
    // Step 1: Ask the user
    var approved = PromptUser($"Allow {e.FunctionName}?");

    // Step 2: MUST send a response event or the agent waits until timeout.
    await agent.RunAsync(new PermissionResponseEvent(
        e.PermissionId,
        e.SourceName,
        approved));
});
```

**Common mistake:** Handling the event but forgetting to send a `PermissionResponseEvent` causes the agent to wait until timeout.

## Complete Minimal Example

```csharp
using HPD.Agent;
using HPD.Agent.Events;

var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithInstructions("You are a helpful assistant.")
    .BuildAsync();

agent
    .On<TextDeltaEvent>(e =>
    {
        Console.Write(e.Text);
        return ValueTask.CompletedTask;
    })
    .On<ReasoningDeltaEvent>(e =>
    {
        Console.Write($"[Thinking: {e.Text}]");
        return ValueTask.CompletedTask;
    })
    .On<ToolCallStartEvent>(e =>
    {
        Console.WriteLine($"\n[Calling tool: {e.Name}]");
        return ValueTask.CompletedTask;
    })
    .On<ToolCallResultEvent>(e =>
    {
        Console.WriteLine($"[Result: {e.Result}]");
        return ValueTask.CompletedTask;
    })
    .On<MessageTurnFinishedEvent>(_ =>
    {
        Console.WriteLine("\nAgent finished");
        return ValueTask.CompletedTask;
    })
    .On<MessageTurnErrorEvent>(e =>
    {
        Console.WriteLine($"\nError: {e.ErrorMessage}");
        return ValueTask.CompletedTask;
    })
    .On<PermissionRequestEvent>(async e =>
    {
        Console.Write($"\nAllow {e.FunctionName}? (y/n): ");
        var input = Console.ReadLine();
        var approved = input?.ToLowerInvariant() == "y";

        await agent.RunAsync(new PermissionResponseEvent(
            e.PermissionId,
            e.SourceName,
            approved));
    });

await agent.RunAsync("What is 2+2?");
```

## Understanding Turns

**  CRITICAL CONCEPT:** There are TWO levels of turns:

1. **Message Turn** (entire user interaction)
   - Starts when you call `RunAsync(...)`
   - Ends when `MessageTurnFinishedEvent` fires
   - This is what your UI should track!

2. **Agent Turn** (internal LLM calls)
   - The agent may call the LLM multiple times internally
   - You usually ignore these events unless debugging
   - Events: `AgentTurnStartedEvent`, `AgentTurnFinishedEvent`

**Common mistake:** Stopping the loading spinner on `AgentTurnFinishedEvent` instead of `MessageTurnFinishedEvent` causes the UI to show "done" too early while the agent is still working!


## Next Steps

This covers the essentials for building responsive agent applications. For more advanced scenarios:

### Building Applications
- [**Building Console Apps**](07%20Building%20Console%20Apps.md) - Complete console CLI patterns
- [**Building Web Apps**](08%20Building%20Web%20Apps.md) - SSE streaming, TypeScript client setup

### Detailed Event Documentation
- [**Events Overview**](../Events/05.1%20Events%20Overview.md) - Event lifecycle, categories, flow diagrams
- [**Event Types Reference**](../Events/05.2%20Event%20Types%20Reference.md) - Complete listing of all 50+ event types
- [**Consuming Events**](../Events/05.3%20Consuming%20Events.md) - Advanced patterns, filtering, error handling
- [**Bidirectional Events**](../Events/05.6%20Bidirectional%20Events.md) - Request/response patterns, clarifications
- [**Streaming & Cancellation**](../Events/05.5%20Streaming%20%26%20Cancellation.md) - Interruption, graceful shutdown

### Platform-Specific Guides
- [**Building Console Apps**](07%20Building%20Console%20Apps.md) - Console patterns, user prompts, Ctrl+C handling
- [**Building Web Apps**](08%20Building%20Web%20Apps.md) - SSE setup, React patterns, TypeScript client
