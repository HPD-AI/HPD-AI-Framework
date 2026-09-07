using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

namespace HPD.TUI.Tests;

public sealed class FocusAndDialogTests
{
    [Fact]
    public void FocusManager_PushAndPopRestoresPreviousFocus()
    {
        var focus = new FocusManager();
        var first = PromptView.Create();
        var second = PromptView.Create();

        focus.SetFocus(first);
        focus.PushFocus(second);

        Assert.False(first.IsFocused);
        Assert.True(second.IsFocused);

        Assert.True(focus.PopFocus());

        Assert.True(first.IsFocused);
        Assert.False(second.IsFocused);
        Assert.Same(first, focus.Focused);
    }

    [Fact]
    public void FocusManager_HandleInputRoutesToFocusedComponent()
    {
        var focus = new FocusManager();
        var prompt = PromptView.Create();

        focus.SetFocus(prompt);
        focus.HandleInput(new KeyEvent(KeyCode.Character, new Rune('x')));

        Assert.Equal("x", prompt.Model.Value);
    }

    [Fact]
    public void DialogHost_PushCapturesFocusAndEscapeCloses()
    {
        var focus = new FocusManager();
        var content = PromptView.Create();
        var dialogInput = PromptView.Create();
        var host = new DialogHost(content, focus);
        var closed = false;

        focus.SetFocus(content);
        host.Push(new Overlay(dialogInput, 0, 0, 8), dialogInput, () => closed = true);

        Assert.True(dialogInput.IsFocused);
        Assert.False(content.IsFocused);

        host.HandleInput(new KeyEvent(KeyCode.Escape));

        Assert.True(closed);
        Assert.False(dialogInput.IsFocused);
        Assert.True(content.IsFocused);
        Assert.False(host.HasOpenDialog);
    }

    [Fact]
    public void DialogHost_RendersTopLayerAfterContent()
    {
        var focus = new FocusManager();
        var host = new DialogHost(new Text("abc"), focus);
        host.Push(new Overlay(new Text("Z"), 1, 0, 2));
        var context = new RenderContext(8, 2, Theme.Default);
        using var grid = new TerminalGrid(8, 2);
        var writer = new DisplayListBuilder(grid, grid.Width);

        host.Render(in context, ref writer);

        Assert.Equal(new Rune('a'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.Equal(new Rune('Z'), grid.GetLeadingRune(grid.GetCell(1, 0)));
        Assert.Equal(new Rune('c'), grid.GetLeadingRune(grid.GetCell(2, 0)));
    }
}
