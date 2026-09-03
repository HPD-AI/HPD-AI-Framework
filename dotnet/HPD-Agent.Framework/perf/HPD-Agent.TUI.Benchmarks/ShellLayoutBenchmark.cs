using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Rendering;

namespace HPD.Agent.TUI.Benchmarks;

[MemoryDiagnoser]
public class ShellLayoutBenchmark
{
    private HpdAgentTuiRegistry _registry = null!;
    private AgentTuiSessionState _state = null!;
    private IComponent _shell = null!;
    private ActivityModel _activity = null!;

    [GlobalSetup]
    public void Setup()
    {
        _registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .Build();
        _state = new AgentTuiSessionState(new AgentTuiRuntimeScope("agent", "session", "main"), _registry);
        for (var i = 0; i < 500; i++)
        {
            _state.Shell.Transcript.AddFinal(new TranscriptEntry(
                Id: $"entry-{i:D4}",
                EntryKey: $"entry:{i:D4}",
                Cell: new UserMessageCell($"message {i:D4} {new string('x', 96)}"),
                Metadata: new TranscriptEntryMetadata(),
                VerticalSpacing: 0));
        }

        _activity = _state.Shell.Activities.Add("rendering");
        var prompt = _registry.PromptFactory.Create(
            new AgentTuiPromptContext(_state.Scope, _state.Shell),
            _ => { },
            new AutocompleteController());
        _shell = _registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
            _state.Shell,
            prompt,
            _registry,
            _registry.ShellChrome,
            _state.State));
    }

    [Benchmark(Baseline = true)]
    public string TranscriptPlusEditor()
        => Render();

    [Benchmark]
    public string StatusChurn()
    {
        _activity.Progress = (_activity.Progress ?? 0) >= 1 ? 0 : (_activity.Progress ?? 0) + 0.05;
        return Render();
    }

    private string Render()
        => TuiCapture.RenderToString(_shell, width: 120, height: 40, trimTrailingBlankLines: false);
}
