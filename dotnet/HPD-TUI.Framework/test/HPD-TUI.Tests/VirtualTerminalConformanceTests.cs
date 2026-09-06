using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class VirtualTerminalConformanceTests
{
    [Fact]
    public void RandomizedEmittedAnsi_IncrementalAndForcedFullRemainSemanticallyEquivalent()
    {
        const int seed = 0x485044;
        var random = new Random(seed);
        var size = new TerminalSize(24, 8);
        using var incrementalTerminal = new RecordedTerminal(size.Width, size.Height);
        using var incrementalRenderer = new ManagedTerminalTuiRenderer(incrementalTerminal) { TrackHardwareCursor = true };
        var incrementalOracle = new VirtualTerminalOracle(size.Width, size.Height);
        var threads = new[] { new MutableAnsiScreen("thread-a"), new MutableAnsiScreen("thread-b") };
        var active = 0;
        IComponent root = threads[active];
        var appliedCharacters = 0;

        for (var iteration = 0; iteration < 160; iteration++)
        {
            var operation = "mutate";
            switch (random.Next(10))
            {
                case 0:
                    operation = "resize";
                    size = new TerminalSize(random.Next(18, 31), random.Next(6, 11));
                    incrementalTerminal.Size = size;
                    incrementalOracle.Resize(size.Width, size.Height);
                    break;
                case 1:
                    operation = "clear";
                    threads[active].Clear();
                    break;
                case 2:
                    operation = "replacement";
                    threads[active] = threads[active].CloneAsReplacement();
                    root = threads[active];
                    break;
                case 3:
                    operation = "thread-switch";
                    active = 1 - active;
                    root = threads[active];
                    break;
                default:
                    operation = threads[active].Mutate(random, size.Width, size.Height);
                    break;
            }

            incrementalRenderer.Render(root);
            var incrementalBytes = incrementalTerminal.Output;
            var delta = incrementalBytes[appliedCharacters..];
            incrementalOracle.Apply(delta);
            appliedCharacters = incrementalBytes.Length;

            using var fullTerminal = new RecordedTerminal(size.Width, size.Height);
            using var fullRenderer = new ManagedTerminalTuiRenderer(fullTerminal) { TrackHardwareCursor = true };
            fullRenderer.Render(root);
            var fullOracle = new VirtualTerminalOracle(size.Width, size.Height);
            fullOracle.Apply(fullTerminal.Output);

            AssertTerminalEquivalent(incrementalOracle, fullOracle, iteration, operation, delta);
        }
    }

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

        var oracle = new VirtualTerminalOracle(8, 4);
        oracle.Apply(display.Output);
        Assert.Empty(oracle.Scrollback);
        Assert.Equal("old", oracle.Line(0));
        Assert.Equal("界", oracle.Line(1));
        Assert.Equal("live", oracle.Line(2));
        Assert.Equal(2, renderer.LiveTop);
        Assert.Equal(1, renderer.LiveHeight);
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
    public void BackpressureAndFailureRecovery_OnlyAcceptedAnsiChangesTheOracle()
    {
        var oracle = new VirtualTerminalOracle(8, 3);
        var transport = new ApplyingScriptedTransport(oracle,
            TerminalWriteStatus.Written,
            TerminalWriteStatus.Backpressured,
            TerminalWriteStatus.Failed,
            TerminalWriteStatus.Written);
        var publisher = new TerminalPublicationCoordinator(transport);
        var initial = new TerminalPresentationState(2, 4, 0, 3, 0, false, TerminalCertainty.Known);

        Assert.Equal(TerminalWriteStatus.Written, publisher.TryPublish("old", acceptedState: initial).Status);
        Assert.Equal("old", oracle.Line(0));
        Assert.Equal(TerminalWriteStatus.Backpressured,
            publisher.TryPublish("-discarded", acceptedState: initial with { CommittedWatermark = 5 }).Status);
        Assert.Equal("old", oracle.Line(0));
        Assert.Equal(4, publisher.State.CommittedWatermark);

        Assert.Equal(TerminalWriteStatus.Failed,
            publisher.TryPublish("-possibly-partial", acceptedState: initial with { CommittedWatermark = 5 }).Status);
        Assert.Equal(TerminalCertainty.Uncertain, publisher.State.Certainty);
        Assert.Equal("old", oracle.Line(0));

        var recovery = "\x1b[3J\x1b[2J\x1b[Hnew";
        var recovered = initial with { PresentationEpoch = 3, CommittedWatermark = 0, Certainty = TerminalCertainty.Known };
        Assert.Equal(TerminalWriteStatus.Written, publisher.TryPublish(recovery, acceptedState: recovered).Status);
        Assert.Equal("new", oracle.Line(0));
        Assert.Empty(oracle.Scrollback);
        Assert.Equal(recovered, publisher.State);
        Assert.Equal(["old", "-discarded", "-possibly-partial", recovery], transport.Payloads);
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

    private static void AssertTerminalEquivalent(VirtualTerminalOracle actual, VirtualTerminalOracle expected, int iteration, string operation, string delta)
    {
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        Assert.True(
            (expected.CursorX, expected.CursorY, expected.CursorVisible, expected.CursorShape) ==
            (actual.CursorX, actual.CursorY, actual.CursorVisible, actual.CursorShape),
            $"Cursor mismatch after mutation {iteration} ({operation}): expected ({expected.CursorX},{expected.CursorY},{expected.CursorVisible},{expected.CursorShape}), actual ({actual.CursorX},{actual.CursorY},{actual.CursorVisible},{actual.CursorShape}); ANSI={Convert.ToHexString(Encoding.UTF8.GetBytes(delta))}.");
        Assert.Equal((expected.Autowrap, expected.SynchronizedOutputDepth, expected.ActiveHyperlink),
            (actual.Autowrap, actual.SynchronizedOutputDepth, actual.ActiveHyperlink));
        for (var y = 0; y < expected.Height; y++)
            for (var x = 0; x < expected.Width; x++)
                Assert.True(SemanticallyEqual(expected[x, y], actual[x, y]),
                    $"ANSI differential mismatch at ({x},{y}) after mutation {iteration}: expected {expected[x, y]}, actual {actual[x, y]}.");
    }

    private static bool SemanticallyEqual(VirtualTerminalOracle.Cell left, VirtualTerminalOracle.Cell right)
    {
        if (left == right) return true;
        return IsDefaultBlank(left) && IsDefaultBlank(right);

        static bool IsDefaultBlank(VirtualTerminalOracle.Cell cell) =>
            !cell.Continuation && cell.Hyperlink is null &&
            cell.Text is null or " " && cell.Style is null or "0";
    }

    private sealed class MutableAnsiScreen(string identity) : Component
    {
        private static readonly string[] Values = ["alpha", "界x", "e\u0301", "👩🏽‍💻", "omega", ""];
        private readonly string _identity = identity;
        private readonly string[] _rows = new string[10];
        private int _cursorX;
        private int _cursorY;
        private int _style;
        private int _link;

        public override ComponentDependencies Dependencies => new(
            RenderContextFields.Width | RenderContextFields.Height,
            RenderContextFields.Width | RenderContextFields.Height);

        internal string Mutate(Random random, int width, int height)
        {
            var operation = random.Next(4);
            switch (operation)
            {
                case 0: _rows[random.Next(_rows.Length)] = Values[random.Next(Values.Length)]; break;
                case 1: _style = random.Next(3); break;
                case 2: _link = random.Next(4); break;
                default: _cursorX = random.Next(width); _cursorY = random.Next(height); break;
            }
            InvalidatePaint();
            return operation switch { 0 => "row", 1 => "style", 2 => "link", _ => $"cursor({_cursorX},{_cursorY})" };
        }

        internal void Clear()
        {
            Array.Clear(_rows);
            _cursorX = _cursorY = _style = _link = 0;
            InvalidatePaint();
        }

        internal MutableAnsiScreen CloneAsReplacement()
        {
            var replacement = new MutableAnsiScreen(_identity + "-replacement")
            {
                _cursorX = _cursorX,
                _cursorY = _cursorY,
                _style = _style,
                _link = _link
            };
            _rows.CopyTo(replacement._rows, 0);
            return replacement;
        }

        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints)
            => new(context.Width, context.Width, context.Height);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            var style = _style switch
            {
                1 => new Style(Color.Cyan, Color.Default, TextAttributes.Bold),
                2 => new Style(Color.Yellow, Color.Blue, TextAttributes.Underline),
                _ => Style.Default
            };
            TerminalHyperlink? hyperlink = null;
            if (_link != 0) TerminalHyperlinkPolicy.TryCreate($"https://example.test/{_link}", out hyperlink);
            for (var row = 0; row < Math.Min(context.Height, _rows.Length); row++)
            {
                output.MoveTo(0, row);
                output.Write(_rows[row] ?? "", style, new TerminalRunMetadata(hyperlink));
            }
            output.SetTerminalCursor(Math.Clamp(_cursorX, 0, context.Width - 1), Math.Clamp(_cursorY, 0, context.Height - 1));
        }
    }

    private sealed class RecordedTerminal : ITerminal, ITerminalInput, IManagedTerminalCapabilitySource
    {
        private readonly StringBuilder _output = new();
        internal RecordedTerminal(int width, int height) => Size = new(width, height);
        public string Output => _output.ToString();
        internal TerminalSize Size { get; set; }
        public ITerminalInput Input => this;
        public ManagedTerminalCapabilityProfile ManagedTerminalCapabilities => ManagedTerminalCapabilityProfile.Verified;
        public TerminalSize GetSize() => Size;
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

    private sealed class ApplyingScriptedTransport(
        VirtualTerminalOracle oracle,
        params TerminalWriteStatus[] statuses) : ITerminalOutputTransport
    {
        private int _index;
        internal List<string> Payloads { get; } = [];
        public ValueTask<TerminalWriteResult> TryWriteFrameAsync(TerminalFrameLease frame, CancellationToken cancellationToken = default)
        {
            var payload = frame.Payload.ToString();
            Payloads.Add(payload);
            var status = statuses[_index++];
            if (status == TerminalWriteStatus.Written) oracle.Apply(payload);
            return ValueTask.FromResult(new TerminalWriteResult(status,
                status == TerminalWriteStatus.Failed ? new IOException("possibly partial") : null));
        }
        public ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
