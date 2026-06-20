using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Application;

namespace HPD.Agent.ToolHarness.Coding.TUI.Benchmarks;

[MemoryDiagnoser]
public class ExplorationRenderBenchmark
{
    private AgentTuiSessionState _oneRead = null!;
    private AgentTuiSessionState _manyReads = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _oneRead = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateExplorationAsync(_oneRead, operations: 1);

        _manyReads = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateExplorationAsync(_manyReads, operations: 100);
    }

    [Benchmark(Baseline = true)]
    public string OneRead()
        => CodingBenchmarkScenarios.RenderTranscript(_oneRead, width: 100, height: 24);

    [Benchmark]
    public string ManyReadsCoalesced()
        => CodingBenchmarkScenarios.RenderTranscript(_manyReads, width: 100, height: 48);
}
