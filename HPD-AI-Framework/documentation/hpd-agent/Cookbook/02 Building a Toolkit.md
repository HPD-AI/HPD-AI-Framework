# Building a Toolkit

> Six ways to give your agent capabilities — using a math toolkit as the example.

A **toolkit** is a class that groups related capabilities. HPD-Agent supports six capability types, each suited for a different level of complexity and autonomy. This cookbook walks through all of them using math as the domain — building up a single `MathToolkit` class one step at a time.

---

## Step 1 — AIFunctions: single operations

The simplest capability. Mark a method with `[AIFunction]` and describe it so the agent knows when to use it.

```csharp
using HPD.Agent;

public partial class MathToolkit
{
    [AIFunction]
    [AIDescription("Add two numbers and return the sum")]
    public double Add(
        [AIDescription("The first number")] double a,
        [AIDescription("The second number")] double b)
        => a + b;

    [AIFunction]
    [AIDescription("Subtract b from a")]
    public double Subtract(
        [AIDescription("The number to subtract from")] double a,
        [AIDescription("The number to subtract")] double b)
        => a - b;

    [AIFunction]
    [AIDescription("Multiply two numbers")]
    public double Multiply(
        [AIDescription("The first factor")] double a,
        [AIDescription("The second factor")] double b)
        => a * b;

    [AIFunction]
    [AIDescription("Divide a by b. Returns an error if b is zero.")]
    public string Divide(
        [AIDescription("The dividend")] double a,
        [AIDescription("The divisor — must not be zero")] double b)
    {
        if (b == 0) return "Error: division by zero";
        return (a / b).ToString();
    }

    [AIFunction]
    [AIDescription("Compute the square root of a non-negative number")]
    public string SquareRoot(
        [AIDescription("The number — must be >= 0")] double n)
    {
        if (n < 0) return "Error: square root of negative number is not real";
        return Math.Sqrt(n).ToString();
    }
}
```

**Why `[AIDescription]`?** The agent uses descriptions to decide which function to call and how to fill in the parameters. Without them, it has to guess from the method and parameter names alone — descriptions make it reliable.

Register the toolkit:

```csharp
var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithToolkit<MathToolkit>()
    .BuildAsync();
```

---

## Step 2 — Skill: a guided multi-step workflow

A **Skill** groups existing functions and gives the agent a workflow to follow when they're activated together. The agent enters the skill, then follows the instructions using the referenced functions.

Use a skill when the steps are known upfront but you want the agent to execute them flexibly.

```csharp
public partial class MathToolkit
{
    // ... AIFunctions from Step 1 ...

    [Skill]
    public Skill SolveEquation()
    {
        return SkillFactory.Create(
            name: "Solve Equation",
            description: "Breaks down and solves a multi-step math equation",
            functionResult: null,  // auto-generated activation message is sufficient
            systemPrompt: @"
                EQUATION SOLVING WORKFLOW:
                1. Identify what operations are needed (add, subtract, multiply, divide, sqrt)
                2. Execute them in the correct order, one step at a time
                3. Use the result of each step as input to the next
                4. Show your working at each step
                5. State the final answer clearly",
            "MathToolkit.Add",
            "MathToolkit.Subtract",
            "MathToolkit.Multiply",
            "MathToolkit.Divide",
            "MathToolkit.SquareRoot"
        );
    }
}
```

When the agent activates the `Solve Equation` skill, the referenced functions become visible and the workflow instructions are injected. The functions must already exist in a registered toolkit — the skill doesn't bring them in, it references them. Since `MathToolkit` is registered and the skill lives in the same class, the references resolve automatically.

---

## Step 3 — SubAgent: autonomous reasoning

A **SubAgent** is a child agent delegated a task it figures out autonomously. Unlike a Skill, you don't enumerate the steps — the SubAgent reasons through the problem on its own, with its own isolated context and tool set.

Use a SubAgent when the path isn't predictable upfront — or when you want to keep the intermediate work out of the parent's context window.

**Math use case: Proof Checker.** Given a mathematical proof, verify each step is logically valid. The path varies wildly depending on the proof — the sub-agent needs to reason autonomously.

```csharp
public partial class MathToolkit
{
    // ... AIFunctions and Skill from above ...

    [SubAgent]
    public SubAgent ProofChecker()
    {
        var config = new AgentConfig
        {
            Name = "Proof Checker",
            SystemInstructions = @"
                You are a mathematical proof verifier. When given a proof:
                1. Read each step carefully
                2. Verify each step follows logically from the previous
                3. Check that each arithmetic operation is correct using your tools
                4. Identify any logical gaps or errors
                5. Return: VALID (with brief explanation) or INVALID (with the specific step that fails and why)"
        };

        return SubAgentFactory.Create(
            name: "Check Proof",
            description: "Verifies a mathematical proof step by step",
            agentConfig: config,
            typeof(MathToolkit)  // fresh instance — not circular, SubAgent gets its own isolated MathToolkit
        );
    }
}
```

The SubAgent has its own isolated reasoning loop. It uses the `MathToolkit` functions to check individual arithmetic steps, but its overall reasoning path — which steps to examine, what questions to ask — is fully autonomous. All that intermediate work is discarded when it's done; the parent only receives the final verdict.

---

## Step 4 — MultiAgent: parallel specialized agents

A **MultiAgent** workflow is for problems that benefit from multiple specialized agents working in sequence or in parallel, with conditional routing between them.

Use MultiAgent when distinct stages need different specializations, parallel execution, or conditional routing — not just one agent doing everything sequentially.

**Math use case: Solving a competition-style problem.** Hard math problems benefit from three distinct roles: a **Decomposer** that breaks the problem into sub-problems, a **Solver** that attacks each sub-problem, and a **Verifier** that checks the final answer is consistent. These are distinct enough jobs to warrant separate agents with different instructions.

