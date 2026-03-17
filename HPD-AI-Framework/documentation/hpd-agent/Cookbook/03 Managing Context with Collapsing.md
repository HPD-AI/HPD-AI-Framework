# Managing Context with Collapsing

> The agent only needs to know about math when it's doing math.

Every tool you register costs tokens — even when it's irrelevant. An agent with 50+ tools exposed on every turn is paying to describe Wolfram Alpha's integral solver when the user just asked "what's 2 + 2". Collapsing fixes this by making capabilities available on demand rather than always visible.

This entry builds on the `MathToolkit` from [Building a Toolkit](./02%20Building%20a%20Toolkit.md). By the end, a toolkit that exposes 50+ entries flat will be reduced to a single entry in the agent's view — with full capability intact.

---

## Step 1 — The problem: everything is always visible

Without collapsing, the full `MathToolkit` floods the agent's context on every turn:

```
Agent's tool list (every turn, every message):
┌────────────────────────────────────────────────────────┐
│ Add                  — Add two numbers                 │
│ Subtract             — Subtract b from a               │
│ Multiply             — Multiply two numbers            │
│ Divide               — Divide a by b                   │
│ SquareRoot           — Compute the square root         │
│ Solve Equation       — [Skill] multi-step solver       │
│ Check Proof          — [SubAgent] proof verifier       │
│ MCP_brave-search     — Web search for theorems         │
│ wolfram_query        — Wolfram Alpha: query            │
│ wolfram_solve        — Wolfram Alpha: solve equation   │
│ wolfram_integrate    — Wolfram Alpha: integrate        │
│ wolfram_differentiate — Wolfram Alpha: differentiate  │
│ ... (40+ more Wolfram operations)                      │
└────────────────────────────────────────────────────────┘
```

Every one of those lines is tokens. The agent reads all of them before deciding what to do — including the 40 Wolfram operations it almost never needs. If this agent also handles writing, coding, and scheduling, the list gets worse.

---

## Step 2 — Collapse the toolkit

Add `[Collapse]` with a description. That's it:

```csharp
[Collapse("Math operations — arithmetic, equation solving, proof checking, and symbolic computation")]
public partial class MathToolkit(ISecretResolver secrets)
{
    // everything inside stays exactly the same
}
```

The agent's tool list goes from 50+ entries to one:

```
Agent's tool list (every turn):
┌─────────────────────────────────────────────────────────────────────┐
│ MathToolkit — Container MathToolkit provides access to: Add,        │
│ Subtract, Multiply, Divide, SquareRoot, Solve Equation, Check Proof,│
│ MCP_brave-search, OpenApi_wolfram. Math operations — arithmetic,    │
│ equation solving, proof checking, and symbolic computation.         │
└─────────────────────────────────────────────────────────────────────┘
```

The system automatically prepends the function names so the agent knows what's inside before deciding to expand. Your description comes after, adding context about when to use this toolkit. The agent pays for one entry instead of 50+ — and it has enough information to make the decision without opening it.

When the agent decides the task is math-related, it expands the toolkit and sees everything inside. When the task isn't math, it never touches it — and pays zero tokens for any of those 50 entries.

This is also how **Skills** work internally. When the agent activates the `Solve Equation` skill, it's expanding a container — the referenced functions appear in context, the workflow instructions inject, and then it gets to work. Collapsing is the same architecture applied to the whole toolkit.

---

## Step 3 — Put the persona in the toolkit

Here's the deeper insight: `SystemPrompt` on `[Collapse]` injects into the system prompt *only while the toolkit is active*. That means you can describe how the agent should behave when doing math — right next to the math tools — and it won't pollute the context during non-math turns.

Since `[Collapse]` takes compile-time string constants, define long prompts as `private const` fields inside the class and reference them from the attribute:

```csharp
[Collapse(
    "Math operations — arithmetic, equation solving, proof checking, and symbolic computation",
    SystemPrompt = MathToolkit.MathSystemPrompt)]
public partial class MathToolkit(ISecretResolver secrets)
{
    private const string MathSystemPrompt = """
        You are operating in math mode. Follow these rules:
        - Always show your working step by step
        - Use the available tools for every calculation — don't compute in your head
        - For equations, identify the operations needed before executing any of them
        - State the final answer clearly and verify it makes sense
        - If a proof is provided, use the Check Proof subagent rather than verifying manually
        """;

    // ... rest of toolkit
}
```

`ISecretResolver` is declared as a primary constructor parameter — the source generator detects it and wires the resolved instance automatically. No DI registration needed.

The agent's base `WithInstructions` stays short and domain-agnostic — something like "You are a helpful assistant." The math-specific behavior only exists in context during math turns. An agent with ten toolkits effectively has ten domain personas, each loading only when relevant.

**`FunctionResult` vs `SystemPrompt`:**

| | `FunctionResult` | `SystemPrompt` |
|---|---|---|
| **When** | Once, on expansion | Every turn while active |
| **Use for** | What's inside, tips, status | Rules the agent must follow |
| **Example** | "Tip: use Check Proof for multi-step proofs" | "Always show working step by step" |

