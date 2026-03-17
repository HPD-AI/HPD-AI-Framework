# Multi-Turn Conversations

HPD-Agent tracks conversation history automatically. Create a session once, then pass its `sessionId` to each `RunAsync` call — the agent handles everything else internally.

## Basic Usage

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic("your-api-key")
    .BuildAsync();

// Create the session explicitly — must be done before the first RunAsync
await agent.CreateSessionAsync("user-123");

// First call — session exists, agent runs and appends to history
await foreach (var evt in agent.RunAsync("Hello!", sessionId: "user-123"))
{
    if (evt is TextDeltaEvent text)
        Console.Write(text.Text);
}

// Second call — session already has history, conversation continues
await foreach (var evt in agent.RunAsync("What did I just say?", sessionId: "user-123"))
{
    if (evt is TextDeltaEvent text)
        Console.Write(text.Text);
}
```

`CreateSessionAsync` throws if the session already exists — session IDs are unique and creation is always intentional. `RunAsync` throws `SessionNotFoundException` if the session has not been created.

By default, conversation history lives in memory and is lost when the process ends.

---

## Persistence

To persist conversations across restarts, configure a session store on the builder:

```csharp
var agent = await new AgentBuilder()
    .WithAnthropic("your-api-key")
    .WithSessionStore("./sessions")  // file-based, auto-saves after every turn
    .BuildAsync();

// Create the session — saved to store immediately
await agent.CreateSessionAsync("user-123");

// Run — history is persisted automatically after each turn
await foreach (var evt in agent.RunAsync("Hello!", sessionId: "user-123")) { }
```

The built-in `JsonSessionStore` stores sessions as JSON files under the given path. For production use, implement `ISessionStore` to back sessions with any storage system — SQL, Redis, MongoDB, etc.:

```csharp
public class RedisSessionStore : ISessionStore
{
    // Implement LoadSessionAsync, SaveSessionAsync, LoadBranchAsync, SaveBranchAsync, etc.
}

var agent = await new AgentBuilder()
    .WithAnthropic("your-api-key")
    .WithSessionStore(new RedisSessionStore(connectionString))
    .BuildAsync();
```

See the [ISessionStore reference](#isessionstore) below for the full interface.

### Auto-Save Behaviour

When you call `WithSessionStore(store)` or `WithSessionStore(path)`, auto-save is **enabled by default**. The session and its messages are saved after every turn completes.

To disable auto-save and save manually:

```csharp
.WithSessionStore(store, persistAfterTurn: false)
```

---

## Crash Recovery

Crash recovery is **automatic** whenever a session store is configured. During execution, the agent saves an uncommitted turn snapshot after each tool batch. If the process crashes mid-run, the next `RunAsync` call on the same session automatically detects and resumes from where it left off.

Because `CreateSessionAsync` saves the session skeleton to the store immediately, crash recovery works from the very first turn.

No extra configuration required.

---

## Branching

Branches let users explore alternative conversation paths from any point — similar to editing a previous message in ChatGPT.

```csharp
// Fork at message index 4 — creates a new branch with messages 0–3 copied
string newBranchId = await agent.ForkBranchAsync(
    sessionId: "user-123",
    sourceBranchId: "main",
    newBranchId: "experiment",
    fromMessageIndex: 4);

// Continue on the new branch
await foreach (var evt in agent.RunAsync(
    "Try a completely different approach",
    sessionId: "user-123",
    branchId: newBranchId))
{ }

// Switch back to main any time
await foreach (var evt in agent.RunAsync(
    "Continue where we left off",
    sessionId: "user-123",
    branchId: "main"))
{ }
```

> **Note:** Once a session has been forked, always pass `branchId` explicitly. If you omit it and the session has more than one branch, an `AmbiguousBranchException` is thrown.

**Fork behaviour:**
- Messages up to `fromMessageIndex` are copied to the new branch
- Branch-scoped middleware state is copied (then diverges independently)
- Session-scoped middleware state is shared across all branches (e.g. permission choices)

---

## Storage Layout (JsonSessionStore)

```
{basePath}/
└── sessions/
    └── {sessionId}/
        ├── session.json          # Session metadata and timestamps
        ├── branches/
        │   └── {branchId}/
        │       └── branch.json   # Messages and branch state
        ├── uncommitted.json      # Crash recovery buffer (temporary)
        └── assets/               # Uploaded files
```

---

## API Reference

### CreateSessionAsync

```csharp
Task<string> CreateSessionAsync(
    string? sessionId = null,              // omit to generate a GUID
    Dictionary<string, object>? metadata = null,
    CancellationToken cancellationToken = default)
```

Creates a new session and its default `"main"` branch in the store. Throws if the session already exists.

### RunAsync

```csharp
// Standard — session history managed automatically
IAsyncEnumerable<AgentEvent> RunAsync(
    string userMessage,
    string sessionId,
    string? branchId = null,           // omit when session has a single branch
    AgentRunConfig? options = null,
    CancellationToken cancellationToken = default)

// Stateless — no session, no persistence (useful for one-off calls)
IAsyncEnumerable<AgentEvent> RunAsync(
    IEnumerable<ChatMessage> messages,
    AgentRunConfig? options = null,
    CancellationToken cancellationToken = default)
```

### ForkBranchAsync

```csharp
Task<string> ForkBranchAsync(
    string sessionId,
    string sourceBranchId,
    string newBranchId,
    int fromMessageIndex,
    CancellationToken cancellationToken = default)
```

### AgentBuilder Extensions

| Method | Description |
|--------|-------------|
| `WithSessionStore(store)` | Configure a store — auto-save on by default |
| `WithSessionStore(store, persistAfterTurn)` | Configure a store with explicit auto-save control |
| `WithSessionStore(path)` | File-based store — auto-save on by default |
| `WithSessionStore(path, persistAfterTurn)` | File-based store with explicit auto-save control |

### ISessionStore

Implement this interface to use your own storage backend:

| Method | Description |
|--------|-------------|
| `LoadSessionAsync(sessionId)` | Load session metadata |
| `SaveSessionAsync(session)` | Save session metadata |
| `ListSessionIdsAsync()` | List all session IDs |
| `DeleteSessionAsync(sessionId)` | Delete session and all branches |
| `LoadBranchAsync(sessionId, branchId)` | Load a branch |
| `SaveBranchAsync(sessionId, branch)` | Save a branch |
| `ListBranchIdsAsync(sessionId)` | List branch IDs for a session |
| `DeleteBranchAsync(sessionId, branchId)` | Delete a branch |

---

## Best Practices

1. **Always call `CreateSessionAsync` before `RunAsync`** — even in development. It makes session lifetime explicit and enables crash recovery from turn 1.

2. **Use `WithSessionStore` for any real application** — in-memory sessions are only suitable for prototyping.

3. **Pass `branchId` explicitly after forking** — omitting it when multiple branches exist throws an exception.

4. **Crash recovery is free** — just having a store configured enables it automatically.

5. **Clean up inactive sessions** — call `DeleteSessionAsync` on a schedule to manage storage growth.
