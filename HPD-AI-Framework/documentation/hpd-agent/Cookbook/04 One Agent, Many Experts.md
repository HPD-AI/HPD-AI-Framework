# One Agent, Many Experts

> One agent. Three domains. Each with its own identity, its own tools, and its own workflows.

The naive approach to building a multi-domain agent is one giant system prompt: math rules, writing rules, coding rules all jammed together. It bloats immediately and gets worse with every domain you add.

The other extreme is separate specialized agents. That works, but means separate sessions, separate histories, more infrastructure.

There's a middle path — and it uses three levels of the same architecture:

```
Agent (generalist, minimal system prompt)
  └── MathToolkit              ← domain expert, persona activates on expansion
        ├── Add, Subtract, Multiply, Divide, SquareRoot   ← operations
        ├── SolveEquation      ← procedure: step-by-step equation solving
        ├── ProveTheorem       ← procedure: structured proof verification
        └── ConvertUnits       ← procedure: unit conversion workflow
  └── WritingToolkit           ← domain expert
        ├── AnalyseStructure, SuggestRewrite, CheckGrammar   ← operations
        ├── DeepEdit           ← procedure: full editorial pass
        └── ToneRework         ← procedure: systematic tone adjustment
  └── CodeToolkit              ← domain expert
        ├── ReviewCode, ScanDependencies, ExplainCode   ← operations
        ├── SecurityAudit      ← procedure: vulnerability review
        └── PerformanceReview  ← procedure: bottleneck analysis
```

The toolkit makes the agent an expert. The skills inside give that expert multiple methodologies — one per type of task the domain handles. A toolkit can have as many skills as the domain warrants. Each level is a collapsed container — context only pays for what's open.

---

## Step 1 — The base agent

Two lines. Everything else lives in the toolkits:

```csharp
var agent = await new AgentBuilder()
    .WithProvider("anthropic", "claude-sonnet-4-5")
    .WithInstructions("You are a helpful assistant. Use your tools to help the user.")
    .WithToolkit<MathToolkit>()
    .WithToolkit<WritingToolkit>()
    .WithToolkit<CodeToolkit>()
    .BuildAsync();
```

---

## Step 2 — Toolkits carry the domain persona

Each toolkit defines who the agent becomes when that domain is active. The `SystemPrompt` on `[Collapse]` injects only when the toolkit is expanded — and clears automatically at the end of the turn:

```csharp
[Collapse(
    "Math tutoring — step-by-step problem solving, equation work, and proof checking",
    SystemPrompt = MathToolkit.Persona)]
public partial class MathToolkit
{
    private const string Persona = """
        You are now a math tutor. Rules:
        - Always show each step — never skip to the answer
        - Use your tools for every calculation, never mental arithmetic
        - After solving, ask if the student understands the approach
        """;

    [AIFunction]
    [AIDescription("Add two numbers")]
    public double Add([AIDescription("First number")] double a, [AIDescription("Second number")] double b) => a + b;

    [AIFunction]
    [AIDescription("Subtract b from a")]
    public double Subtract([AIDescription("Number to subtract from")] double a, [AIDescription("Number to subtract")] double b) => a - b;

    [AIFunction]
    [AIDescription("Multiply two numbers")]
    public double Multiply([AIDescription("First factor")] double a, [AIDescription("Second factor")] double b) => a * b;

    [AIFunction]
    [AIDescription("Divide a by b — returns error if b is zero")]
    public string Divide([AIDescription("Dividend")] double a, [AIDescription("Divisor")] double b)
        => b == 0 ? "Error: division by zero" : (a / b).ToString();

    [AIFunction]
    [AIDescription("Compute the square root of a non-negative number")]
    public string SquareRoot([AIDescription("The number — must be >= 0")] double n)
        => n < 0 ? "Error: square root of negative number is not real" : Math.Sqrt(n).ToString();

    // SolveEquation skill defined in Step 3 below
}
```

```csharp
[Collapse(
    "Writing assistance — drafting, editing, tone adjustment, and structure feedback",
    SystemPrompt = WritingToolkit.Persona)]
public partial class WritingToolkit
{
    private const string Persona = """
        You are now a writing coach. Rules:
        - Lead with the strongest structural feedback first
        - Suggest edits as concrete rewrites, not vague advice
        - Match the user's existing tone unless asked to change it
        - Never rewrite entire passages unprompted — propose, don't replace
        """;

    [AIFunction]
    [AIDescription("Analyse the structure and flow of a piece of writing")]
    public string AnalyseStructure([AIDescription("The text to analyse")] string text) { /* ... */ }

    [AIFunction]
    [AIDescription("Suggest a rewrite for a specific sentence or paragraph")]
    public string SuggestRewrite(
        [AIDescription("The original text")] string original,
        [AIDescription("The goal: e.g. 'more concise', 'more formal'")] string goal) { /* ... */ }

    [AIFunction]
    [AIDescription("Check grammar and punctuation")]
    public string CheckGrammar([AIDescription("The text to check")] string text) { /* ... */ }

    // DeepEdit skill defined in Step 3 below
}
```

