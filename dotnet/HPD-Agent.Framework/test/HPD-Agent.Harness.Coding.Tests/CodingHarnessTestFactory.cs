using HPD.Agent;
using HPDOS.ToolHarnesses.Middleware;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ToolHarness.Coding.Tests;

// Unit tests supply an explicit fake settings handle. Integration tests exercise
// the generated binding through AgentBuilder instead of this test-only factory.
internal static class CodingHarnessTestFactory
{
    public static TestCodingHarness Create(
        GlobSearchOptions? globSearchOptions = null,
        IFileMutationLockProvider? fileMutationLockProvider = null,
        IEnumerable<IFileMutationTextSink>? fileMutationTextSinks = null,
        IEnumerable<IFileMutationHistorySink>? fileMutationHistorySinks = null,
        ExecuteCommandOptions? executeCommandOptions = null)
        => new(new TestConfig(new CodingHarnessConfig
        {
            Glob = globSearchOptions ?? new(), Commands = executeCommandOptions ?? new()
        }), fileMutationLockProvider,
            fileMutationTextSinks, fileMutationHistorySinks);

    private sealed class TestConfig(CodingHarnessConfig value) : IHarnessConfig<CodingHarnessConfig>
    {
        public CodingHarnessConfig AgentDefaults => value;
        public CodingHarnessConfig For(IHarnessConfigurationContext context) => value;
    }
}
