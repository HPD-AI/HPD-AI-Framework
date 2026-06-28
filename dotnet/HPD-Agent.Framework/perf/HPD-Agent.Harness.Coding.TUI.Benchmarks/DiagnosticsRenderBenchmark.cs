using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Application;

namespace HPD.Agent.ToolHarness.Coding.TUI.Benchmarks;

[MemoryDiagnoser]
public class DiagnosticsRenderBenchmark
{
    private AgentTuiSessionState _none = null!;
    private AgentTuiSessionState _ten = null!;
    private AgentTuiSessionState _thousand = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _none = CodingBenchmarkScenarios.CreateState();

        _ten = CodingBenchmarkScenarios.CreateState();
        await _ten.ApplyEventAsync(CodingBenchmarkScenarios.Diagnostics(10), CodingBenchmarkScenarios.Registry);

        _thousand = CodingBenchmarkScenarios.CreateState();
        await _thousand.ApplyEventAsync(CodingBenchmarkScenarios.Diagnostics(1_000), CodingBenchmarkScenarios.Registry);
    }

    [Benchmark(Baseline = true)]
    public string NoDiagnostics()
        => CodingBenchmarkScenarios.RenderTranscript(_none, width: 100, height: 24);

    [Benchmark]
    public string TenDiagnostics()
        => CodingBenchmarkScenarios.RenderTranscript(_ten, width: 100, height: 48);

    [Benchmark]
    public string ThousandDiagnostics()
        => CodingBenchmarkScenarios.RenderTranscript(_thousand, width: 100, height: 48);
}
