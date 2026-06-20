using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Application;

namespace HPD.Agent.ToolHarness.Coding.TUI.Benchmarks;

[MemoryDiagnoser]
public class FileMutationRenderBenchmark
{
    private AgentTuiSessionState _smallDiff = null!;
    private AgentTuiSessionState _largeDiff = null!;
    private AgentTuiSessionState _withDiagnostics = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _smallDiff = CodingBenchmarkScenarios.CreateState();
        await _smallDiff.ApplyEventAsync(CodingBenchmarkScenarios.Mutation(hunkCount: 1, linesPerHunk: 8));

        _largeDiff = CodingBenchmarkScenarios.CreateState();
        await _largeDiff.ApplyEventAsync(CodingBenchmarkScenarios.Mutation(hunkCount: 20, linesPerHunk: 20));

        _withDiagnostics = CodingBenchmarkScenarios.CreateState();
        await _withDiagnostics.ApplyEventAsync(CodingBenchmarkScenarios.Mutation(hunkCount: 20, linesPerHunk: 20));
        await _withDiagnostics.ApplyEventAsync(CodingBenchmarkScenarios.Diagnostics(1_000));
    }

    [Benchmark(Baseline = true)]
    public string SmallDiff()
        => CodingBenchmarkScenarios.RenderTranscript(_smallDiff, width: 100, height: 48);

    [Benchmark]
    public string LargeDiff()
        => CodingBenchmarkScenarios.RenderTranscript(_largeDiff, width: 100, height: 80);

    [Benchmark]
    public string AttachedDiagnostics()
        => CodingBenchmarkScenarios.RenderTranscript(_withDiagnostics, width: 100, height: 80);
}
