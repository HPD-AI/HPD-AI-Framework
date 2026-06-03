# Building Console Apps

> Get a console CLI running in under 2 minutes

HPD-Agent works natively in .NET console applications with no additional dependencies. Register `On<TEvent>()` handlers for output, then call `RunAsync(...)` with each user input.

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

agent
    .On<TextDeltaEvent>(e =>
    {
        Console.Write(e.Text);
        return ValueTask.CompletedTask;
    })
    .On<MessageTurnFinishedEvent>(_ =>
    {
        Console.WriteLine("\n");
        return ValueTask.CompletedTask;
    });

while (true)
{
    // Get user input
    Console.Write("You: ");
    var input = Console.ReadLine();
    if (string.IsNullOrEmpty(input)) break;

    // Send input - history is tracked automatically via sessionId.
    Console.Write("Agent: ");
    await agent.RunAsync(input, sessionId: sessionId);
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

## Alternative Transport: Kestrel Instead of stdin/stdout

The console app above reads from `Console.ReadLine()` and writes to `Console.Write()`. But Kestrel is built into the .NET SDK — you don't need a separate server or a frontend to use it. You can take the same console project and swap the transport: instead of stdin/stdout, the agent listens over HTTP using `HPD-Agent.AspNetCore`.

This is still a console application. The only difference is how input and output flow:

| Transport | Input | Output | Access |
|---|---|---|---|
| stdin/stdout | `Console.ReadLine()` | `Console.Write()` | Same machine, same terminal |
| Kestrel (HTTP) | Event envelope | SSE or WebSocket output events | Any HTTP client, over the network |

### Minimal Kestrel console app

Same project type — just add the package and replace the REPL loop with a Kestrel host:

```bash
dotnet add package HPD-Agent.AspNetCore
```

```csharp
using HPD.Agent.AspNetCore;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddHPDAgent(options =>
{
    options.ConfigureAgent = agent => agent
        .WithProvider("anthropic", "claude-sonnet-4-5")
        .WithInstructions("You are a helpful assistant.");
});

var app = builder.Build();
app.MapHPDAgentApi();  // sessions, branches, SSE, WebSocket, assets — all wired up
app.Run();
```

`MapHPDAgentApi()` registers the full REST + streaming API automatically. No frontend required — call it directly with curl or any HTTP client from day one, and add a frontend later if you need one.

For everything `MapHPDAgentApi()` exposes, see [**Building Web Apps**](08%20Building%20Web%20Apps.md).

## See Also

- [**Event Handling**](05%20Event%20Handling.md) - Handling output events
- [**Building Web Apps**](08%20Building%20Web%20Apps.md) - SSE streaming for web/mobile
