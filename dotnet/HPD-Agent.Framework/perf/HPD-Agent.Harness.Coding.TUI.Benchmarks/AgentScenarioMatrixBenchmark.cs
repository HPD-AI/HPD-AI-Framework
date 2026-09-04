using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Application;

namespace HPD.Agent.ToolHarness.Coding.TUI.Benchmarks;

/// <summary>Exercises every agent-level scenario named by the compositor proposal.</summary>
[MemoryDiagnoser]
public sealed class AgentScenarioMatrixBenchmark
{
    private AgentTuiSessionState _history = null!;
    private AgentTuiSessionState _live = null!;
    private int _sequence;

    [GlobalSetup]
    public async Task Setup()
    {
        _history = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateCommandAsync(_history, 1_000);
        _live = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateCommandAsync(_live, 100);
        _ = CodingBenchmarkScenarios.RenderTranscript(_history, 100, 18);
        _ = CodingBenchmarkScenarios.RenderTranscript(_live, 100, 18);
    }

    [Benchmark(Baseline = true, Description = "1000-entry transcript / 16-row viewport")]
    public string ThousandEntryViewport() => CodingBenchmarkScenarios.RenderTranscript(_history, 100, 18);

    [Benchmark(Description = "append final entry")]
    public async Task<string> AppendFinalEntry()
    {
        await _live.ApplyEventAsync(CodingBenchmarkScenarios.Output($"final-{_sequence++}\n"));
        return CodingBenchmarkScenarios.RenderTranscript(_live, 100, 18);
    }

    [Benchmark(Description = "update live viewport tail")]
    public async Task<string> UpdateLiveTail()
    {
        await _live.ApplyEventAsync(CodingBenchmarkScenarios.Output($"tail-{_sequence++}\n"));
        return CodingBenchmarkScenarios.RenderTranscript(_live, 100, 18);
    }

    [Benchmark(Description = "update non-visible keyed entry")]
    public async Task<string> UpdateNonVisibleEntry()
    {
        await _history.ApplyEventAsync(CodingBenchmarkScenarios.Output($"offscreen-{_sequence++}\n"));
        return CodingBenchmarkScenarios.RenderTranscript(_history, 100, 18);
    }

    [Benchmark(Description = "status-only / stable transcript")]
    public string StatusOnlyStableTranscript() => CodingBenchmarkScenarios.RenderTranscript(_history, 100, 18);

    [Benchmark(Description = "stream 100KB markdown-equivalent chunks")]
    public async Task<string> StreamHundredKilobytes()
    {
        var state = CodingBenchmarkScenarios.CreateState();
        await CodingBenchmarkScenarios.PopulateCommandAsync(state, 1_000, line: $"**token** {new string('m', 90)}\n");
        return CodingBenchmarkScenarios.RenderTranscript(state, 100, 18);
    }

    [Benchmark(Description = "scroll large transcript")]
    public string ScrollLargeTranscript() => CodingBenchmarkScenarios.RenderTranscript(_history, 64, 48);

    [Benchmark(Description = "switch thread / rehydrate history")]
    public string SwitchThreadAndRehydrate() => CodingBenchmarkScenarios.RenderTranscript((_sequence++ & 1) == 0 ? _history : _live, 100, 18);

    [Benchmark(Description = "normal-terminal scrollback commit materialization")]
    public string MaterializeScrollbackRows() => CodingBenchmarkScenarios.RenderTranscript(_history, 120, 80);
}
