using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class TerminalOutputTransportTests
{
    [Fact]
    public async Task SynchronousTransport_AcceptsCompleteLeasedPayload()
    {
        using var terminal = new RecordingDisplay();
        var transport = new SynchronousTerminalOutputTransport(terminal);
        using var writer = new AnsiFrameWriter();
        writer.Write("frame");
        using var lease = writer.CreateLease();

        var result = await transport.TryWriteFrameAsync(lease);

        Assert.Equal(TerminalWriteStatus.Written, result.Status);
        Assert.Equal("frame", terminal.Output);
        Assert.Equal("frame", lease.Payload.ToString());
    }

    [Fact]
    public async Task SynchronousTransport_WritabilityWaitIsLevelTriggered()
    {
        using var terminal = new RecordingDisplay();
        var transport = new SynchronousTerminalOutputTransport(terminal);

        await transport.WaitUntilWritableAsync();
    }

    private sealed class RecordingDisplay : ITerminalDisplay
    {
        private readonly StringBuilder _output = new();

        public string Output => _output.ToString();

        public TerminalSize GetSize() => new(80, 24);

        public void Write(ReadOnlySpan<char> text) => _output.Append(text);

        public void Flush() { }

        public void HideCursor() { }

        public void ShowCursor() { }

        public void Dispose() { }
    }
}
