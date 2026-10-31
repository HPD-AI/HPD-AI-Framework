using HPD.Agent;
using HPDOS.ToolHarnesses.Middleware;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.Ripgrep;

// Unit tests supply an explicit fake settings handle. Integration tests exercise
// the generated binding through AgentBuilder instead of this test-only factory.
internal static class CodingHarnessTestFactory
{
    public static CodingToolHarness Create(
        IEnumerable<IReadFileTextSource>? readFileTextSources = null,
        IEnumerable<IDirectoryListingSource>? directoryListingSources = null,
        IEnumerable<IGlobSearchPathResolver>? globSearchPathResolvers = null,
        GlobSearchOptions? globSearchOptions = null,
        IRipgrepRunner? ripgrepRunner = null,
        IFileMutationLockProvider? fileMutationLockProvider = null,
        IEnumerable<IFileMutationTextSink>? fileMutationTextSinks = null,
        IEnumerable<IFileMutationHistorySink>? fileMutationHistorySinks = null,
        ExecuteCommandOptions? executeCommandOptions = null)
        => new(new TestConfig(new CodingHarnessConfig
        {
            Glob = globSearchOptions ?? new(), Commands = executeCommandOptions ?? new()
        }), readFileTextSources, directoryListingSources, globSearchPathResolvers, ripgrepRunner,
            fileMutationLockProvider, fileMutationTextSinks, fileMutationHistorySinks);

    private sealed class TestConfig(CodingHarnessConfig value) : IHarnessConfig<CodingHarnessConfig>
    {
        public CodingHarnessConfig AgentDefaults => value;
        public CodingHarnessConfig For(IHarnessConfigurationContext context) => value;
    }
}
