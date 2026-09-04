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
    public void DefaultConstructor_DoesNotAssumeUnreportedCapabilities()
    {
        using var terminal = new UnreportedTerminal();
        using var renderer = new ManagedTerminalTuiRenderer(terminal);

        renderer.Render(new Text("live"));

        Assert.False(renderer.SupportsManagedScrollback);
        Assert.DoesNotContain("\x1b[?2026h", terminal.Output);
        Assert.DoesNotContain("\x1b[H", terminal.Output);
        Assert.DoesNotContain("\x1b[1;", terminal.Output);
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
    public void Resize_StartsNewPresentationEpochBeforeReflowedHistoryIsPrepared()
    {
        using var terminal = new ResizableProtocolTerminal();
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        renderer.Render(new Text("live"), scrollback: Batch(4, 0));

        terminal.Size = new(60, 10);

        Assert.Equal(5, renderer.SynchronizePresentation(terminal.Size));
        renderer.Render(new Text("live"), scrollback: Batch(5, 0));
        Assert.Equal(5, renderer.PresentationEpoch);
    }

    [Fact]
    public void Shutdown_BackpressureDoesNotCloseRendererAndRetryPublishesByteExactCleanup()
    {
        using var terminal = new ProtocolTerminal();
        var transport = new ShutdownScriptTransport(TerminalWriteStatus.Backpressured, TerminalWriteStatus.Written);
        var renderer = new ManagedTerminalTuiRenderer(terminal, transport);

        var first = renderer.Shutdown();
        var second = renderer.Shutdown();

        Assert.Equal(TerminalWriteStatus.Backpressured, first.Status);
        Assert.Equal(TerminalWriteStatus.Written, second.Status);
        Assert.Equal(["\x1b[?7h\x1b[?25h", "\x1b[?7h\x1b[?25h"], transport.Payloads);
        renderer.Dispose();
    }

    [Fact]
    public void Shutdown_FailurePropagatesAndDisposeRefusesToReleaseUncertainLifetime()
    {
        using var terminal = new ProtocolTerminal();
        var error = new IOException("partial cleanup");
        var transport = new ShutdownScriptTransport(new TerminalWriteResult(TerminalWriteStatus.Failed, error));
        var renderer = new ManagedTerminalTuiRenderer(terminal, transport);

        var result = renderer.Shutdown();

        Assert.Equal(TerminalWriteStatus.Failed, result.Status);
        Assert.Same(error, result.Error);
        var thrown = Assert.Throws<InvalidOperationException>(() => renderer.Dispose());
        Assert.Same(error, thrown.InnerException);
    }

    [Fact]
    public void ClearAndReplay_WithoutClearCapability_RemainsUncertainAndEmitsNoCsi3J()
    {
        using var terminal = new CapturingProtocolTerminal();
        var transport = new FailOnceCapturingTransport();
        var features = ManagedTerminalCapabilityProfile.SplitFooterRequirements;
        using var renderer = new ManagedTerminalTuiRenderer(
            terminal, transport, new(features), recoveryPolicy: ManagedTerminalRecoveryPolicy.ClearAndReplay);

        Assert.Throws<InvalidOperationException>(() => renderer.Render(new Text("live")));
        Assert.Throws<InvalidOperationException>(() => renderer.Render(new Text("live")));

        Assert.DoesNotContain("\x1b[3J", transport.AcceptedPayload);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public void HistoryMutationPolicies_AreExternallyVisible()
    {
        using var terminal = new CapturingProtocolTerminal();
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        renderer.Render(new Text("live"));
        terminal.Clear();

        Assert.Equal(ManagedHistoryRebaseStatus.Written,
            renderer.RebaseCommittedHistory(ManagedTerminalRecoveryPolicy.VisibleEpochBoundary).Status);
        Assert.Contains("new presentation epoch", terminal.Output);

        terminal.Clear();
        Assert.Equal(ManagedHistoryRebaseStatus.Written,
            renderer.RebaseCommittedHistory(ManagedTerminalRecoveryPolicy.SwitchToAlternateScreen).Status);
        Assert.Contains("\x1b[?1049h", terminal.Output);

        Assert.Equal(ManagedHistoryRebaseStatus.Aborted,
            renderer.RebaseCommittedHistory(ManagedTerminalRecoveryPolicy.Abort).Status);
    }

    [Fact]
    public void ExternalOutput_UsesPublisherAndRequiresRecoveryBeforeNextFrame()
    {
        using var terminal = new CapturingProtocolTerminal();
        using var renderer = new ManagedTerminalTuiRenderer(terminal);
        renderer.Render(new Text("before"));
        terminal.Clear();

        renderer.PublishExternalOutput("process\r\n");
        renderer.Render(new Text("after"));

        Assert.StartsWith("process\r\n", terminal.Output);
        Assert.Contains("new presentation epoch", terminal.Output);
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

    private sealed class ProtocolTerminal : ITerminal, ITerminalInput, IManagedTerminalCapabilitySource
    {
        public ITerminalInput Input => this;
        public ManagedTerminalCapabilityProfile ManagedTerminalCapabilities => ManagedTerminalCapabilityProfile.Verified;
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

    private class CapturingProtocolTerminal : ITerminal, ITerminalInput, IManagedTerminalCapabilitySource
    {
        private readonly StringWriter _output = new();
        public string Output => _output.ToString();
        public virtual TerminalSize Size { get; set; } = new(40, 8);
        public ITerminalInput Input => this;
        public ManagedTerminalCapabilityProfile ManagedTerminalCapabilities => ManagedTerminalCapabilityProfile.Verified;
        public TerminalSize GetSize() => Size;
        public void Clear() => _output.GetStringBuilder().Clear();
        public void Write(ReadOnlySpan<char> text) => _output.Write(text);
        public void Flush() { }
        public void HideCursor() { }
        public void ShowCursor() { }
        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromCanceled<TerminalInputEvent>(cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class ResizableProtocolTerminal : CapturingProtocolTerminal;

    private sealed class FailOnceCapturingTransport : ITerminalOutputTransport
    {
        public int Attempts { get; private set; }
        public string AcceptedPayload { get; private set; } = "";
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(
            TerminalFrameLease frame, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts == 1)
                return ValueTask.FromResult(new TerminalWriteResult(TerminalWriteStatus.Failed, new IOException("partial")));
            AcceptedPayload += frame.Payload.ToString();
            return ValueTask.FromResult(TerminalWriteResult.Written);
        }
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class UnreportedTerminal : ITerminal, ITerminalInput
    {
        private readonly StringWriter _output = new();
        public string Output => _output.ToString();
        public ITerminalInput Input => this;
        public TerminalSize GetSize() => new(40, 8);
        public void Write(ReadOnlySpan<char> text) => _output.Write(text);
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

    private sealed class ShutdownScriptTransport : ITerminalOutputTransport
    {
        private readonly Queue<TerminalWriteResult> _results;
        private TerminalWriteResult _last;
        public ShutdownScriptTransport(params TerminalWriteStatus[] statuses)
            => _results = new(statuses.Select(status => new TerminalWriteResult(status)));
        public ShutdownScriptTransport(TerminalWriteResult result) => _results = new([result]);
        public List<string> Payloads { get; } = [];
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken cancellationToken = default)
        {
            Payloads.Add(frame.Payload.ToString());
            if (_results.Count > 0) _last = _results.Dequeue();
            return ValueTask.FromResult(_last);
        }
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
