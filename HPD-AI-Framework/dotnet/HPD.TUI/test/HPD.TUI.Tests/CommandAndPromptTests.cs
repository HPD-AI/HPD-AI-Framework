using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

namespace HPD.TUI.Tests;

public sealed class CommandAndPromptTests
{
    [Fact]
    public void CommandRouter_ExecutesSlashCommandWithArguments()
    {
        string? captured = null;
        var model = new CommandModel()
            .Register(new CommandDescriptor("open", context => captured = context.Arguments.ToString())
            {
                SlashName = "open",
                Aliases = ["o"]
            });
        var router = new CommandRouter(model);

        var handled = router.TryExecute("/o readme.md".AsSpan());

        Assert.True(handled);
        Assert.Equal("readme.md", captured);
    }

    [Fact]
    public void CommandRouter_CompletesVisibleCommands()
    {
        var model = new CommandModel()
            .Register(new CommandDescriptor("open", _ => { }) { SlashName = "open" })
            .Register(new CommandDescriptor("hidden", _ => { }) { SlashName = "hidden", Hidden = true });
        var router = new CommandRouter(model);
        Span<CommandDescriptor> commands = new CommandDescriptor[4];

        var count = router.Complete("/o".AsSpan(), commands);

        Assert.Equal(1, count);
        Assert.Equal("open", commands[0].Name);
    }

    [Fact]
    public void PromptController_EditsSubmitsAndKeepsHistory()
    {
        ReadOnlyMemory<char> submitted = default;
        var model = new PromptModel();
        var controller = new PromptController(model) { Submitted = value => submitted = value };

        controller.HandleInput(new KeyEvent(KeyCode.Character, new Rune('h')));
        controller.HandleInput(new KeyEvent(KeyCode.Character, new Rune('i')));
        controller.HandleInput(new KeyEvent(KeyCode.Enter));
        controller.HandleInput(new KeyEvent(KeyCode.UpArrow));

        Assert.Equal("hi", submitted.ToString());
        Assert.Equal("hi", model.Value);
    }

    [Fact]
    public void AutocompleteController_AcceptsMatchingSuggestion()
    {
        var model = new PromptModel();
        model.SetText("/op");
        var autocomplete = new AutocompleteController()
            .Register(new StaticAutocompleteProvider('/', [new AutocompleteSuggestion("open", "/open")]));

        var active = autocomplete.Refresh(model);
        var accepted = autocomplete.Accept(model);

        Assert.True(active);
        Assert.True(accepted);
        Assert.Equal("/open", model.Value);
        Assert.Equal(5, model.Cursor);
    }

    [Fact]
    public void PromptView_RendersPlaceholderAndCursor()
    {
        var model = new PromptModel { Placeholder = "Ask anything" };
        var controller = new PromptController(model);
        var view = new PromptView(model, controller) { IsFocused = true };
        var context = new RenderContext(12, 1, Theme.Default);
        using var grid = new TerminalGrid(12, 1);
        var writer = new SegmentWriter(grid);

        view.Render(in context, 12, ref writer);

        Assert.Equal("Ask anything", ReadLine(grid, 0));
        Assert.True(grid.HasTerminalCursor);
        Assert.Equal(0, grid.TerminalCursorX);
    }

    [Fact]
    public void CommandPaletteView_ComposesSelectionAndExecutesCommand()
    {
        var executed = false;
        var model = new CommandModel()
            .Register(new CommandDescriptor("help", _ => executed = true) { SlashName = "help", Description = "Show help" });
        var router = new CommandRouter(model);
        var view = new CommandPaletteView(model, router);

        view.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.True(executed);
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

    private sealed class StaticAutocompleteProvider : IAutocompleteProvider
    {
        private readonly char _marker;
        private readonly IReadOnlyList<AutocompleteSuggestion> _suggestions;

        public StaticAutocompleteProvider(char marker, IReadOnlyList<AutocompleteSuggestion> suggestions)
        {
            _marker = marker;
            _suggestions = suggestions;
        }

        public bool CanProvide(AutocompleteTrigger trigger) => trigger.Marker == _marker;

        public IEnumerable<AutocompleteSuggestion> GetSuggestions(AutocompleteTrigger trigger)
        {
            foreach (var suggestion in _suggestions)
            {
                if (suggestion.Title.StartsWith(trigger.Query, StringComparison.OrdinalIgnoreCase))
                {
                    yield return suggestion;
                }
            }
        }
    }
}
