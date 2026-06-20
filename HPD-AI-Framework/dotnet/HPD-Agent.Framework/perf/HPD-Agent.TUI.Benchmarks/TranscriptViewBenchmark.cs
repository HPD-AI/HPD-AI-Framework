using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Views;
using HPD.TUI.Rendering;

namespace HPD.Agent.TUI.Benchmarks;

[MemoryDiagnoser]
public class TranscriptViewBenchmark
{
    private AgentTuiTranscriptRendererRegistry _renderers = null!;
    private TranscriptModel _largeTranscript = null!;
    private TranscriptView _largeView = null!;
    private int _appendIndex;
    private int _updateIndex;

    [GlobalSetup]
    public void Setup()
    {
        _renderers = new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .Build()
            .TranscriptRenderers;
        _largeTranscript = CreateTranscript(1_000);
        _largeView = new TranscriptView(_largeTranscript, _renderers, height: 16);
        _appendIndex = 1_000;
        _updateIndex = 990;
        Render(_largeView);
    }

    [Benchmark(Baseline = true)]
    public string LargeTranscriptSmallViewport()
        => Render(_largeView);

    [Benchmark]
    public string RenderWithoutChanges()
    {
        Render(_largeView);
        return Render(_largeView);
    }

    [Benchmark]
    public string AppendOneEntry()
    {
        var model = CreateTranscript(1_000);
        var view = new TranscriptView(model, _renderers, height: 16);
        Render(view);
        model.Append(Row(_appendIndex++));
        return Render(view);
    }

    [Benchmark]
    public string UpdateOneKeyedEntry()
    {
        var model = CreateTranscript(1_000);
        var view = new TranscriptView(model, _renderers, height: 16);
        Render(view);
        model.Update(Row(_updateIndex, $"updated visible row {_updateIndex++:D4}"));
        return Render(view);
    }

    [Benchmark]
    public string Scroll()
    {
        _largeTranscript.ScrollUp(4);
        var output = Render(_largeView);
        _largeTranscript.ScrollDown(4);
        return output;
    }

    private static TranscriptModel CreateTranscript(int count)
    {
        var model = new TranscriptModel();
        for (var i = 0; i < count; i++)
        {
            model.Append(Row(i));
        }

        return model;
    }

    private static TranscriptEntry Row(int index, string? text = null)
        => new(
            Id: $"entry-{index:D4}",
            EntryKey: $"entry:{index:D4}",
            Cell: new UserMessageCell(text ?? $"row {index:D4} {new string('x', 96)}"),
            Metadata: new TranscriptEntryMetadata(),
            VerticalSpacing: 0);

    private static string Render(TranscriptView view)
        => TuiCapture.RenderToString(view, width: 100, height: view.Height, trimTrailingBlankLines: false);
}
