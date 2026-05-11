using System.Diagnostics;
using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class RenderingPerformanceTests
{
    [Fact]
    public void Render_At120By40_CompletesWithinFrameBudget()
    {
        using var terminal = new NoOpTerminal(120, 40);
        using var renderer = new TuiRenderer(terminal);
        var root = new Container();
        root.Add(new Text("The quick brown fox jumps over the lazy dog."));
        root.Add(new Text("Streaming response line 2."));

        renderer.Render(root);
        var stopwatch = Stopwatch.StartNew();

        renderer.Render(root);

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(16), $"Frame took {stopwatch.Elapsed.TotalMilliseconds:0.###}ms.");
    }

    [Fact]
    public void Render_DoesNotAllocateAfterWarmup()
    {
        using var terminal = new NoOpTerminal(120, 40);
        using var renderer = new TuiRenderer(terminal);
        var root = new Text("Hello world");

        renderer.Render(root);
        renderer.Render(root);
        var before = GC.GetAllocatedBytesForCurrentThread();

        renderer.Render(root);

        var after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(before, after);
    }

    private sealed class NoOpTerminal : ITerminal
    {
        private readonly TerminalSize _size;

        public NoOpTerminal(int width, int height)
        {
            _size = new TerminalSize(width, height);
        }

        public TerminalSize GetSize() => _size;

        public void Write(ReadOnlySpan<char> text)
        {
        }

        public bool TryReadKey(out KeyEvent key)
        {
            key = default;
            return false;
        }

        public void HideCursor()
        {
        }

        public void ShowCursor()
        {
        }

        public void Dispose()
        {
        }
    }
}
