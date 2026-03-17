# Event Handling

> Understanding when the agent is thinking, calling tools, and finished responding

Every agent interaction emits a stream of events that tell you exactly what's happening: when the agent is generating text, calling tools, asking for permission, or finished responding. Event handling is essential for building responsive UIs and knowing when the agent is done.

## Why Events Matter

Events let you:
- **Stream responses** - Display text as it's generated, not after it's complete
- **Show progress** - Display "Calling calculator..." when tools are executing
- **Handle permissions** - Prompt users before executing sensitive operations
- **Know when done** - Stop loading spinners and re-enable input when the agent finishes

## Basic Event Loop

The fundamental pattern for consuming events is `await foreach`:

```csharp
await foreach (var evt in agent.RunAsync(messages))
{
    switch (evt)
    {
        case TextDeltaEvent delta:
            Console.Write(delta.Text);
            break;

        case MessageTurnFinishedEvent:
            Console.WriteLine("\n✓ Done");
            break;
    }
}
```

**Note:** Observability events (`IObservabilityEvent`) are disabled by default, so you don't need to filter them. See [Consuming Events](../Events/05.3%20Consuming%20Events.md#observability-events-disabled-by-default) if you need internal diagnostics.

## The Five Essential Event Types

Every application needs to handle these five categories:

### 1. Text Events
The agent's response to the user:

```csharp
case TextDeltaEvent delta:
    Console.Write(delta.Text);
    break;
```

### 2. Reasoning Events
Extended thinking (when enabled on models like Claude):

```csharp
case ReasoningDeltaEvent reasoning:
    Console.Write($"[Thinking: {reasoning.Text}]");
    break;
```

### 3. Tool Events
When the agent calls functions:

```csharp
case ToolCallStartEvent toolStart:
    Console.WriteLine($"\n[Calling: {toolStart.Name}]");
    break;

case ToolCallResultEvent toolResult:
    Console.WriteLine($"[Result: {toolResult.Result}]");
    break;
```

### 4. Turn Lifecycle Events

**  CRITICAL:** This is how you know when the agent is done:

```csharp
case MessageTurnFinishedEvent:
    Console.WriteLine("\n✓ Agent finished");
    // In a web UI: setIsLoading(false), enableInput()
    break;

case MessageTurnErrorEvent error:
    Console.WriteLine($"\n✗ Error: {error.ErrorMessage}");
    // Show error to user, conversation ends
    break;
```

**Common mistake:** Without handling `MessageTurnFinishedEvent`, your UI's loading spinner will never stop!

### 5. Permission Events

**  CRITICAL:** These events require TWO steps - receiving AND responding:

```csharp
case PermissionRequestEvent permission:
    // Step 1: Ask the user
    var approved = PromptUser($"Allow {permission.FunctionName}?");

    // Step 2: MUST send response or agent hangs!
    agent.SendMiddlewareResponse(permission.PermissionId,
        new PermissionResponseEvent
        {
            PermissionId = permission.PermissionId,
            Approved = approved
        });
    break;
```

**Common mistake:** Handling the event but forgetting to call `SendMiddlewareResponse()` causes the agent to hang until timeout!

## Complete Minimal Example

```csharp
using HPD.Agent;
using HPD.Agent.Events;

var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithInstructions("You are a helpful assistant.")
    .BuildAsync();

var messages = new List<ChatMessage>
{
    new ChatMessage { Role = "user", Content = "What is 2+2?" }
};

await foreach (var evt in agent.RunAsync(messages))
{
    // Filter observability events (prevents console spam)
    if (evt is IObservabilityEvent) continue;

    switch (evt)
    {
        // Stream text as it's generated
        case TextDeltaEvent delta:
            Console.Write(delta.Text);
            break;

        // Show reasoning (extended thinking)
        case ReasoningDeltaEvent reasoning:
            Console.Write($"[Thinking: {reasoning.Text}]");
            break;

        // Show tool execution
        case ToolCallStartEvent toolStart:
            Console.WriteLine($"\n[Calling tool: {toolStart.Name}]");
            break;

        case ToolCallResultEvent toolResult:
            Console.WriteLine($"[Result: {toolResult.Result}]");
            break;

        // Know when agent is done  
        case MessageTurnFinishedEvent:
            Console.WriteLine("\n✓ Agent finished");
            break;

        // Handle errors
        case MessageTurnErrorEvent error:
            Console.WriteLine($"\n✗ Error: {error.ErrorMessage}");
            break;

        // Handle permission requests  
        case PermissionRequestEvent permission:
            Console.Write($"\nAllow {permission.FunctionName}? (y/n): ");
            var input = Console.ReadLine();
            var approved = input?.ToLower() == "y";

            // MUST call SendMiddlewareResponse or agent hangs!
            agent.SendMiddlewareResponse(permission.PermissionId,
                new PermissionResponseEvent
                {
                    PermissionId = permission.PermissionId,
                    Approved = approved
                });
            break;
    }
}
```

## Understanding Turns

**  CRITICAL CONCEPT:** There are TWO levels of turns:

1. **Message Turn** (entire user interaction)
   - Starts when you call `RunAsync()`
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
