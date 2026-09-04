using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class VirtualTerminalConformanceTests
{
    [Fact]
    public void Oracle_ModelsRequiredTerminalStateAndFailsClosed()
    {
        var terminal = new VirtualTerminalOracle(5, 3);
        terminal.Apply("\x1b[?2026h\x1b[?25l\x1b[?7h\x1b]8;;https://hpd.dev\x1b\\界a\x1b]8;;\x1b\\bcde");

        Assert.Equal("界abc", terminal.Line(0));
        Assert.True(terminal[1, 0].Continuation);
        Assert.Equal("https://hpd.dev", terminal[0, 0].Hyperlink);
        Assert.Null(terminal[3, 0].Hyperlink);
        Assert.Equal("de", terminal.Line(1));
        Assert.False(terminal.CursorVisible);
        Assert.Equal(1, terminal.SynchronizedOutputDepth);

        terminal.Apply("\x1b[?2026l\x1b[2;3r\x1b[3;1Hbottom\nnext\u001b7\x1b[1;1Htop\u001b8\x1b[2 q");
        Assert.Equal(0, terminal.SynchronizedOutputDepth);
        Assert.Equal(2, terminal.CursorShape);
        terminal.Resize(4, 2);
        Assert.Equal((4, 2), (terminal.Width, terminal.Height));

        var history = new VirtualTerminalOracle(2, 2);
        history.Apply("a\r\nb\r\nc\r\n");
        Assert.Equal(["a", "b"], history.Scrollback);
        history.Apply("\x1b[3J");
        Assert.Empty(history.Scrollback);

        var cursor = new VirtualTerminalOracle(5, 3);
        cursor.Apply("\x1b[2;3H\u001b7\x1b[1;1H\u001b8");
        Assert.Equal((2, 1), (cursor.CursorX, cursor.CursorY));
        Assert.Throws<InvalidDataException>(() => terminal.Apply("\x1b[999z"));
        Assert.Throws<InvalidDataException>(() => terminal.Apply("\x1b]0;title\a"));
        Assert.Throws<InvalidDataException>(() => terminal.Apply("\x1b[?2026l"));
    }

    [Fact]
    public void ManagedRenderer_ByteTraceProducesExpectedScreenScrollbackAndProtocolState()
    {
        using var display = new RecordedTerminal(8, 4);
        using var renderer = new ManagedTerminalTuiRenderer(display);
        renderer.Render(new Text("live"), scrollback: Batch(12, 0, "old", "界"));

        var expectedPrefix = "\x1b[?2026h\x1b[?25l\x1b[?7l\x1b[2J\x1b[H\r";
        Assert.StartsWith(expectedPrefix, display.Output);
        Assert.Contains("old", display.Output);
        Assert.Contains("界", display.Output);
        Assert.Equal(2, Count(display.Output, "\x1b[K\r\n"));
        Assert.Contains("\x1b[?7h\x1b[2J\x1b[H", display.Output);
        Assert.EndsWith("\x1b[?25l\x1b[?2026l", display.Output);

        var oracle = new VirtualTerminalOracle(8, 4);
        oracle.Apply(display.Output);
        Assert.Contains("old", oracle.Scrollback);
        Assert.Contains("界", oracle.Scrollback);
        Assert.Equal("live", oracle.Line(0));
        Assert.True(oracle.Autowrap);
        Assert.False(oracle.CursorVisible);
        Assert.Equal(0, oracle.SynchronizedOutputDepth);
        Assert.Equal(12, renderer.PresentationEpoch);
    }

    [Fact]
    public void PublicationState_AdvancesOnlyForByteExactWrittenLease()
    {
        var transport = new ScriptedTransport(TerminalWriteStatus.Backpressured, TerminalWriteStatus.Written, TerminalWriteStatus.Failed);
        var publisher = new TerminalPublicationCoordinator(transport);
        var state = new TerminalPresentationState(4, 9, 1, 3, 2, true, TerminalCertainty.Known);

        Assert.Equal(TerminalWriteStatus.Backpressured, publisher.TryPublish("first", acceptedState: state).Status);
        Assert.Equal(default, publisher.State);
        Assert.Equal(TerminalWriteStatus.Written, publisher.TryPublish("second", acceptedState: state).Status);
        Assert.Equal(state, publisher.State);
        Assert.Equal(TerminalWriteStatus.Failed, publisher.TryPublish("third", acceptedState: state with { CommittedWatermark = 10 }).Status);
        Assert.Equal(9, publisher.State.CommittedWatermark);
        Assert.Equal(TerminalCertainty.Uncertain, publisher.State.Certainty);
        Assert.Equal(["first", "second", "third"], transport.Payloads);
    }

    [Fact]
    public void RandomizedSupportedTrace_IsDeterministicAndMaintainsCellInvariants()
    {
        const int seed = 0x485044;
        var random = new Random(seed);
        var trace = new StringBuilder();
        var alphabet = new[] { "a", "Z", "界", "😀", "e\u0301" };
        for (var i = 0; i < 2_000; i++)
        {
            switch (random.Next(8))
            {
                case 0: trace.Append(alphabet[random.Next(alphabet.Length)]); break;
                case 1: trace.Append($"\x1b[{random.Next(1, 7)};{random.Next(1, 13)}H"); break;
                case 2: trace.Append("\x1b[2K"); break;
                case 3: trace.Append(random.Next(2) == 0 ? "\x1b[?7l" : "\x1b[?7h"); break;
                case 4: trace.Append("\r\n"); break;
                case 5: trace.Append("\u001b7"); break;
                case 6: trace.Append("\u001b8"); break;
                default: trace.Append(random.Next(2) == 0 ? "\x1b]8;;https://hpd.dev\a" : "\x1b]8;;\a"); break;
            }
        }

        var left = new VirtualTerminalOracle(12, 6);
        var right = new VirtualTerminalOracle(12, 6);
        left.Apply(trace.ToString());
        right.Apply(trace.ToString());
        for (var y = 0; y < 6; y++)
        {
            Assert.Equal(left.Line(y), right.Line(y));
            for (var x = 0; x < 12; x++)
            {
                Assert.Equal(left[x, y], right[x, y]);
                if (left[x, y].Continuation)
                    Assert.True(x > 0 && left[x - 1, y].Text is not null);
            }
        }
        Assert.Equal((left.CursorX, left.CursorY, left.Autowrap, left.ActiveHyperlink),
            (right.CursorX, right.CursorY, right.Autowrap, right.ActiveHyperlink));
    }

    private static ScrollbackBatch Batch(long epoch, long first, params string[] rows) => new(epoch, first,
        rows.Select((text, index) => new ScrollbackRow($"row-{index}", [new ScrollbackCell(text, default, default, checked((byte)text.Length))])).ToArray());

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }

    private sealed class RecordedTerminal(int width, int height) : ITerminal, ITerminalInput, IManagedTerminalCapabilitySource
    {
        private readonly StringBuilder _output = new();
        public string Output => _output.ToString();
        public ITerminalInput Input => this;
        public ManagedTerminalCapabilityProfile ManagedTerminalCapabilities => ManagedTerminalCapabilityProfile.Verified;
        public TerminalSize GetSize() => new(width, height);
        public void Write(ReadOnlySpan<char> text) => _output.Append(text);
        public void Flush() { }
        public void HideCursor() { }
        public void ShowCursor() { }
        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromCanceled<TerminalInputEvent>(cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class ScriptedTransport(params TerminalWriteStatus[] statuses) : ITerminalOutputTransport
    {
        private int _index;
        internal List<string> Payloads { get; } = [];
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken cancellationToken = default)
        {
            Payloads.Add(frame.Payload.ToString());
            var status = statuses[_index++];
            return ValueTask.FromResult(new TerminalWriteResult(status, status == TerminalWriteStatus.Failed ? new IOException("partial") : null));
        }
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
