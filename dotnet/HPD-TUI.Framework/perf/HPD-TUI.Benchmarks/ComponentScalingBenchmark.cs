using BenchmarkDotNet.Attributes;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Benchmarks;

/// <summary>Measures retained component-tree scaling and mutation locality.</summary>
[MemoryDiagnoser]
public sealed class ComponentScalingBenchmark
{
    [Params(10, 100, 1000)] public int ComponentCount { get; set; }
    private Stack _root = null!;
    private Text _tail = null!;
    private MutableTerminal _terminal = null!;
    private TuiRenderer _renderer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _root = new Stack();
        for (var i = 0; i < ComponentCount; i++)
        {
            _tail = new Text($"row-{i:D4}");
            _root.Add(_tail);
        }
        _terminal = new MutableTerminal(120, 40);
        _renderer = new TuiRenderer(_terminal);
        _renderer.Render(_root);
    }

    [GlobalCleanup]
    public void Cleanup() => _renderer.Dispose();

    [Benchmark(Baseline = true)]
    public void StableTree() => _renderer.Render(_root);

    [Benchmark]
    public void TailPaintMutation()
    {
        _tail.SetStyle(_tail.Style == Style.Default ? Theme.Default.Accent : Style.Default);
        _renderer.Render(_root);
    }

    [Benchmark]
    public void LayoutAffectingMutation()
    {
        _tail.SetText(_tail.Value.Length == 8 ? "layout-expanded-tail" : "row-0999");
        _renderer.Render(_root);
    }

    private sealed class MutableTerminal(int width, int height) : ITerminal, ITerminalInput
    {
        public ITerminalInput Input => this;
        public TerminalSize GetSize() => new(width, height);
        public void Write(ReadOnlySpan<char> text) { }
        public void Flush() { }
        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(TerminalInputEvent.Stop);
        public void HideCursor() { }
        public void ShowCursor() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
