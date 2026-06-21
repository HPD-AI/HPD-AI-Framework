using HPD.TUI.Core;
using HPD.TUI.Flows;
using HPD.TUI.Models;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

namespace HPD.TUI.Tests;

public sealed class ActivityTests
{
    [Fact]
    public void Render_UsesElapsedTimeForSpinnerFrame()
    {
        var model = new ActivityModel("Work");
        var view = new ActivityView(model);
        var context = new RenderContext(10, 1, Theme.Default, elapsed: TimeSpan.FromMilliseconds(80));
        using var grid = new TerminalGrid(10, 1);
        var writer = new SegmentWriter(grid);

        view.Render(in context, 10, ref writer);

        Assert.Equal(new Rune('⠙'), grid.GetCell(0, 0).Rune);
        Assert.Equal("⠙ Work    ", ReadLine(grid, 0));
    }

    [Fact]
    public void Render_DisabledAnimationUsesFallback()
    {
        var model = new ActivityModel("Work");
        var view = new ActivityView(model) { AnimationsEnabled = false };
        var context = new RenderContext(10, 1, Theme.Default, elapsed: TimeSpan.FromMilliseconds(80));
        using var grid = new TerminalGrid(10, 1);
        var writer = new SegmentWriter(grid);

        view.Render(in context, 10, ref writer);

        Assert.Equal("⋯ Work    ", ReadLine(grid, 0));
    }

    [Fact]
    public void Render_ProgressWritesPercent()
    {
        var model = new ActivityModel("Index") { Progress = 0.42 };
        var view = new ActivityView(model);
        var context = new RenderContext(16, 1, Theme.Default);
        using var grid = new TerminalGrid(16, 1);
        var writer = new SegmentWriter(grid);

        view.Render(in context, 16, ref writer);

        Assert.Equal("● Index 42%     ", ReadLine(grid, 0));
    }

    [Fact]
    public void Render_CompletedUsesCompletedIndicator()
    {
        var model = new ActivityModel("Done") { State = ActivityState.Completed };
        var view = new ActivityView(model);
        var context = new RenderContext(10, 1, Theme.Default);
        using var grid = new TerminalGrid(10, 1);
        var writer = new SegmentWriter(grid);

        view.Render(in context, 10, ref writer);

        Assert.Equal("● Done    ", ReadLine(grid, 0));
    }

    [Fact]
    public void ActivityGroupView_RendersDetailedActivities()
    {
        var group = new ActivityGroupModel { Title = "Tasks" }
            .Add(new ActivityModel("Index") { Progress = 0.5 })
            .Add(new ActivityModel("Build") { State = ActivityState.Completed });
        var view = new ActivityGroupView(group) { AnimationsEnabled = false };

        var lines = TuiCapture.RenderToLines(view, 20, 3);

        Assert.Equal("Tasks               ", lines[0]);
        Assert.Equal("● Index 50%         ", lines[1]);
        Assert.Equal("● Build             ", lines[2]);
    }

    [Fact]
    public void ActivityGroupView_RendersCompactSummary()
    {
        var group = new ActivityGroupModel()
            .Add(new ActivityModel("Pending") { State = ActivityState.Pending })
            .Add(new ActivityModel("Running"))
            .Add(new ActivityModel("Done") { State = ActivityState.Completed })
            .Add(new ActivityModel("Failed") { State = ActivityState.Failed });
        var view = new ActivityGroupView(group) { Mode = ActivityGroupDisplayMode.Compact };

        var lines = TuiCapture.RenderToLines(view, 48, 1);

        Assert.StartsWith("running 1  done 1  failed 1  pending 1", lines[0]);
        Assert.Equal(48, lines[0].Length);
    }

    [Fact]
    public void ActivityGroupModel_HidesAndClearsCompleted()
    {
        var group = new ActivityGroupModel { HideCompleted = true }
            .Add(new ActivityModel("Done") { State = ActivityState.Completed })
            .Add(new ActivityModel("Run"));

        Assert.Single(group.GetVisibleActivities());

        group.HideCompleted = false;
        group.AutoClearCompleted = true;

        Assert.Single(group.GetVisibleActivities());
        Assert.Equal("Run", group.Activities[0].Label);
    }

    [Fact]
    public void ActivityScope_UpdatesActivityState()
    {
        var group = new ActivityGroupModel();

        using (var scope = ActivityScope.Start(group, "Work"))
        {
            scope.SetProgress(0.25);
            Assert.Equal(ActivityState.Running, scope.Activity.State);
            Assert.Equal(0.25, scope.Activity.Progress);
        }

        Assert.Equal(ActivityState.Completed, group.Activities[0].State);
        Assert.Equal(1, group.Activities[0].Progress);
    }

    private static string ReadLine(TerminalGrid grid, int y)
    {
        Span<char> buffer = stackalloc char[grid.Width];
        for (var x = 0; x < grid.Width; x++)
        {
            buffer[x] = (char)grid.GetCell(x, y).Rune.Value;
        }

        return new string(buffer);
    }
}
