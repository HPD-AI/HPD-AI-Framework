# Tool Calling

Toolkit are functions that the agent can call to interact with the world. HPD-Agent supports multiple sources of Toolkit, all unified under a common system with shared features like collapsing and instructions.

## Tool Sources

| Source | Description | Defined By |
|--------|-------------|------------|
| **C# Toolkit** | Native Toolkit with full feature support | Developer (code) |
| **MCP Servers** | External Toolkit via Model Context Protocol | MCP server configs |
| **Client Toolkit** | Toolkit provided by the client/UI | Client application |
| **OpenAPI** | Auto-generated from API specs | OpenAPI/Swagger files |

```
                    HPD-Agent
                        │
        ┌───────────────┼───────────────┐───────────────┐
        ▼               ▼               ▼               ▼
   C# Toolkit      MCP Servers    Client Toolkit      OpenAPI
   [AIFunction]    filesystem      OpenFile         GET /users
   [Skill]         github          ShowDialog       POST /orders
   [SubAgent]      database        GetSelection     ...
```

---

## C# Toolkit

The most powerful option. Define Toolkit directly in C# with full access to:
- **AIFunctions** - Single operations
- **Skills** - Multi-function workflows with instructions
- **SubAgents** - Delegated child agents
- **Tool Metadata** - Dynamic descriptions and conditional visibility
- **Collapsing** - Hierarchical organization with `[Collapse]`

```csharp
[Collapse("File operations")]
public class FileToolkit
{
    [AIFunction]
    [AIDescription("Read a file")]
    public string ReadFile(string path) { }

    [AIFunction]
    [ConditionalFunction("AllowWrite")]
    public void WriteFile(string path, string content) { }
}
```

→ See [C# Tools Overview](../Tools/02.1%20CSharp%20Tools%20Overview.md) for the full guide.

---

## MCP Servers

Connect external tool servers using the Model Context Protocol. MCP servers run as separate processes and expose Toolkit over a standardized protocol.

```csharp
var agent = await new AgentBuilder()
    .WithMCPServer("filesystem", new MCPServerConfig { ... })
    .WithMCPServer("github", new MCPServerConfig { ... })
    .BuildAsync();
```

MCP Toolkit support:
- Collapsing (grouped by server)
- Custom instructions per server
- Automatic tool discovery

→ See [02.2 MCP Servers.md](../Tools/02.2%20MCP%20Servers.md) for setup and configuration.

---

## Client Toolkit

Toolkit provided by the client application (IDE extension, web UI, etc.). These are injected at runtime and allow the agent to interact with the user's environment.

Common client Toolkit:
- `OpenFile` - Open a file in the editor
- `ShowDialog` - Display a dialog to the user
- `GetSelection` - Get the user's current selection

```csharp
var config = new AgentConfig
{
    Collapsing = new CollapsingConfig
    {
        CollapseClientTools = true,
        ClientToolsInstructions = "These tools interact with the user's IDE."
    }
};
```

→ See [02.3 Client Toolkit.md](../Tools/02.3%20Client%20Tools.md) for integration details.

---

## OpenAPI (Coming Soon)

Auto-generate Toolkit from OpenAPI/Swagger specifications. Point to an API spec and get Toolkit for each endpoint.

```csharp
// Future API
var agent = await new AgentBuilder()
    .WithOpenApi("https://api.example.com/openapi.json")
    .BuildAsync();
```

---

## Shared Features

All tool sources share these capabilities:

### Collapsing

Group Toolkit into expandable containers to reduce context clutter:

```csharp
var config = new AgentConfig
{
    Collapsing = new CollapsingConfig
    {
        Enabled = true,                    // C# Toolkit
        CollapseClientTools = true,           // Client Toolkit
        // MCP servers are collapsed by server name automatically
    }
};
```

### Instructions

Provide guidance for tool usage:

```csharp
var config = new AgentConfig
{
    Collapsing = new CollapsingConfig
    {
        // Per-MCP-server instructions
        MCPServerInstructions = new Dictionary<string, string>
        {
            ["filesystem"] = "Always use absolute paths.",
            ["github"] = "Prefer GraphQL API for bulk operations."
        },

        // Client Toolkit instructions
        ClientToolsInstructions = "These tools interact with the user's IDE."
    }
};
```

For C# Toolkit, instructions are defined via `[Collapse]` attributes and skill instructions.

---

## Choosing a Tool Source

| Need | Best Choice |
|------|-------------|
| Full control, type safety, compile-time validation | C# Toolkit |
| Use existing MCP-compatible servers | MCP Servers |
| Interact with user's environment (IDE, UI) | Client Toolkit |
| Integrate with REST APIs quickly | OpenAPI |

Most applications use a combination:
- **C# Toolkit** for core business logic
- **MCP Servers** for standard capabilities (filesystem, git, etc.)
- **Client Toolkit** for UI interaction

---

## Next Steps

- [C# Tools Overview](../Tools/02.1%20CSharp%20Tools%20Overview.md) - Native tool development
- [02.2 MCP Servers.md](../Tools/02.2%20MCP%20Servers.md) - Model Context Protocol integration
- [02.3 Client Toolkit.md](../Tools/02.3%20Client%20Tools.md) - Client-provided Toolkit