---

## Step 4 — Nested collapsing for large API surfaces

The MCP and OpenAPI entries bring in many tools — Wolfram Alpha alone exposes 40+ operations. Even inside an already-collapsed `MathToolkit`, that's a lot to dump into context when the toolkit expands.

Add `CollapseWithinToolkit = true` to keep them behind their own sub-container:

```csharp
[Collapse(
    "Math operations — arithmetic, equation solving, proof checking, and symbolic computation",
    SystemPrompt = MathToolkit.MathSystemPrompt)]
public partial class MathToolkit(ISecretResolver secrets)
{
    private const string MathSystemPrompt = """
        You are operating in math mode. Always show your working.
        Use tools for every calculation. State the final answer clearly.
        """;

    // AIFunctions — always visible when toolkit expands
    [AIFunction] public double Add(...) => ...;
    [AIFunction] public double Subtract(...) => ...;
    [AIFunction] public double Multiply(...) => ...;
    [AIFunction] public string Divide(...) => ...;
    [AIFunction] public string SquareRoot(...) => ...;

    // Skill — visible as a single entry when toolkit expands
    [Skill] public Skill SolveEquation() => ...;

    // SubAgent — visible as a single entry when toolkit expands
    [SubAgent] public SubAgent ProofChecker() => ...;

    // MCP — stays collapsed inside the toolkit (one more expand required)
    [MCPServer(CollapseWithinToolkit = true)]
    public MCPServerConfig BraveSearch() => new()
    {
        Name = "brave-search",
        Command = "npx",
        Arguments = ["-y", "@anthropic/mcp-brave-search"],
        EnableCollapsing = true,
        SystemPrompt = "Prefer authoritative sources: Wikipedia, MathWorld, academic papers.",
        Environment = new() { ["BRAVE_API_KEY"] = secrets.Require("brave:ApiKey") }
    };

    // OpenAPI — stays collapsed inside the toolkit (one more expand required)
    [OpenApi(Prefix = "wolfram", CollapseWithinToolkit = true)]
    public OpenApiConfig WolframAlpha() => new()
    {
        SpecUri = new Uri("https://products.wolframalpha.com/api/v2/openapi.json"),
        AuthCallback = async (req, ct) =>
        {
            var key = await secrets.RequireAsync("wolfram:ApiKey", "Wolfram Alpha", ct: ct);
            var uri = req.RequestUri!;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            query["appid"] = key;
            req.RequestUri = new Uri(uri.GetLeftPart(UriPartial.Path) + "?" + query);
        },
        ResponseOptimization = new ResponseOptimizationConfig { MaxLength = 3000 }
    };
}
```

Now the agent expands in layers:

```
Level 0 — always visible:
┌──────────────────────────────────────────────────────────────────────┐
│ MathToolkit — Container MathToolkit provides access to: Add,         │
│ Subtract, Multiply, Divide, SquareRoot, Solve Equation, Check Proof, │
│ MCP_brave-search, OpenApi_wolfram. Math operations — arithmetic,     │
│ equation solving, proof checking, and symbolic computation.          │
└──────────────────────────────────────────────────────────────────────┘

Level 1 — after expanding MathToolkit:
┌──────────────────────────────────────────────────────────┐
│ Add              — Add two numbers                       │
│ Subtract         — Subtract b from a                     │
│ Multiply         — Multiply two numbers                  │
│ Divide           — Divide a by b                         │
│ SquareRoot       — Compute the square root               │
│ Solve Equation   — [Skill] multi-step solver             │
│ Check Proof      — [SubAgent] proof verifier             │
│ MCP_brave-search — Web search for theorems (8 tools)     │
│ OpenApi_wolfram  — Wolfram Alpha symbolic math (40 tools)│
└──────────────────────────────────────────────────────────┘

Level 2 — after expanding OpenApi_wolfram:
┌──────────────────────────────────────────────────────────┐
│ wolfram_query           — Query Wolfram Alpha            │
│ wolfram_solve           — Solve an equation              │
│ wolfram_integrate       — Integrate an expression        │
│ wolfram_differentiate   — Differentiate an expression    │
│ ... (36 more operations)                                 │
└──────────────────────────────────────────────────────────┘
```

The 40 Wolfram operations only enter context if the agent decides it needs symbolic computation. For "what's 9 + 16?", it uses `Add` directly — Wolfram never appears.

---

## Step 5 — What not to collapse

Collapsing adds a step. If the agent needs a tool on almost every turn, hiding it behind a container wastes a round-trip. Don't collapse:

- Core identity tools (tools the agent uses constantly)
- Simple agents with few tools (collapsing 3 functions isn't worth it)
- Tools the agent must always know are available (e.g. a `SendMessage` in a chat agent)

For everything else — especially large API surfaces, domain-specific capabilities, and tools that are only relevant some of the time — collapsing keeps the context focused and the agent fast.
