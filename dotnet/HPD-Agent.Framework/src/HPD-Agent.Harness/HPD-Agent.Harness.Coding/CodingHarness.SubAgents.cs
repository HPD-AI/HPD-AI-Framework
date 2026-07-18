using HPD.Agent;

public partial class CodingToolHarness
{
    private static readonly string[] ReadOnlyCodingFunctions =
    [
        nameof(ReadFile),
        nameof(ListDirectory),
        nameof(GlobSearch),
        nameof(Grep)
    ];

    private static readonly string[] WorkerCodingFunctions =
    [
        .. ReadOnlyCodingFunctions,
        nameof(EditFile),
        nameof(WriteFile),
        nameof(ExecuteCommand)
    ];

    [SubAgent]
    public SubAgent Explore()
    {
        return SubAgent.FromConfig(
            "coding/explorer",
            "explore",
            "Investigates a focused codebase question using read-only coding tools and returns evidence with exact file and symbol references.",
            CreateCodingSubAgentConfig(
                name: "Coding Explorer",
                instructions: CodingSubAgentPrompts.Explorer,
                functions: ReadOnlyCodingFunctions,
                maxIterations: 15),
            SubAgentExecutionPolicies.ParentSessionForkedThread(),
            metadata: new Dictionary<string, object>
            {
                ["codingRole"] = "explore",
                ["workspaceAccess"] = "read-only"
            },
            invocationModePolicy: AgentInvocationModePolicy.ModelChoice,
            backgroundNotification: null);
    }

    [SubAgent]
    public SubAgent Worker()
    {
        return SubAgent.FromConfig(
            "coding/worker",
            "worker",
            "Implements a clearly scoped coding task in the shared workspace, verifies the result, and preserves unrelated work.",
            CreateCodingSubAgentConfig(
                name: "Coding Worker",
                instructions: CodingSubAgentPrompts.Worker,
                functions: WorkerCodingFunctions,
                maxIterations: 30),
            SubAgentExecutionPolicies.ParentSessionForkedThread(),
            metadata: new Dictionary<string, object>
            {
                ["codingRole"] = "worker",
                ["workspaceAccess"] = "read-write"
            },
            invocationModePolicy: AgentInvocationModePolicy.ModelChoice,
            backgroundNotification: null);
    }

    [SubAgent]
    public SubAgent Reviewer()
    {
        return SubAgent.FromConfig(
            "coding/reviewer",
            "reviewer",
            "Performs an independent read-only code review and reports concrete findings ordered by severity.",
            CreateCodingSubAgentConfig(
                name: "Coding Reviewer",
                instructions: CodingSubAgentPrompts.Reviewer,
                functions: ReadOnlyCodingFunctions,
                maxIterations: 15),
            SubAgentExecutionPolicies.ParentSessionForkedThread(),
            metadata: new Dictionary<string, object>
            {
                ["codingRole"] = "reviewer",
                ["workspaceAccess"] = "read-only"
            },
            invocationModePolicy: AgentInvocationModePolicy.ModelChoice,
            backgroundNotification: null);
    }

    private static AgentConfig CreateCodingSubAgentConfig(
        string name,
        string instructions,
        IReadOnlyList<string> functions,
        int maxIterations)
    {
        return new AgentConfig
        {
            Name = name,
            SystemInstructions = instructions,
            MaxAgenticIterations = maxIterations,
            ToolHarnesses =
            [
                new ToolHarnessReference
                {
                    Name = nameof(CodingToolHarness),
                    Functions = [.. functions]
                }
            ]
        };
    }
}

internal static class CodingSubAgentPrompts
{
    public const string Explorer = """
        You are a focused codebase explorer. Answer the delegated question by inspecting the workspace with the available read-only coding tools.

        Scope your investigation tightly. Prefer direct evidence from source, tests, configuration, and documentation. Report exact file paths and symbols, distinguish confirmed behavior from inference, and identify important uncertainty. Do not modify files. Do not claim to have run commands or tests because command execution is not available to you.
        """;

    public const string Worker = """
        You are a coding worker operating in a shared workspace. Implement the delegated task completely within the ownership boundary stated in the input.

        Inspect relevant code before editing, follow established conventions, add or update focused tests, and run the smallest meaningful validation. Other agents or the user may be editing the same worktree: preserve unrelated changes, never revert work you do not own, and adapt to concurrent edits. If the requested ownership boundary is unclear or conflicts with existing changes, report the conflict instead of expanding scope silently.
        """;

    public const string Reviewer = """
        You are an independent code reviewer. Inspect the requested implementation and report bugs, correctness risks, behavioral regressions, security problems, and missing tests.

        Present findings first, ordered by severity. Every finding must include concrete evidence and an exact file or symbol reference. Do not modify files. If no findings are confirmed, say so explicitly and identify any residual test or verification gaps. Do not claim to have run commands or tests because command execution is not available to you.
        """;
}
