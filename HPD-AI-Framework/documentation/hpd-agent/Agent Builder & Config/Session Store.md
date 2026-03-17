# Session Store

A session store provides durable persistence for agent conversation history. Without one, all history is held in-memory and lost when the process exits. With one, sessions survive restarts and can be shared across processes.

---

## Setup

### Via builder

```csharp
// Attach a session store — auto-save after each turn enabled by default
.WithSessionStore(myStore)

// Control auto-save explicitly
.WithSessionStore(myStore, persistAfterTurn: false)

// Full options
.WithSessionStore(myStore, opts =>
{
    opts.PersistAfterTurn = true;
})

// Convenience: file-based JSON store in one call
.WithSessionStore("./sessions", persistAfterTurn: true)
```

### Via `AgentConfig`

`SessionStore` and `SessionStoreOptions` are not JSON-serializable — set them directly on the config object:

```csharp
var config = new AgentConfig { ... };
config.SessionStore = new JsonSessionStore("./sessions");
config.SessionStoreOptions = new SessionStoreOptions
{
    PersistAfterTurn = true
};
```

---

## Built-in Stores

### `InMemorySessionStore`

Default when no store is registered. History lives for the lifetime of the `Agent` object.

```csharp
// This is the implicit default — no registration needed
```

### `JsonSessionStore`

Persists sessions as JSON files on disk. Good for development and single-process deployments.

```csharp
.WithSessionStore("./sessions")

// Or explicitly
.WithSessionStore(new JsonSessionStore("./sessions"))
```

Each session is saved as `{storagePath}/{sessionId}.json`.

---

## `SessionStoreOptions`

Controls when the store is written to:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `PersistAfterTurn` | `bool` | `false` | Save after each complete agent turn. Enabled automatically when you call `.WithSessionStore()` on the builder |

---

## Custom Stores

Implement `ISessionStore` for custom backends (SQL, Redis, blob storage, etc.):

```csharp
public class MyRedisSessionStore : ISessionStore
{
    public Task<AgentSession?> LoadAsync(string sessionId, CancellationToken ct) { ... }
    public Task SaveAsync(AgentSession session, CancellationToken ct) { ... }
    public Task DeleteAsync(string sessionId, CancellationToken ct) { ... }
}
```

Then register it:

```csharp
.WithSessionStore(new MyRedisSessionStore(connectionString))
```

---

## See Also

- [Multi-Turn Conversations](../Getting%20Started/02%20Multi-Turn%20Conversations.md) — how sessions and branches work
- [Agent Config](Agent%20Config.md)
- [Agent Builder](Agent%20Builder.md)
