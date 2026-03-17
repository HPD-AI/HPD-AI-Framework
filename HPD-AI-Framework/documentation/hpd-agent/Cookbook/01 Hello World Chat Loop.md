# Hello World: Chat Loop

> Build a working console chatbot in under 2 minutes.

---

## Step 1 — Install the package

```bash
dotnet add package HPD.Agent
```

---

## Step 2 — Create the agent

There are three ways to create an agent. Pick the one that fits your situation.

### Builder (quickest)

```csharp
using HPD.Agent;

var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithInstructions("You are a helpful assistant.")
    .BuildAsync();
```

Good for scripts, prototypes, and simple apps. Swap the provider key and model name to use any supported provider.

### Authentication

The framework resolves your API key automatically in this order:

1. **Environment variable** — recommended for development
   ```bash
   export ANTHROPIC_API_KEY="sk-ant-..."
   ```
   No code change needed — the framework picks it up automatically.

2. **`appsettings.json`** — recommended for production
   ```json
   { "anthropic": { "ApiKey": "sk-ant-..." } }
   ```

3. **Explicit in the builder** — for testing only, never commit this
   ```csharp
   .WithAnthropic(model: "claude-sonnet-4-5", apiKey: "sk-ant-...")
   ```

> Use provider-specific builder methods like `.WithAnthropic()` or `.WithOpenAI()` when you need authentication options or provider-specific settings. `.WithProvider()` works for the simple case.

### Config object

```csharp
var config = new AgentConfig
{
    Name = "MyAgent",
    SystemInstructions = "You are a helpful assistant.",
    Provider = new ProviderConfig
    {
        ProviderKey = "anthropic",
        ModelName = "claude-sonnet-4-5"
    }
};

var agent = await config.BuildAsync();
```

Good when you want to store or share configuration as data — serialize it, load it from a database, pass it around.

### JSON file

```json
{
    "Name": "MyAgent",
    "SystemInstructions": "You are a helpful assistant.",
    "Provider": {
        "ProviderKey": "anthropic",
        "ModelName": "claude-sonnet-4-5"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("agent-config.json");
```

Good for production apps where configuration lives outside the code.

All three produce the same agent — this cookbook uses the builder for brevity.

---

## Step 3 — Create a session

```csharp
// Let the framework generate a GUID
var sessionId = await agent.CreateSessionAsync();

// Or provide your own ID — useful when you already have a user/conversation ID
var sessionId = await agent.CreateSessionAsync("user-123");
```

A session holds the conversation history. Every message you send and every response the agent gives is stored under this ID — without it, the agent has no memory between turns. Always capture the return value and pass it to every `RunAsync()` call.

`CreateSessionAsync` throws if the session ID already exists, so session creation is always intentional.

---

## Step 4 — The chat loop

```csharp
while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();
    if (string.IsNullOrEmpty(input)) break;

    Console.Write("Agent: ");

    await foreach (var evt in agent.RunAsync(input, sessionId: sessionId))
    {
        // handle events
    }
}
```

`agent.RunAsync()` returns an `IAsyncEnumerable<AgentEvent>` — events stream in real time as the agent works. The loop continues until the user presses Enter on an empty line.

---

## Step 5 — Handle events

This is where you choose how much detail to show. Start minimal, add granularity as needed.

### Minimal — just the essentials

All you need to display a response and know when it's done:

```csharp
switch (evt)
{
    case TextDeltaEvent delta:
        Console.Write(delta.Text);
        break;

    case MessageTurnFinishedEvent:
        Console.WriteLine("\n");
        break;
}
```

### Full granularity — the complete lifecycle

Every event type, with start and end boundaries for each phase:

```csharp
switch (evt)
{
    // ── Turn lifecycle (start) ───────────────────────────────
    // Fires once when the agent begins processing the message
    case MessageTurnStartedEvent:
        Console.WriteLine("[Turn started]");
        break;

    // ── Reasoning ───────────────────────────────────────────
    // Only fires on models with extended thinking enabled
    case ReasoningMessageStartEvent:
        Console.Write("[Thinking: ");
        break;

    case ReasoningDeltaEvent reasoning:
        Console.Write(reasoning.Text);
        break;

    case ReasoningMessageEndEvent:
        Console.WriteLine("]");
        break;

    // ── Text ────────────────────────────────────────────────
    // Wraps the streaming text response
    case TextMessageStartEvent:
        Console.WriteLine("[Message started]");
        break;

    case TextDeltaEvent delta:
        Console.Write(delta.Text);
        break;

    case TextMessageEndEvent:
        Console.WriteLine("[Message ended]");
        break;

    // ── Tool calls ──────────────────────────────────────────
    // Fires when the agent calls a function
    case ToolCallStartEvent toolStart:
        Console.WriteLine($"\n[Calling: {toolStart.Name}]");
        break;

    case ToolCallResultEvent toolResult:
        Console.WriteLine($"[Result: {toolResult.Result}]");
        break;

    // ── Turn lifecycle (end) ─────────────────────────────────
    // MessageTurnFinishedEvent = agent is fully done
    // Don't use AgentTurnFinishedEvent — that fires after each
    // internal LLM call, not at the end of the full response
    case MessageTurnFinishedEvent:
        Console.WriteLine("\n[Turn finished]");
        break;
}
```

**Event order in a typical turn:**

```
MessageTurnStartedEvent
  ReasoningMessageStartEvent
    ReasoningDeltaEvent (×N)
  ReasoningMessageEndEvent
  TextMessageStartEvent
    TextDeltaEvent (×N)
  TextMessageEndEvent
  ToolCallStartEvent
  ToolCallResultEvent
  TextMessageStartEvent       ← agent may emit more text after tool result
    TextDeltaEvent (×N)
  TextMessageEndEvent
MessageTurnFinishedEvent
```

> This covers the most common events. HPD-Agent emits 50+ event types in total — including permission requests, clarifications, structured output, streaming control, and full observability events. See the [Event Types Reference](/hpd-agent/Events/05.2%20Event%20Types%20Reference) for the complete list.

---

## Complete program

```csharp
using HPD.Agent;

var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithInstructions("You are a helpful assistant.")
    .BuildAsync();

var sessionId = await agent.CreateSessionAsync();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();
    if (string.IsNullOrEmpty(input)) break;

    Console.Write("Agent: ");

    await foreach (var evt in agent.RunAsync(input, sessionId: sessionId))
    {
        switch (evt)
        {
            case MessageTurnStartedEvent:
                Console.WriteLine("[Turn started]");
                break;

            case ReasoningMessageStartEvent:
                Console.Write("[Thinking: ");
                break;

            case ReasoningDeltaEvent reasoning:
                Console.Write(reasoning.Text);
                break;

            case ReasoningMessageEndEvent:
                Console.WriteLine("]");
                break;

            case TextMessageStartEvent:
                Console.WriteLine("[Message started]");
                break;

            case TextDeltaEvent delta:
                Console.Write(delta.Text);
                break;

            case TextMessageEndEvent:
                Console.WriteLine("[Message ended]");
                break;

            case ToolCallStartEvent toolStart:
                Console.WriteLine($"\n[Calling: {toolStart.Name}]");
                break;

            case ToolCallResultEvent toolResult:
                Console.WriteLine($"[Result: {toolResult.Result}]");
                break;

            case MessageTurnFinishedEvent:
                Console.WriteLine("\n[Turn finished]");
                break;
        }
    }
}
```

## Run it

```bash
dotnet run
```
