# Agents Overview

An **agent** is an AI-powered program that can think, plan, and take actions to accomplish tasks. Instead of just responding to your input, the agent can call tools, evaluate results, and decide what to do next.

## What Can Agents Do?

Agents can:
- **Understand context** - Process complex requests and conversations
- **Call tools** - Execute functions to interact with files, APIs, databases, etc.
- **Reason about results** - Evaluate what happened and decide next steps
- **Iterate** - Keep trying different approaches until the task is complete
- **Remember context** - Maintain conversation history across turns

## The Agent Loop

The core agent loop involves calling the LLM, letting it choose tools to execute, and finishing when no more tools are needed:

```
User Message
    ↓
Call LLM
    ↓
Execute Tools (if LLM requested any)
    ↓
Call LLM again 
    ↓
Execute Tools (if needed)
    ↓
Final Response (no more tools needed)
```
## Your First Agent

### Step 1: Install NuGet Package

```bash
dotnet add package HPD.Agent
```

### Step 2: Create an Agent

```csharp
using HPD.Agent;

// Create and configure an agent
// API key is auto-detected from OPENAI_API_KEY environment variable
var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .WithInstructions("You are a helpful assistant.")
    .BuildAsync();
```

### Step 2b: Create an Agent with Tools

Tools give your agent the ability to take actions. Define tools as classes with methods:

```csharp
using HPD.Agent;

// Define a tool with functions
public class CalculatorTool
{
    [AIFunction(Description = "Add two numbers")]
    public int Add(int a, int b) => a + b;

    [AIFunction(Description = "Multiply two numbers")]
    public int Multiply(int a, int b) => a * b;
}

// Create an agent with tools
var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .WithInstructions("You are a helpful assistant with math capabilities.")
    .WithToolkit<CalculatorTool>()  // Register the tool
    .BuildAsync();
```

Now the agent can call `Add` and `Multiply` when needed. For more info on tools: [03 Tool Calling.md](03%20Tool%20Calling.md)

### Step 2c: Create an Agent with Middleware

Middleware lets you intercept and customize agent behavior. Add middleware to log activity, enforce permissions, retry on failure, etc.:

```csharp
using HPD.Agent;
using HPD.Agent.Middleware;

// Create middleware to log what the agent does
public class LoggingMiddleware : IAgentMiddleware
{
    public Task BeforeFunctionAsync(AgentMiddlewareContext context, CancellationToken ct)
    {
        Console.WriteLine($"Calling: {context.Function?.Name}");
        return Task.CompletedTask;
    }

    public Task AfterFunctionAsync(AgentMiddlewareContext context, CancellationToken ct)
    {
        Console.WriteLine($"Result: {context.FunctionResult}");
        return Task.CompletedTask;
    }
}

// Create an agent with middleware
var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .WithInstructions("You are a helpful assistant.")
    .WithToolkit<CalculatorTool>()
    .WithMiddleware(new LoggingMiddleware())  // Add the middleware
    .BuildAsync();
```

Now every time the agent calls a function, your middleware will log it. For more info on middleware: [Middleware](04%20Middleware.md)

### Step 3a: Run the Agent (Stateless)

```csharp
// Run the agent - no session needed for simple queries
var message = "Add 5 and 3";  // Uses the CalculatorTool
await foreach (var evt in agent.RunAsync(message))
{
    if (evt is TextDeltaEvent textDelta)
        Console.Write(textDelta.Text);
}
```

That's it! Your agent is running and can use tools.

### Step 3b: Run the Agent (Stateful)

For multi-turn conversations, create a session first, then pass its ID to each `RunAsync` call:

```csharp
// Create the session once
await agent.CreateSessionAsync("user-123");

// Each call with the same sessionId continues the conversation
await foreach (var evt in agent.RunAsync("Add 10 and 20", sessionId: "user-123"))
{
    if (evt is TextDeltaEvent textDelta)
        Console.Write(textDelta.Text);
}
Console.WriteLine();

// Agent remembers the previous result
await foreach (var evt in agent.RunAsync("Now multiply the result by 5", sessionId: "user-123"))
{
    if (evt is TextDeltaEvent textDelta)
        Console.Write(textDelta.Text);
}
```

