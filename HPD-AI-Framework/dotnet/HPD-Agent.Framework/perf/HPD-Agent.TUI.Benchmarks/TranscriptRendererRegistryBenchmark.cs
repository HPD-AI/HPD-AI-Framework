using BenchmarkDotNet.Attributes;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;

namespace HPD.Agent.TUI.Benchmarks;

[MemoryDiagnoser]
public class TranscriptRendererRegistryBenchmark
{
    private readonly TranscriptEntry _entry = new(
        Id: "entry-1",
        EntryKey: "entry:1",
        Cell: new UserMessageCell("hello renderer registry"),
        Metadata: new TranscriptEntryMetadata(),
        VerticalSpacing: 0);

    private AgentTuiTranscriptRendererRegistry _defaultRenderers = null!;
    private AgentTuiTranscriptRendererRegistry _replacedRenderers = null!;
    private AgentTuiTranscriptRendererRegistry _decoratedRenderers = null!;

    [GlobalSetup]
    public void Setup()
    {
        _defaultRenderers = new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .Build()
            .TranscriptRenderers;
        _replacedRenderers = new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .ReplaceTranscriptRenderer<UserMessageCell>(
                AgentTuiTranscriptRendererKeys.UserMessage,
                _ => new Text("replaced"))
            .Build()
            .TranscriptRenderers;
        _decoratedRenderers = new HpdAgentTuiBuilder()
            .AddDefaultTranscriptRenderers()
            .DecorateTranscriptRenderer<UserMessageCell>(
                AgentTuiTranscriptRendererKeys.UserMessage,
                inner => new DecoratingRenderer(inner))
            .Build()
            .TranscriptRenderers;
    }

    [Benchmark(Baseline = true)]
    public string BuiltInsOnly()
        => Render(_defaultRenderers.Create(_entry));

    [Benchmark]
    public string ReplacedRenderer()
        => Render(_replacedRenderers.Create(_entry));

    [Benchmark]
    public string DecoratedRenderer()
        => Render(_decoratedRenderers.Create(_entry));

    private static string Render(IComponent component)
        => TuiCapture.RenderToString(component, width: 80, height: 4, trimTrailingBlankLines: false);

    private sealed class DecoratingRenderer(IAgentTuiTranscriptRenderer<UserMessageCell> inner)
        : IAgentTuiTranscriptRenderer<UserMessageCell>
    {
        public IComponent Create(AgentTuiTranscriptRenderContext<UserMessageCell> context)
            => inner.Create(context);
    }
}