```csharp
[Collapse(
    "Code review and debugging — analysis, fixes, and best practice guidance",
    SystemPrompt = CodeToolkit.Persona)]
public partial class CodeToolkit
{
    private const string Persona = """
        You are now a code reviewer. Rules:
        - Flag security issues before style issues
        - Show corrected code, not just descriptions of what to fix
        - Explain why a pattern is problematic, not just that it is
        - Don't refactor beyond what was asked
        """;

    [AIFunction]
    [AIDescription("Review code for bugs, security issues, and best practices")]
    public string ReviewCode(
        [AIDescription("The code to review")] string code,
        [AIDescription("The programming language")] string language) { /* ... */ }

    [AIFunction]
    [AIDescription("Scan dependencies for known vulnerabilities")]
    public string ScanDependencies([AIDescription("The dependency file contents")] string contents) { /* ... */ }

    [AIFunction]
    [AIDescription("Explain what a piece of code does in plain language")]
    public string ExplainCode([AIDescription("The code to explain")] string code) { /* ... */ }

    // SecurityAudit skill defined in Step 3 below
}
```

---

## Step 3 — Skills carry the domain procedures

Each toolkit can have as many skills as the domain warrants — one per type of task the expert handles. Each skill is a collapsed container inside the already-collapsed toolkit, referencing the functions it needs and injecting its own workflow instructions when activated. Here's one skill per toolkit to show the pattern:

```csharp
public partial class MathToolkit
{
    [Skill]
    public Skill SolveEquation() => SkillFactory.Create(
        name: "Solve Equation",
        description: "Step-by-step equation solving with working shown at each stage",
        functionResult: null,
        systemPrompt: """
            EQUATION SOLVING PROCEDURE:
            1. Identify what type of equation this is
            2. Identify which operations are needed and in what order
            3. Execute each step one at a time using your tools
            4. Show the result of each step before moving to the next
            5. State the final answer and verify it by substituting back
            """,
        "MathToolkit.Add",
        "MathToolkit.Subtract",
        "MathToolkit.Multiply",
        "MathToolkit.Divide",
        "MathToolkit.SquareRoot"
    );
}
```

```csharp
public partial class WritingToolkit
{
    [Skill]
    public Skill DeepEdit() => SkillFactory.Create(
        name: "Deep Edit",
        description: "Full editorial pass — structure, line level, then grammar",
        functionResult: null,
        systemPrompt: """
            DEEP EDIT PROCEDURE:
            1. AnalyseStructure first — identify any structural issues before touching prose
            2. Address structural problems with SuggestRewrite at the section level
            3. Then work line-by-line: SuggestRewrite for clarity and tone
            4. CheckGrammar last — only after the structure and prose are solid
            5. Present all suggestions together, grouped by type
            """,
        "WritingToolkit.AnalyseStructure",
        "WritingToolkit.SuggestRewrite",
        "WritingToolkit.CheckGrammar"
    );
}
```

```csharp
public partial class CodeToolkit
{
    [Skill]
    public Skill SecurityAudit() => SkillFactory.Create(
        name: "Security Audit",
        description: "Structured security review — vulnerabilities, dependencies, then logic",
        functionResult: null,
        systemPrompt: """
            SECURITY AUDIT PROCEDURE:
            1. ScanDependencies first — known CVEs before reading any code
            2. ReviewCode for injection, auth, and data exposure issues
            3. ReviewCode again for logic errors and edge cases
            4. Rate each finding: Critical / High / Medium / Low
            5. For each finding, show the vulnerable code and the corrected version
            """,
        "CodeToolkit.ScanDependencies",
        "CodeToolkit.ReviewCode",
        "CodeToolkit.ExplainCode"
    );
}
```

---

## Step 4 — What the agent sees at each level

At rest, three entries:

```
┌──────────────────────────────────────────────────────────────────────┐
│ MathToolkit    — Container MathToolkit provides access to: Add,      │
│                  Subtract, Multiply, Divide, SquareRoot,             │
│                  Solve Equation. Math tutoring — step-by-step...     │
├──────────────────────────────────────────────────────────────────────┤
│ WritingToolkit — Container WritingToolkit provides access to:        │
│                  AnalyseStructure, SuggestRewrite, CheckGrammar,     │
│                  Deep Edit. Writing assistance — drafting...         │
├──────────────────────────────────────────────────────────────────────┤
│ CodeToolkit    — Container CodeToolkit provides access to:           │
│                  ReviewCode, ScanDependencies, ExplainCode,          │
│                  Security Audit. Code review and debugging...        │
└──────────────────────────────────────────────────────────────────────┘
```

User asks: **"Can you do a full security review of this function?"**

Agent opens `CodeToolkit`. Persona injects. Agent sees the individual tools plus the `Security Audit` skill as a single entry:

```
[CodeToolkit active — code reviewer persona in system prompt]

Tools visible:
  ReviewCode       — Review code for bugs, security issues...
  ScanDependencies — Scan dependencies for known vulnerabilities
  ExplainCode      — Explain what a piece of code does...
  Security Audit   — Structured security review — vulnerabilities, dependencies, then logic
```

Agent activates `Security Audit`. Procedure injects. Now the agent has the persona *and* the step-by-step methodology:

```
[CodeToolkit active]
  You are now a code reviewer. Flag security issues before style issues...

[Security Audit active]
  SECURITY AUDIT PROCEDURE:
  1. ScanDependencies first — known CVEs before reading any code
  2. ReviewCode for injection, auth, and data exposure issues
  ...
```

The turn ends. Everything collapses. Next turn starts clean.

---

## Step 5 — When to use MultiAgent instead

This pattern works when:
- Domains share the same conversation history naturally
- The user switches between domains in one session
- You want one coherent agent the user talks to

Use **MultiAgent** instead when:
- Domains need genuinely isolated context — the code reviewer shouldn't see the math conversation
- Different domains need different models
- Domain outputs feed into each other as a pipeline

The rule of thumb: if the agent is *switching hats*, use toolkits and skills. If the domains are *separate jobs that coordinate*, use MultiAgent.
