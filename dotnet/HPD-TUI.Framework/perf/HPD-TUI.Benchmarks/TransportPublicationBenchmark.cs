using BenchmarkDotNet.Attributes;
using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Benchmarks;

/// <summary>Measures accepted and zero-byte-backpressured publication through the public renderer path.</summary>
[MemoryDiagnoser]
public sealed class TransportPublicationBenchmark
{
    private readonly BenchmarkTerminal _terminal = new();
    private TuiRenderer _accepted = null!;
    private Text _text = null!;
    private bool _toggle;

    [GlobalSetup]
    public void Setup()
    {
        _accepted = new TuiRenderer(_terminal, new AcceptingTransport());
        _text = new Text("alpha");
        _accepted.Render(_text);
    }

    [GlobalCleanup]
    public void Cleanup() => _accepted.Dispose();

    [Benchmark(Baseline = true)]
    public void InMemoryAccepted()
    {
        _toggle = !_toggle;
        _text.SetText(_toggle ? "alpha" : "Alpha");
        _accepted.Render(_text);
    }

    [Benchmark]
    public TerminalWriteStatus ZeroByteBackpressure()
    {
        using var renderer = new TuiRenderer(_terminal, new BackpressuredTransport());
        try { renderer.Render(new Text("alpha")); }
        catch (Exception) { return TerminalWriteStatus.Backpressured; }
        return TerminalWriteStatus.Written;
    }

    private sealed class AcceptingTransport : ITerminalOutputTransport
    {
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken cancellationToken = default)
        {
            _ = frame.Payload.Span[^1];
            return ValueTask.FromResult(TerminalWriteResult.Written);
        }
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class BackpressuredTransport : ITerminalOutputTransport
    {
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(TerminalWriteResult.Backpressured);
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class BenchmarkTerminal : ITerminal, ITerminalInput
    {
        public ITerminalInput Input => this;
        public TerminalSize GetSize() => new(120, 40);
        public void Write(ReadOnlySpan<char> text) { }
        public void Flush() { }
        public void HideCursor() { }
        public void ShowCursor() { }
        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(TerminalInputEvent.Stop);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
