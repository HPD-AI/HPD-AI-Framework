# Collapsing

Collapsing is the system that organizes toolkits into a hierarchical structure. Instead of exposing every function to the model simultaneously, toolkits are grouped and presented as high-level "skill" containers. The model first selects a container, then the functions inside it expand for use.

This reduces prompt length and helps models with large tool sets navigate functions more effectively.

Collapsing is **enabled by default** for all C# toolkits registered via `AgentBuilder`.

---

## Configuration

```csharp
var config = new AgentConfig
{
    Collapsing = new CollapsingConfig
    {
        Enabled = true,
        SkillInstructionMode = SkillInstructionMode.Both   // default
    }
};
```

---

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `true` | Enable collapsing for C# toolkits |
| `CollapseClientTools` | `bool` | `false` | Also collapse client-side tools (browser/remote) |
| `MaxFunctionNamesInDescription` | `int` | `10` | Max function names to list in the auto-generated container description |
| `SkillInstructionMode` | `SkillInstructionMode` | `Both` | Where to inject skill expansion instructions |
| `PersistSystemPromptInjections` | `bool` | `false` | Keep injected skill instructions across turns (not just the current one) |
| `EnableErrorRecovery` | `bool` | `true` | Auto-expand a collapsed toolkit if the model calls a function that should be inside it |
| `NeverCollapse` | `HashSet<string>?` | `null` | Toolkit names that should never be collapsed — always fully expanded |
| `MCPServerInstructions` | `Dictionary<string, string>?` | `null` | Custom instructions to inject after a specific MCP server container expands |
| `ClientToolsInstructions` | `string?` | `null` | Instructions to inject after the ClientTools container expands |

---

## `SkillInstructionMode`

| Value | Description |
|-------|-------------|
| `PromptMiddlewareOnly` | Inject skill instructions only via prompt middleware — keeps `ChatOptions` clean. |
| `Both` | Inject via both prompt middleware and directly into `ChatOptions` (default) |

---

## Examples

### Disable collapsing for a specific toolkit

```csharp
Collapsing = new CollapsingConfig
{
    NeverCollapse = new HashSet<string> { "FileToolkit", "CalculatorToolkit" }
}
```

These toolkits will always have all their functions directly visible to the model.

### Per-server MCP instructions

```csharp
Collapsing = new CollapsingConfig
{
    MCPServerInstructions = new Dictionary<string, string>
    {
        ["my-database-server"] = "Always filter results by the current user's tenant ID.",
        ["my-search-server"] = "Prefer results from the last 6 months."
    }
}
```

### Disable collapsing entirely

```csharp
Collapsing = new CollapsingConfig { Enabled = false }
```

All functions are exposed to the model at all times. Suitable for agents with a small number of tools.

---

## JSON Example

```json
{
    "Collapsing": {
        "Enabled": true,
        "CollapseClientTools": false,
        "MaxFunctionNamesInDescription": 10,
        "SkillInstructionMode": "Both",
        "PersistSystemPromptInjections": false,
        "EnableErrorRecovery": true,
        "NeverCollapse": ["CalculatorToolkit"],
        "ClientToolsInstructions": null
    }
}
```

> `MCPServerInstructions` can also be expressed as a JSON object.

---

## See Also

- [Agent Config](Agent%20Config.md)
- [Tools Overview](../Tools/02.1%20CSharp%20Tools%20Overview.md)
