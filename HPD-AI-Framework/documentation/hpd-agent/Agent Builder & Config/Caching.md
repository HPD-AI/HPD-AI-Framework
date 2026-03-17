# Caching

`CachingConfig` enables distributed caching of LLM responses. When a request matches a cached entry, the agent returns the cached response without calling the provider — reducing latency and cost for repeated or deterministic queries.

Caching is **opt-in** and requires an `IDistributedCache` implementation registered in the service provider (e.g., `services.AddStackExchangeRedisCache(...)` or `services.AddDistributedMemoryCache()`).

---

## Configuration

### Via `AgentConfig`

```csharp
var config = new AgentConfig
{
    Caching = new CachingConfig
    {
        Enabled = true,
        CacheExpiration = TimeSpan.FromMinutes(30)
    }
};
```

### Via builder

```csharp
// Defaults (30 min TTL)
.WithCaching()

// Custom TTL
.WithCaching(TimeSpan.FromHours(1))

// Allow caching for stateful (multi-turn) conversations
.WithCaching(TimeSpan.FromHours(1), cacheStatefulConversations: true)
```

---

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `false` | Enable response caching (opt-in) |
| `CacheExpiration` | `TimeSpan?` | `30 minutes` | TTL for cached entries. `null` = never expire |
| `CoalesceStreamingUpdates` | `bool` | `true` | Store the final assembled response (`true`) rather than the raw streaming chunks |
| `CacheStatefulConversations` | `bool` | `false` | Allow caching when a `ConversationId` is present (stateful/multi-turn conversations) |

---

## Bypassing the Cache at Runtime

To skip the cache for a specific run, use `AgentRunConfig`:

```csharp
var options = new AgentRunConfig { UseCache = false };
await foreach (var evt in agent.RunAsync("...", branch, options)) { }
```

---

## JSON Example

```json
{
    "Caching": {
        "Enabled": true,
        "CacheExpiration": "00:30:00",
        "CoalesceStreamingUpdates": true,
        "CacheStatefulConversations": false
    }
}
```

---

## Setup

Register a distributed cache in your DI container before calling `BuildAsync()`:

```csharp
// In-memory (development / single-process)
services.AddDistributedMemoryCache();

// Redis (production / multi-process)
services.AddStackExchangeRedisCache(opts =>
{
    opts.Configuration = "localhost:6379";
});

var agent = await new AgentBuilder(config)
    .WithServiceProvider(services.BuildServiceProvider())
    .WithCaching(TimeSpan.FromHours(1))
    .BuildAsync();
```

---

## See Also

- [Agent Config](Agent%20Config.md)
- [Run Config](Run%20Config.md) — `UseCache` per-run override
