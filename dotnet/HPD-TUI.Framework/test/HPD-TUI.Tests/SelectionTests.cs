using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

namespace HPD.TUI.Tests;

public sealed class SelectionTests
{
    [Fact]
    public void Controller_MovesSelection()
    {
        var model = new SelectionModel<int>()
            .Add(1, "one")
            .Add(2, "two");
        var controller = new SelectionController<int>(model);

        controller.HandleInput(new KeyEvent(KeyCode.DownArrow));

        Assert.Equal(1, controller.SelectedIndex);
        Assert.Equal(2, controller.SelectedItem?.Value);
    }

    [Fact]
    public void Controller_SkipsDisabledItems()
    {
        var model = new SelectionModel<int>()
            .Add(new CollectionItem<int>("one", 1, "one"))
            .Add(new CollectionItem<int>("two", 2, "two", disabled: true))
            .Add(new CollectionItem<int>("three", 3, "three"));
        var controller = new SelectionController<int>(model);

        controller.HandleInput(new KeyEvent(KeyCode.DownArrow));

        Assert.Equal(2, controller.SelectedIndex);
        Assert.Equal(3, controller.SelectedItem?.Value);
    }

    [Fact]
    public void Controller_SubmitUpdatesCurrentValueAndRaisesCallback()
    {
        var model = new SelectionModel<string>().Add("a", "A");
        var controller = new SelectionController<string>(model);
        string? submitted = null;
        controller.Submitted = item => submitted = item.Value;

        controller.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.Equal("a", submitted);
        Assert.Equal("a", model.CurrentValue);
    }

    [Fact]
    public void View_RendersSelectedItem()
    {
        var model = new SelectionModel<string>()
            .Add("one", "One")
            .Add("two", "Two");
        var controller = new SelectionController<string>(model);
        controller.HandleInput(new KeyEvent(KeyCode.DownArrow));
        var view = new SelectionView<string>(model, controller);
        var context = new RenderContext(8, 2, Theme.Default);
        using var grid = new TerminalGrid(8, 2);
        var writer = new DisplayListBuilder(grid, grid.Width);

        view.Render(in context, 8, ref writer);

        Assert.Equal("  One   ", ReadLine(grid, 0));
        Assert.Equal("> Two   ", ReadLine(grid, 1));
    }

    [Fact]
    public void Controller_CharacterInputFiltersWhenEnabled()
    {
        var model = new SelectionModel<string> { AllowFilter = true }
            .Add("alpha", "Alpha")
            .Add("beta", "Beta");
        var controller = new SelectionController<string>(model);

        controller.HandleInput(new KeyEvent(KeyCode.Character, new Rune('b')));

        Assert.Equal("b", model.Query);
        Assert.Equal(1, controller.SelectedIndex);
        Assert.Equal("beta", controller.SelectedItem?.Value);
    }

    [Fact]
    public void View_RendersSearchLineWhenFilteringIsEnabled()
    {
        var model = new SelectionModel<string> { AllowFilter = true }
            .Add("alpha", "Alpha")
            .Add("beta", "Beta");
        model.Query = "be";
        var controller = new SelectionController<string>(model);
        var view = new SelectionView<string>(model, controller);

        var rendered = TuiCapture.RenderToString(view, 16, 3);

        Assert.Contains("Search: be", rendered);
        Assert.Contains("> Beta", rendered);
        Assert.DoesNotContain("Alpha", rendered);
    }

    private static string ReadLine(TerminalGrid grid, int y)
    {
        Span<char> buffer = stackalloc char[grid.Width];
        for (var x = 0; x < grid.Width; x++)
        {
            buffer[x] = (char)grid.GetLeadingRune(grid.GetCell(x, y)).Value;
        }

        return new string(buffer);
    }
}
