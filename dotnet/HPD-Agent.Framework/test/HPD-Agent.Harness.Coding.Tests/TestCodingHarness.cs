using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

/// <summary>
/// Supplies an explicit logical workspace for legacy direct tool tests whose
/// call sites predate the required Agent execution context.
/// </summary>
internal sealed class TestCodingHarness(
    IHarnessConfig<CodingHarnessConfig> config,
    IFileMutationLockProvider? fileMutationLockProvider = null,
    IEnumerable<IFileMutationTextSink>? fileMutationTextSinks = null,
    IEnumerable<IFileMutationHistorySink>? fileMutationHistorySinks = null)
    : CodingToolHarness(
        config,
        fileMutationLockProvider,
        fileMutationTextSinks,
        fileMutationHistorySinks)
{
    public new Task<string> ListDirectory(
        string path,
        int offset = 1,
        int limit = 200,
        bool recursive = false,
        int? maxDepth = null,
        bool includeHidden = false,
        bool respectIgnoreFiles = true,
        DirectoryEntryKindFilter kind = DirectoryEntryKindFilter.All,
        DirectorySortBy sortBy = DirectorySortBy.Name,
        SortDirection sortDirection = SortDirection.Ascending,
        bool includeMetadata = false,
        FunctionExecutionContext? context = null) =>
        base.ListDirectory(
            path,
            offset,
            limit,
            recursive,
            maxDepth,
            includeHidden,
            respectIgnoreFiles,
            kind,
            sortBy,
            sortDirection,
            includeMetadata,
            context ?? CreateContext());

    public new Task<string> GlobSearch(
        string pattern,
        string path = ".",
        int offset = 1,
        int limit = 200,
        bool caseSensitive = false,
        bool includeHidden = false,
        bool respectIgnoreFiles = true,
        GlobEntryKindFilter kind = GlobEntryKindFilter.Files,
        GlobSortBy sortBy = GlobSortBy.Path,
        SortDirection sortDirection = SortDirection.Ascending,
        FunctionExecutionContext? context = null) =>
        base.GlobSearch(
            pattern,
            path,
            offset,
            limit,
            caseSensitive,
            includeHidden,
            respectIgnoreFiles,
            kind,
            sortBy,
            sortDirection,
            context ?? CreateContext());

    public new Task<string> Grep(
        string pattern,
        string path = ".",
        GrepOutputMode outputMode = GrepOutputMode.FilesWithMatches,
        int offset = 1,
        int limit = 200,
        string[]? includeGlobs = null,
        string[]? excludeGlobs = null,
        GrepCaseMode caseMode = GrepCaseMode.Smart,
        bool fixedStrings = false,
        bool wordRegexp = false,
        int contextLines = 0,
        int beforeContext = 0,
        int afterContext = 0,
        int? maxMatchesPerFile = null,
        int? maxDepth = null,
        bool multiline = false,
        bool includeHidden = false,
        bool respectIgnoreFiles = true,
        FunctionExecutionContext? context = null) =>
        base.Grep(
            pattern,
            path,
            outputMode,
            offset,
            limit,
            includeGlobs,
            excludeGlobs,
            caseMode,
            fixedStrings,
            wordRegexp,
            contextLines,
            beforeContext,
            afterContext,
            maxMatchesPerFile,
            maxDepth,
            multiline,
            includeHidden,
            respectIgnoreFiles,
            context ?? CreateContext());

    private static FunctionExecutionContext CreateContext()
    {
        string cwd = Directory.GetCurrentDirectory();
        var runConfig = new AgentRunConfig
        {
            Context = new AgentContextRunConfig
            {
                Properties = new Dictionary<string, object>
                {
                    [AgentWorkspace.ContextKey] = new AgentWorkspace(
                        "default",
                        cwd,
                        [
                            new AgentWorkspaceRoot(
                                "default",
                                cwd,
                                AgentWorkspaceAccess.ReadWrite),
                        ]),
                },
            },
        };
        AIFunction function = AIFunctionFactory.Create(
            () => "ok",
            new AIFunctionFactoryOptions
            {
                Name = "test",
                Description = "test",
            });
        AgentLoopState state = AgentLoopState.InitialSafe(
            [],
            "test-run",
            "test-conversation",
            "test-agent");
        var agentContext = new AgentContext(
            "test-agent",
            "test-conversation",
            state,
            new EventCoordinator(),
            new Session("test-session"),
            new Thread("test-session", "test-agent"),
            CancellationToken.None);
        agentContext.RuntimeCapabilities.Set(
            TestRuntimeWorkspace.FromRunConfig(runConfig));
        BeforeFunctionContext before = agentContext.AsBeforeFunction(
            function,
            "test-call",
            new Dictionary<string, object?>(),
            runConfig,
            toolharnessName: "CodingToolHarness");
        return new FunctionExecutionContext(
            before,
            new FunctionRequest
            {
                Function = function,
                CallId = "test-call",
                Arguments = new Dictionary<string, object?>(),
                State = state,
                RunConfig = runConfig,
            });
    }
}
