using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Application;

namespace HPD.Agent.ToolHarness.Coding.TUI.Benchmarks;

[MemoryDiagnoser]
public class CodingCommandStreamingBenchmark
{
    [Params(100, 1_000, 10_000)]
    public int Chunks { get; set; }

    [Benchmark(Baseline = true)]
    public async Task<AgentTuiSessionState> StdoutOnly()
    {
        var state = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateCommandAsync(state, Chunks);
        return state;
    }

    [Benchmark]
    public async Task<AgentTuiSessionState> StderrOnly()
    {
        var state = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateCommandAsync(
            state,
            Chunks,
            ExecuteCommandStreamKind.Stderr);
        return state;
    }

    [Benchmark]
    public async Task<AgentTuiSessionState> MixedStreams()
    {
        var state = CodingBenchmarkScenarios.CreateState();
        await state.ApplyEventAsync(CodingBenchmarkScenarios.Started("dotnet test"));
        for (var i = 0; i < Chunks; i++)
        {
            var stream = i % 2 == 0 ? ExecuteCommandStreamKind.Stdout : ExecuteCommandStreamKind.Stderr;
            await state.ApplyEventAsync(CodingBenchmarkScenarios.Output($"line {i:D5} {new string('x', 80)}\n", stream));
        }

        return state;
    }
}
