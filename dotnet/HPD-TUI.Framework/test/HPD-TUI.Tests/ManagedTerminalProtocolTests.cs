using System.Collections.Concurrent;
using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class ManagedTerminalProtocolTests
{
    [Fact]
    public void UnverifiedCapabilities_UseBoundedScreenAndRejectScrollback()
    {
        using var terminal = new ProtocolTerminal();
        using var renderer = new ManagedTerminalTuiRenderer(
            terminal,
            new SynchronousProtocolTransport(),
            new ManagedTerminalCapabilityProfile(ManagedTerminalFeatures.None));

        Assert.False(renderer.SupportsManagedScrollback);
        renderer.Render(new Text("live"));
        Assert.Throws<NotSupportedException>(() => renderer.Render(new Text("live"), scrollback: Batch(0, 0)));
    }

    [Fact]
    public void RejectPolicy_FailsBeforeAnyTerminalMutation()
    {
        using var terminal = new ProtocolTerminal();
        Assert.Throws<NotSupportedException>(() => new ManagedTerminalTuiRenderer(
            terminal,
            new SynchronousProtocolTransport(),
            new ManagedTerminalCapabilityProfile(ManagedTerminalFeatures.AbsoluteCursorAddressing),
            ManagedTerminalFallbackPolicy.Reject));
    }

    [Fact]
    public void PresentationEpoch_RejectsStaleHistory()
    {
        using var terminal = new ProtocolTerminal();
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        renderer.Render(new Text("live"), scrollback: Batch(7, 0));

        Assert.Equal(8, renderer.StartPresentationEpoch());
        Assert.Throws<InvalidOperationException>(() => renderer.Render(new Text("live"), scrollback: Batch(7, 1)));
    }

    [Fact]
    public async Task PublicationCoordinator_AdmitsContendersInFifoOrder()
    {
        var transport = new OrderedBlockingTransport();
        var coordinator = new TerminalPublicationCoordinator(transport);
        var first = Task.Run(() => coordinator.TryPublish("first"));
        await transport.FirstEntered.Task;
        var second = Task.Run(() => coordinator.TryPublish("second"));
        await Task.Delay(20);
        var third = Task.Run(() => coordinator.TryPublish("third"));
        transport.ReleaseFirst.Set();

        await Task.WhenAll(first, second, third);
        Assert.Equal(["first", "second", "third"], transport.Payloads);
    }

    private static ScrollbackBatch Batch(long epoch, long sequence) => new(epoch, sequence,
    [
        new ScrollbackRow($"row-{sequence}", [new ScrollbackCell("history", default, default, 7)])
    ]);

    private sealed class ProtocolTerminal : ITerminal, ITerminalInput
    {
        public ITerminalInput Input => this;
        public TerminalSize GetSize() => new(40, 8);
        public void Write(ReadOnlySpan<char> text) { }
        public void Flush() { }
        public void HideCursor() { }
        public void ShowCursor() { }
        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromCanceled<TerminalInputEvent>(cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class SynchronousProtocolTransport : ITerminalOutputTransport
    {
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(TerminalWriteResult.Written);
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class OrderedBlockingTransport : ITerminalOutputTransport
    {
        private int _attempt;
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseFirst { get; } = new(false);
        public ConcurrentQueue<string> Payloads { get; } = new();

        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _attempt);
            if (attempt == 1)
            {
                FirstEntered.TrySetResult();
                ReleaseFirst.Wait(cancellationToken);
            }
            Payloads.Enqueue(frame.Payload.ToString());
            return ValueTask.FromResult(TerminalWriteResult.Written);
        }

        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
