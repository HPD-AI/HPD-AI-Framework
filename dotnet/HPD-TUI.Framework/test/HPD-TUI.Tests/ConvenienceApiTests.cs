using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Views;

namespace HPD.TUI.Tests;

public sealed class ConvenienceApiTests
{
    [Fact]
    public void SelectionView_Create_WiresSubmitCallback()
    {
        var selected = 0;
        var view = SelectionView<int>.Create([1, 2], static value => value.ToString(), value => selected = value);

        view.HandleInput(new KeyEvent(KeyCode.DownArrow));
        view.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.Equal(2, selected);
        Assert.Equal(2, view.Model.CurrentValue);
    }

    [Fact]
    public void PromptView_Create_WiresModelControllerAndSubmit()
    {
        ReadOnlyMemory<char> submitted = default;
        var view = PromptView.Create("Ask", value => submitted = value);

        view.HandleInput(new KeyEvent(KeyCode.Character, new Rune('o')));
        view.HandleInput(new KeyEvent(KeyCode.Character, new Rune('k')));
        view.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.Equal("Ask", view.Model.Placeholder);
        Assert.Equal("ok", submitted.ToString());
    }

    [Fact]
    public void CommandPaletteView_Create_BuildsModelRouterAndExecutes()
    {
        var executed = false;
        var view = CommandPaletteView.Create([
            new CommandDescriptor("run", _ => executed = true) { SlashName = "run" }
        ]);

        view.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.True(executed);
        Assert.Single(view.Model.Commands);
        Assert.NotNull(view.Router);
    }

    [Fact]
    public void TableView_Create_ConfiguresRowsAndColumns()
    {
        var rows = new[] { new Row("alpha", "file") };
        var view = TableView<Row>.Create(rows, model => model.AddColumn("Name", static row => row.Name));

        Assert.Single(view.Model.Rows);
        Assert.Single(view.Model.Columns);
    }

    [Fact]
    public void TreeView_Create_AddsRoots()
    {
        var root = new TreeNode<string>("root", "root", "root");
        var view = TreeView<string>.Create([root]);

        Assert.Single(view.Model.Roots);
        Assert.True(view.Model.IsExpanded("root"));
    }

    [Fact]
    public void ActivityView_FactoriesExposeSemanticModel()
    {
        var progress = ActivityView.Progress("Loading", 0.5);
        var failed = ActivityView.Failed("Broken");

        Assert.Equal(0.5, progress.Model.Progress);
        Assert.Equal(ActivityState.Failed, failed.Model.State);
        Assert.Equal(ActivitySeverity.Error, failed.Model.Severity);
    }

    private readonly record struct Row(string Name, string Kind);
}
