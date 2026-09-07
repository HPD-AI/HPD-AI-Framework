using BenchmarkDotNet.Attributes;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Benchmarks;

[MemoryDiagnoser]
public sealed class ScreenCompositorBenchmark
{
    private readonly NullTerminal _terminal = new(120, 40);
    private readonly TuiRenderer _renderer;
    private readonly MutableRows _rows = new();
    private bool _toggle;

    public ScreenCompositorBenchmark()
    {
        _renderer = new TuiRenderer(_terminal);
        _renderer.Render(_rows);
    }

    [Benchmark(Baseline = true, Description = "120x40 warmed no-op")]
    public void WarmNoOp() => _renderer.Render(_rows);

    [Benchmark(Description = "120x40 one-row paint mutation")]
    public void OneRowMutation()
    {
        _toggle = !_toggle;
        _rows.SetFirst(_toggle ? "alpha" : "Alpha");
        _renderer.Render(_rows);
    }

    [Benchmark(Description = "120x40 two-disjoint-row mutation")]
    public void TwoDisjointRows()
    {
        _toggle = !_toggle;
        _rows.SetBoth(_toggle ? "alpha" : "Alpha", _toggle ? "omega" : "Omega");
        _renderer.Render(_rows);
    }

    private sealed class MutableRows : Component
    {
        private string _first = "alpha";
        private string _last = "omega";
        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);
        public void SetFirst(string value) => SetPaint(ref _first, value);
        public void SetBoth(string first, string last)
        {
            var changed = !string.Equals(_first, first, StringComparison.Ordinal) || !string.Equals(_last, last, StringComparison.Ordinal);
            _first = first;
            _last = last;
            if (changed) InvalidatePaint();
        }
        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(120, 120, 40);
        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            output.MoveTo(0, 0);
            output.Write(_first, context.Theme.Text);
            output.MoveTo(0, 39);
            output.Write(_last, context.Theme.Text);
        }
    }

    private sealed class NullTerminal(int width, int height) : ITerminal, ITerminalInput
    {
        public ITerminalInput Input => this;
        public TerminalSize GetSize() => new(width, height);
        public void Write(ReadOnlySpan<char> text) { }
        public void Flush() { }
        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TerminalInputEvent.Stop);
        public void HideCursor() { }
        public void ShowCursor() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
