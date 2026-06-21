using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Application;

namespace HPD.Agent.ToolHarness.Coding.TUI.Benchmarks;

[MemoryDiagnoser]
public class CodingCommandRenderBenchmark
{
    private AgentTuiSessionState _normal = null!;
    private AgentTuiSessionState _longLine = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _normal = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateCommandAsync(_normal, chunks: 1_000);

        _longLine = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateCommandAsync(
            _longLine,
            chunks: 100,
            line: $"{new string('a', 4_000)}middle{new string('z', 4_000)}\n");
    }

    [Benchmark(Baseline = true)]
    public string NormalWidth()
        => CodingBenchmarkScenarios.RenderTranscript(_normal, width: 100, height: 24);

    [Benchmark]
    public string NarrowWidth()
        => CodingBenchmarkScenarios.RenderTranscript(_normal, width: 28, height: 24);

    [Benchmark]
    public string LongLine()
        => CodingBenchmarkScenarios.RenderTranscript(_longLine, width: 100, height: 24);

    [Benchmark]
    public string RepeatedSameWidth()
    {
        CodingBenchmarkScenarios.RenderTranscript(_normal, width: 80, height: 24);
        return CodingBenchmarkScenarios.RenderTranscript(_normal, width: 80, height: 24);
    }

    [Benchmark]
    public string WidthChange()
    {
        CodingBenchmarkScenarios.RenderTranscript(_normal, width: 64, height: 24);
        return CodingBenchmarkScenarios.RenderTranscript(_normal, width: 120, height: 24);
    }
}
