# History Reduction

`HistoryReductionConfig` controls how the agent manages long conversation histories. When enabled, the agent trims or summarizes old messages before each turn to keep the context window under control.

```csharp
var config = new AgentConfig
{
    HistoryReduction = new HistoryReductionConfig
    {
        Enabled = true,
        Strategy = HistoryReductionStrategy.MessageCounting,
        TargetCount = 20
    }
};
```

---

## Properties

### Core

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `false` | Enable history reduction |
| `Strategy` | `HistoryReductionStrategy` | `MessageCounting` | How history is reduced — count-based or AI summarization |
| `Behavior` | `HistoryReductionBehavior` | `Continue` | What happens when reduction fires — transparent or circuit-breaker |
| `CountingUnit` | `HistoryCountingUnit` | `Exchanges` | Unit of measurement for `TargetCount` |
| `TargetCount` | `int` | `20` | Number of units to retain after reduction |
| `SummarizationThreshold` | `int?` | `5` | Units beyond `TargetCount` before summarization fires (Summarizing strategy only) |

### Summarization

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CustomSummarizationPrompt` | `string?` | `null` | Override the default summarization prompt |
| `SummarizerProvider` | `ProviderConfig?` | `null` | Use a separate (cheaper) provider for summarization |
| `UseSingleSummary` | `bool` | `true` | Re-summarize all history each time (`true`) vs. append incremental summaries (`false`) |

### Token-Budget Properties (Not Yet Implemented)

These properties exist on `HistoryReductionConfig` but are **not currently evaluated** by the framework. They are reserved for a future token-aware reduction mode.

| Property | Type | Default |
|----------|------|---------|
| `TargetTokenBudget` | `int` | `4000` |
| `TokenBudgetThreshold` | `int` | `1000` |
| `TokenBudgetTriggerPercentage` | `double?` | `null` |
| `TokenBudgetPreservePercentage` | `double` | `0.3` |
| `ContextWindowSize` | `int?` | `null` |

Do not rely on these properties to have any effect.

---

## Enums

### `HistoryReductionStrategy`

| Value | Description |
|-------|-------------|
| `MessageCounting` | Drop oldest messages until `TargetCount` units remain |
| `Summarizing` | Summarize old messages into a compact context block using an LLM call |

### `HistoryReductionBehavior`

| Value | Description |
|-------|-------------|
| `Continue` | Reduction happens silently mid-turn — the agent keeps going without interruption |
| `CircuitBreaker` | Stop the current turn and notify the user that history was reduced before continuing |

### `HistoryCountingUnit`

| Value | Description |
|-------|-------------|
| `Exchanges` | Count user↔agent pairs (one user message + one agent reply = 1 exchange) |
| `Messages` | Count raw `ChatMessage` objects (including tool calls and results) |

---

## Examples

### MessageCounting (simple)

Keep the last 10 user↔agent exchanges, drop the rest:

```csharp
HistoryReduction = new HistoryReductionConfig
{
    Enabled = true,
    Strategy = HistoryReductionStrategy.MessageCounting,
    CountingUnit = HistoryCountingUnit.Exchanges,
    TargetCount = 10
}
```

### Summarizing (richer context)

Summarize old history instead of dropping it, using a cheaper model:

```csharp
HistoryReduction = new HistoryReductionConfig
{
    Enabled = true,
    Strategy = HistoryReductionStrategy.Summarizing,
    TargetCount = 15,
    SummarizationThreshold = 5,       // Summarize when 20+ exchanges exist
    SummarizerProvider = new ProviderConfig
    {
        ProviderKey = "openai",
        ModelName = "gpt-4o-mini"     // Cost-optimized summarizer
    },
    CustomSummarizationPrompt = "Summarize this conversation concisely, preserving key facts and decisions."
}
```

### Circuit-breaker behavior

Stop the turn and inform the user before compressing history:

```csharp
HistoryReduction = new HistoryReductionConfig
{
    Enabled = true,
    Strategy = HistoryReductionStrategy.MessageCounting,
    Behavior = HistoryReductionBehavior.CircuitBreaker,
    TargetCount = 20
}
```

---

## Per-Run Overrides

You can override reduction behavior for a single turn via `AgentRunConfig`:

```csharp
// Force reduction now (before an expensive operation)
var options = new AgentRunConfig { TriggerHistoryReduction = true };

// Skip reduction (need full context for an important decision)
var options = new AgentRunConfig { SkipHistoryReduction = true };

// Change behavior mode for this turn
var options = new AgentRunConfig
{
    HistoryReductionBehaviorOverride = HistoryReductionBehavior.CircuitBreaker
};
```

---

## JSON Example

```json
{
    "HistoryReduction": {
        "Enabled": true,
        "Strategy": "MessageCounting",
        "Behavior": "Continue",
        "CountingUnit": "Exchanges",
        "TargetCount": 20,
        "SummarizationThreshold": 5,
        "UseSingleSummary": true,
        "CustomSummarizationPrompt": null,
        "SummarizerProvider": null
    }
}
```

---

## See Also

- [Agent Config](Agent%20Config.md)
- [Run Config](Run%20Config.md) — per-run overrides (`TriggerHistoryReduction`, `SkipHistoryReduction`)