## Key Concepts

### Sessions
A **session** is a conversation container identified by a `sessionId` string. Create it once with `CreateSessionAsync`, then pass the same `sessionId` to each `RunAsync` call — the agent tracks history automatically. Add a session store to persist conversations across restarts, and use `ForkBranchAsync` to explore alternative conversation paths.

→ Learn more: [02 Multi-Turn Conversations.md](02%20Multi-Turn%20Conversations.md)

### Tools
Tools are functions your agent can call. Add them to give your agent capabilities - read files, call APIs, interact with databases, etc.

→ Learn more: [03 Tool Calling.md](03%20Tool%20Calling.md)

### Configuration
Agents can be customized extensively - error handling, history reduction, validation, observability, and much more.

→ Learn more: [01 Customizing an Agent.md](01%20Customizing%20an%20Agent.md)

### Middleware
Middleware intercepts and processes events throughout the agent execution pipeline. Use middleware for cross-cutting concerns like history reduction, error handling, logging, and more.

→ Learn more: [04 Middleware.md](04%20Middleware.md)

### Memory
Agents can leverage memory systems to persist and recall information across conversations. Use memory for context awareness, learning from past interactions, and maintaining long-term state.

→ Learn more: [06 Memory.md](06%20Memory.md)

## Event-Driven Architecture

The agent doesn't just return a final answer - it streams **events** as it works:

```csharp
await agent.CreateSessionAsync("user-123");

await foreach (var evt in agent.RunAsync("Do something", sessionId: "user-123"))
{
    if (evt is TextDeltaEvent textDelta)
    {
        Console.Write(textDelta.Text);  // Streaming text output
    }
}
```

This means you can:
- Show real-time progress to users
- Cancel mid-execution
- Respond to what the agent is doing
- Build interactive experiences

→ Complete event reference: [05 Event Handling.md](05%20Event%20Handling.md)

## Stateless vs. Persistent

### In-Memory (Default)
```csharp
// Create the session, then run — history lives in memory for the lifetime of the process
await agent.CreateSessionAsync("user-123");
await foreach (var evt in agent.RunAsync("First message", sessionId: "user-123")) { }
await foreach (var evt in agent.RunAsync("Second message", sessionId: "user-123")) { }
// Session is lost when process ends
```

Use this for: Testing, scripts, one-off tasks.

### Persistent
```csharp
var agent = await new AgentBuilder()
    .WithSessionStore("./sessions")  // auto-saves after every turn
    .BuildAsync();

// Create the session once — survives process restarts
await agent.CreateSessionAsync("user-123");
await foreach (var evt in agent.RunAsync("Message", sessionId: "user-123")) { }
```

Use this for: Web apps, long-running services, conversation resumption.

→ Details: [02 Multi-Turn Conversations.md](02%20Multi-Turn%20Conversations.md)

## Next Steps

1. **Customize behavior** - Configure error handling, history reduction, etc.
    - See: [01 Customizing an Agent.md](01%20Customizing%20an%20Agent.md)

2. **Persist conversations** - Save and resume sessions
    - See: [02 Multi-Turn Conversations.md](02%20Multi-Turn%20Conversations.md)

3. **Add tools** - Give your agent capabilities
    - See: [03 Tool Calling.md](03%20Tool%20Calling.md)

4. **Extend with middleware** - Add cross-cutting concerns like history reduction, logging, and error handling
    - See: [04 Middleware.md](04%20Middleware.md)

5. **Handle events** - Respond to agent activity and streaming output
    - See: [05 Event Handling.md](05%20Event%20Handling.md)

6. **Add memory** - Enable your agent to learn from past interactions and maintain long-term context
    - See: [06 Memory.md](06%20Memory.md)
