# Building Console Apps

> Get a console CLI running in under 2 minutes

HPD-Agent works natively in .NET console applications with no additional dependencies. Use `await foreach` to consume events and build interactive command-line tools.

## Quick Start

### 1. Install the Package

```bash
dotnet add package HPD.Agent
```

### 2. Create a Minimal Console App

```csharp
using HPD.Agent;
using HPD.Agent.Events;

// Configure the agent
var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithInstructions("You are a helpful assistant.")
    .BuildAsync();

// Create a session to track conversation history
var sessionId = await agent.CreateSessionAsync();

while (true)
{
    // Get user input
    Console.Write("You: ");
    var input = Console.ReadLine();
    if (string.IsNullOrEmpty(input)) break;

    // Stream agent response — history is tracked automatically via sessionId
    Console.Write("Agent: ");
    await foreach (var evt in agent.RunAsync(input, sessionId: sessionId))
    {
        switch (evt)
        {
            case TextDeltaEvent delta:
                Console.Write(delta.Text);
                break;

            case MessageTurnFinishedEvent:
                Console.WriteLine("\n");
                break;
        }
    }
}
```

### 3. Run It

```bash
dotnet run
```

That's it! You now have a working console agent.

## Next Steps

This basic example gets you started, but production console apps need:
- Tool execution indicators
- Permission prompts
- Error handling
- Ctrl+C cancellation
- Multi-turn conversation management

For complete patterns and best practices, see:

- [**Event Handling**](05%20Event%20Handling.md) - Understanding the event stream
- [**Middleware**](04%20Middleware.md) - Adding hooks and custom logic
- [**Bidirectional Events**](../Events/05.6%20Bidirectional%20Events.md) - Handling user prompts and clarifications
- [**Streaming & Cancellation**](../Events/05.5%20Streaming%20%26%20Cancellation.md) - Ctrl+C handling and graceful shutdown

## See Also

- [**Event Handling**](05%20Event%20Handling.md) - Understanding the event stream
- [**Building Web Apps**](08%20Building%20Web%20Apps.md) - SSE streaming for web/mobile
