using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;

namespace HPD.TUI.Tests;

public sealed class RandomizedCompositorEquivalenceTests
{
    [Fact]
    public void IncrementalRaster_EqualsForcedFullRaster_ForRandomizedMutations()
    {
        const int width = 32;
        const int height = 12;
        var random = new Random(0x485044);
        var root = new MutableScreen(width, height);
        var displayList = new RetainedDisplayList();
        using var current = new ScreenBuffer(width, height);
        using var previous = new ScreenBuffer(width, height);
        var context = new RenderContext(width, height, Theme.Default);
        var hasPrevious = false;

        for (var iteration = 0; iteration < 500; iteration++)
        {
            var changedX = random.Next(width);
            var changedY = random.Next(height);
            var changedValue = MutableScreen.Values[random.Next(MutableScreen.Values.Length)];
            root.Set(changedX, changedY, changedValue);
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
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    Assert.True(current.Grid.CellEquals(expected, x, y),
                        $"Mismatch at ({x},{y}) after mutation {iteration} ({changedX},{changedY})='{changedValue}'; actual='{current.Grid.GetGrapheme(current.Grid.GetCell(x, y)).ToString()}', expected='{expected.GetGrapheme(expected.GetCell(x, y)).ToString()}'.");

            previous.CopyFrom(current);
            hasPrevious = true;
        }
    }

    private sealed class MutableScreen : Component
    {
        internal static readonly string[] Values = ["a", "界", "e\u0301", "👩🏽‍💻", "z"];
        private readonly int _width;
        private readonly int _height;
        private readonly string[,] _cells;

        public MutableScreen(int width, int height)
        {
            _width = width;
            _height = height;
            _cells = new string[width, height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++) _cells[x, y] = " ";
        }

        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);

        public void Set(int x, int y, string value)
        {
            if (_cells[x, y] == value) return;
            _cells[x, y] = value;
            InvalidatePaint();
        }

        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) =>
            new(_width, _width, _height);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            for (var y = 0; y < _height; y++)
                for (var x = 0; x < _width; x++)
                {
                    output.MoveTo(x, y);
                    output.Write(_cells[x, y], context.Theme.Text);
                }
        }
    }
}