```csharp
// Set up each specialized agent
var decomposerConfig = new AgentConfig
{
    SystemInstructions = @"
        You are a mathematical problem decomposer. Given a complex math problem:
        1. Break it down into discrete, independently solvable sub-problems
        2. State each sub-problem clearly and concisely
        3. Output them as a numbered list
        Do NOT solve them — decompose only."
};

var solverConfig = new AgentConfig
{
    SystemInstructions = @"
        You are a mathematical solver. Given a list of sub-problems:
        1. Solve each one using the available math tools
        2. Show your working for each
        3. Combine the sub-results into a final answer"
};

var verifierConfig = new AgentConfig
{
    SystemInstructions = @"
        You are a mathematical verifier. Given a problem and a proposed answer:
        1. Re-derive the answer independently using the math tools
        2. Check it matches the proposed answer
        3. Output: VERIFIED or MISMATCH (with what you got instead)"
};

// Build the workflow
var workflow = await AgentWorkflow.Create()
    .AddAgent("decomposer", decomposerConfig)
    .AddAgent("solver", solverConfig, typeof(MathToolkit))
    .AddAgent("verifier", verifierConfig, typeof(MathToolkit))
    .From("decomposer").To("solver")
    .From("solver").To("verifier")
    .BuildAsync();

// Run it
await foreach (var evt in workflow.ExecuteStreamingAsync("Find all integer solutions to x² + y² = 25"))
{
    if (evt is TextDeltaEvent delta)
        Console.Write(delta.Text);
}
```

Each agent only sees what it needs — the decomposer sees the original problem, the solver sees the sub-problems, the verifier sees the final answer. No agent is distracted by another's work.

---

## Step 5 — MCP Server: external tool via protocol

**MCP (Model Context Protocol)** lets you connect external tool servers without writing integration code. Tools are discovered automatically from the server.

**Math use case:** Connect to Brave Search so the agent can look up math formulas, theorems, and proofs on demand.

Add `[MCPServer]` directly to `MathToolkit`. Declare `ISecretResolver` as a primary constructor parameter — the source generator detects it and handles the wiring automatically:

```csharp
public partial class MathToolkit(ISecretResolver secrets)
{
    // ... AIFunctions, Skill, SubAgent from above ...

    [MCPServer]
    public MCPServerConfig BraveSearch() => new()
    {
        Name = "brave-search",
        Command = "npx",
        Arguments = ["-y", "@anthropic/mcp-brave-search"],
        EnableCollapsing = true,
        SystemPrompt = "Use search to look up math theorems, formulas, and proofs. Always prefer authoritative sources (Wikipedia, MathWorld, academic papers).",
        Environment = new()
        {
            ["BRAVE_API_KEY"] = secrets.Require("brave:ApiKey")
        }
    };
}
```

The MCP server is part of the toolkit — no separate registration needed.

---

## Step 6 — OpenAPI: a REST API as tools

**OpenAPI** turns any REST API into agent tools automatically from its spec — no hand-written integration code.

**Math use case:** [Wolfram Alpha](https://products.wolframalpha.com/api/) has a public API for computational math — symbolic solving, calculus, number theory, unit conversion, and more. Every operation in the spec becomes a callable function.

```csharp
public partial class MathToolkit(ISecretResolver secrets)
{
    // ... AIFunctions, Skill, SubAgent, MCP from above ...

    [OpenApi(Prefix = "wolfram")]
    public OpenApiConfig WolframAlpha() => new()
    {
        SpecUri = new Uri("https://products.wolframalpha.com/api/v2/openapi.json"),
        AuthCallback = async (req, ct) =>
        {
            var key = await secrets.RequireAsync("wolfram:ApiKey", "Wolfram Alpha", ct: ct);
            // Wolfram Alpha uses appid as a query parameter
            var uri = req.RequestUri!;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            query["appid"] = key;
            req.RequestUri = new Uri(uri.GetLeftPart(UriPartial.Path) + "?" + query);
        },
        ResponseOptimization = new ResponseOptimizationConfig
        {
            MaxLength = 3000  // Wolfram responses can be verbose — trim for the LLM
        }
    };
}
```

The agent can now call Wolfram Alpha for anything beyond basic arithmetic — symbolic integration, equation solving, prime factorization, and more — while still using the native functions for simple operations.

---

## Putting it all together

All six capability types live in one class. Register it once:

```csharp
var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithInstructions("You are a math assistant. Use your tools to solve problems accurately.")
    .WithToolkit<MathToolkit>()
    .BuildAsync();

var sessionId = await agent.CreateSessionAsync();

await foreach (var evt in agent.RunAsync(
    "Verify this proof: √(3² + 4²) = 5, because 9 + 16 = 25 and √25 = 5",
    sessionId: sessionId))
{
    if (evt is TextDeltaEvent delta)
        Console.Write(delta.Text);
}
```

**They don't have to be in one class.** Grouping them in `MathToolkit` makes sense here because they're all math-related. In practice you might split them  Register as many toolkits as you need:
Ex. 
```csharp
var agent = await new AgentBuilder()
    .WithToolkit<CoreMathToolkit>()
    .WithToolkit<MathSearchToolkit>()
    .BuildAsync();
```

The agent sees all their capabilities as one unified tool set.

---

## Which type to use?

| Situation | Use |
|---|---|
| One deterministic operation | `AIFunction` |
| Steps are known, want guided execution | `Skill` |
| Task path is unpredictable, needs autonomous reasoning | `SubAgent` |
| Distinct stages, parallel work, or conditional routing | `MultiAgent` |
| External tool server (auto-discovered tools) | `MCP` |
| REST API with an OpenAPI spec | `OpenAPI` |
