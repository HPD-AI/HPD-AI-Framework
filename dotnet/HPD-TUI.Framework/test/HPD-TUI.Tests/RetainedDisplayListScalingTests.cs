using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class RetainedDisplayListScalingTests
{
    [Fact]
    public void WarmingOneCellMutation_PrepareAndDamageReplayAllocateNothing()
    {
        var component = new MutableCharacter();
        var context = new RenderContext(8, 2, Theme.Default);
        using var list = new RetainedDisplayList();
        using var grid = new TerminalGrid(8, 2);
        list.Prepare(component, in context, 8);
        list.Replay(grid);
        component.Change();
        list.Prepare(component, in context, 8);
        list.ReplayDamaged(grid);
        component.Change();

        var before = GC.GetAllocatedBytesForCurrentThread();
        list.Prepare(component, in context, 8);
        list.ReplayDamaged(grid);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void WarmingLargeTree_NoOpReadsOnlyRootStamp()
    {
        var leaves = Enumerable.Range(0, 2_000).Select(_ => new Probe()).ToArray();
        var root = new Row(leaves);
        var context = new RenderContext(4_000, 2, Theme.Default);
        using var list = new RetainedDisplayList();
        list.Prepare(root, in context, context.Width);
        foreach (var leaf in leaves) leaf.DependencyReads = 0;

        Assert.True(list.Prepare(root, in context, context.Width));
        Assert.Equal(0, leaves.Sum(static leaf => leaf.DependencyReads));
    }

    [Fact]
    public void OneDamagedRow_ReplaysOnlyIntersectingCommands()
    {
        var component = new ManyRows(2_000);
        var context = new RenderContext(10, 2_000, Theme.Default);
        using var list = new RetainedDisplayList();
        using var grid = new TerminalGrid(10, 2_000);
        list.Prepare(component, in context, context.Width);
        list.Replay(grid);
        component.Change(1_337);
        list.Prepare(component, in context, context.Width);
        grid.Clear();

        list.ReplayDamaged(grid);

        Assert.InRange(list.LastReplayCandidateCount, 1, 2);
    }

    [Fact]
    public void SurfaceGenerationLease_PreservesCapturedPixelsAcrossReplacement()
    {
        var surfaceContext = new RenderContext(3, 1, Theme.Default);
        using var surface = new TuiSurface(3, 1);
        surface.Capture(new Text("old"), in surfaceContext);
        using var list = new RetainedDisplayList();
        var root = new SurfaceComponent(surface);
        list.Prepare(root, in surfaceContext, 3);
        surface.Capture(new Text("new"), in surfaceContext);
        using var grid = new TerminalGrid(3, 1);

        list.Replay(grid);

        Assert.Equal("old", new string(Enumerable.Range(0, 3)
            .Select(x => (char)grid.GetLeadingRune(grid.GetCell(x, 0)).Value).ToArray()));
    }

    private sealed class Probe : Component
    {
        public int DependencyReads;
        public override ComponentDependencies Dependencies { get { DependencyReads++; return new(RenderContextFields.None, RenderContextFields.None); } }
        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(1, 1, 1);
        public override void Render(in RenderContext context, ref DisplayListBuilder output) => output.Write('x', Style.Default);
    }

    private sealed class MutableCharacter : Component
    {
        private char _value = 'a';
        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);
        public void Change() { _value++; InvalidatePaint(); }
        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(1, 1, 1);
        public override void Render(in RenderContext context, ref DisplayListBuilder output) => output.Write(_value, Style.Default);
    }

    private sealed class Row : Component
    {
        public Row(IEnumerable<IComponent> children) => AdoptChildren(children);
        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);
        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(constraints.MaxWidth, constraints.MaxWidth, 1);
        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        { foreach (var child in OwnedChildren) output.Render(child, in context, 1); }
    }

    private sealed class ManyRows(int count) : Component
    {
        private int _changed = -1;
        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);
        public void Change(int row) { _changed = row; InvalidatePaint(); }
        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(1, 1, count);
        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            for (var row = 0; row < count; row++) { output.MoveTo(0, row); output.Write(row == _changed ? 'y' : 'x', Style.Default); }
        }
    }

    private sealed class SurfaceComponent(TuiSurface surface) : Component
    {
        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);
        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(3, 3, 1);
        public override void Render(in RenderContext context, ref DisplayListBuilder output) => output.ReplaySurface(surface, 0, 0);
    }
}
