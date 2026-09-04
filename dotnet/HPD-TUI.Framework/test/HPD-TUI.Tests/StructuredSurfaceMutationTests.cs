using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class StructuredSurfaceMutationTests
{
    [Fact]
    public void SurfaceMutation_InvalidatesRetainedCommandWithoutComponentMutation()
    {
        const int width = 28;
        const int height = 10;
        var context = new RenderContext(width, height, Theme.Default);
        using var surface = new TuiSurface(2, 1);
        var surfaceContext = new RenderContext(2, 1, Theme.Default);
        surface.Capture(new Text("old"), in surfaceContext);
        var oldRevision = surface.CacheRevision;
        var component = new StructuredScreen(surface);
        var displayList = new RetainedDisplayList();
        using var previous = new ScreenBuffer(width, height);
        using var current = new ScreenBuffer(width, height);

        displayList.Prepare(component, in context, width);
        displayList.Replay(previous.Grid);
        surface.Capture(new Text("new"), in surfaceContext);
        Assert.NotEqual(oldRevision, surface.CacheRevision);

        Assert.False(displayList.Prepare(component, in context, width));
        Assert.False(displayList.RequiresFullRaster);
        Assert.True(displayList.DamagedRowCount > 0,
            $"built={displayList.CommandsBuilt}, reused={displayList.CommandsReused}, painted={displayList.ComponentsPainted}");
        current.CopyFrom(previous);
        current.ClearDamagedRows(displayList.DamagedRows);
        displayList.ReplayDamaged(current.Grid);

        using var expected = TuiCapture.RenderToGrid(component, width, height);
        Assert.Equal('n', (char)current.Grid.GetLeadingRune(current.Grid.GetCell(12, 5)).Value);
        AssertEquivalent(current.Grid, expected, 0);
    }

    [Fact]
    public void IncrementalRaster_EqualsFullRaster_ForRandomizedStructuredMutations()
    {
        const int width = 28;
        const int height = 10;
        var random = new Random(0x53545255);
        var surfaceContext = new RenderContext(5, 2, Theme.Default);
        using var surface = new TuiSurface(5, 2);
        surface.Capture(new Text("seed"), in surfaceContext);
        var root = new StructuredScreen(surface);
        var displayList = new RetainedDisplayList();
        using var current = new ScreenBuffer(width, height);
        using var previous = new ScreenBuffer(width, height);
        var context = new RenderContext(width, height, Theme.Default);
        var hasPrevious = false;

        for (var iteration = 0; iteration < 400; iteration++)
        {
            if (random.Next(7) == 0)
                surface.Capture(new Text($"s{random.Next(100):00}"), in surfaceContext);
            else
                root.Mutate(random);

            displayList.Prepare(root, in context, width);
            if (hasPrevious && !displayList.RequiresFullRaster)
            {
                current.CopyFrom(previous);
                current.ClearDamagedRows(displayList.DamagedRows);
                displayList.ReplayDamaged(current.Grid);
            }
            else
            {
                current.Clear();
                displayList.Replay(current.Grid);
            }

            using var expected = TuiCapture.RenderToGrid(root, width, height);
            AssertEquivalent(current.Grid, expected, iteration);
            previous.CopyFrom(current);
            hasPrevious = true;
        }
    }

    private static void AssertEquivalent(TerminalGrid actual, TerminalGrid expected, int iteration)
    {
        Assert.Equal(expected.HasTerminalCursor, actual.HasTerminalCursor);
        Assert.Equal(expected.TerminalCursorX, actual.TerminalCursorX);
        Assert.Equal(expected.TerminalCursorY, actual.TerminalCursorY);
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
            Assert.True(actual.CellEquals(expected, x, y), $"Mismatch at ({x},{y}) after mutation {iteration}.");
    }

    private sealed class StructuredScreen(TuiSurface surface) : Component
    {
        private static readonly string[] TextValues = ["alpha", "界x", "e\u0301!", "👩🏽‍💻", "omega"];
        private readonly TuiSurface _surface = surface;
        private string _text = TextValues[0];
        private char _fill = '.';
        private int _clipX;
        private int _clipWidth = 24;
        private int _cursorX;
        private int _cursorY;
        private Style _style = new(Color.Cyan, Color.Default);
        private TerminalHyperlink? _link;

        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);

        public void Mutate(Random random)
        {
            switch (random.Next(6))
            {
                case 0: _text = TextValues[random.Next(TextValues.Length)]; break;
                case 1: _fill = _fill == '.' ? ':' : '.'; break;
                case 2: _clipX = random.Next(0, 8); _clipWidth = random.Next(8, 25 - _clipX); break;
                case 3: _style = random.Next(2) == 0
                    ? new Style(Color.Cyan, Color.Default, TextAttributes.Bold)
                    : new Style(Color.Yellow, Color.Blue, TextAttributes.Underline); break;
                case 4:
                    TerminalHyperlinkPolicy.TryCreate($"https://example.test/{random.Next(4)}", out _link);
                    break;
                default: _cursorX = random.Next(28); _cursorY = random.Next(10); break;
            }
            InvalidatePaint();
        }

        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(28, 28, 10);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            output.Fill(new LayoutRect(0, 0, 28, 10), _fill, new Style(Color.Gray, Color.Default));
            output.PushClip(new LayoutRect(_clipX, 1, _clipWidth, 7));
            output.Border(new LayoutRect(1, 1, 25, 7), _style, '#');
            output.MoveTo(3, 3);
            output.Write(_text, _style, new TerminalRunMetadata(_link));
            output.ReplaySurface(_surface, 12, 5);
            output.PopClip();
            output.SetTerminalCursor(_cursorX, _cursorY);
        }
    }
}
