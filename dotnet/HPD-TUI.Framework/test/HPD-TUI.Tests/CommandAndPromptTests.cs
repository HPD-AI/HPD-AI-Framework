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
    public void PromptController_EnterAcceptsVisibleAutocompleteSuggestion()
    {
        var model = new PromptModel();
        var controller = new PromptController(model)
        {
            Autocomplete = new AutocompleteController()
                .Register(new StaticAutocompleteProvider('/', [new AutocompleteSuggestion("open", "/open ")]))
        };

        controller.HandleInput(new KeyEvent(KeyCode.Character, new Rune('/')));
        controller.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.Equal("/open ", model.Value);
    }

    [Fact]
    public void PromptController_EscapeHidesVisibleAutocompleteSuggestion()
    {
        var model = new PromptModel();
        var controller = new PromptController(model)
        {
            Autocomplete = new AutocompleteController()
                .Register(new StaticAutocompleteProvider('/', [new AutocompleteSuggestion("open", "/open ")]))
        };

        controller.HandleInput(new KeyEvent(KeyCode.Character, new Rune('/')));
        Assert.Equal(1, controller.Autocomplete.SuggestionCount);

        var handled = controller.HandleInput(new KeyEvent(KeyCode.Escape));

        Assert.True(handled);
        Assert.Equal(0, controller.Autocomplete.SuggestionCount);
        Assert.Equal("/", model.Value);
    }

    [Fact]
    public void PromptController_EnterSubmitsAcceptedSuggestionWhenRequested()
    {
        ReadOnlyMemory<char> submitted = default;
        var model = new PromptModel();
        var controller = new PromptController(model)
        {
            Submitted = value => submitted = value,
            Autocomplete = new AutocompleteController()
                .Register(new StaticAutocompleteProvider(
                    '/',
                    [new AutocompleteSuggestion("open", "/open", SubmitOnAccept: true)]))
        };

        controller.HandleInput(new KeyEvent(KeyCode.Character, new Rune('/')));
        controller.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.Equal("/open", submitted.ToString());
        Assert.Equal("", model.Value);
    }

    [Fact]
    public void PromptController_TabDoesNotSubmitAcceptedSuggestion()
    {
        ReadOnlyMemory<char> submitted = default;
        var model = new PromptModel();
        var controller = new PromptController(model)
        {
            Submitted = value => submitted = value,
            Autocomplete = new AutocompleteController()
                .Register(new StaticAutocompleteProvider(
                    '/',
                    [new AutocompleteSuggestion("open", "/open", SubmitOnAccept: true)]))
        };

        controller.HandleInput(new KeyEvent(KeyCode.Character, new Rune('/')));
        controller.HandleInput(new KeyEvent(KeyCode.Tab));

        Assert.Equal("", submitted.ToString());
        Assert.Equal("/open", model.Value);
    }

    [Fact]
    public void PromptController_PasteDisplaysSummaryAndSubmitsOriginalText()
    {
        ReadOnlyMemory<char> submitted = default;
        var model = new PromptModel();
        var controller = new PromptController(model) { Submitted = value => submitted = value };

        controller.HandleInput(new KeyEvent(KeyCode.Paste, Text: "alpha beta\ngamma"));

        Assert.Equal("(pasted 3 words)", model.Value);
        Assert.Equal("alpha beta\ngamma", model.SubmittedValue);

        controller.HandleInput(new KeyEvent(KeyCode.Enter));

        Assert.Equal("alpha beta\ngamma", submitted.ToString());
        Assert.Equal("", model.Value);
    }

    [Fact]
    public void PromptController_EmptyPasteDoesNotCorruptSlashCommandDraft()
    {
        var model = new PromptModel();
        var controller = new PromptController(model);

        controller.HandleInput(new KeyEvent(KeyCode.Character, new Rune('/')));
        controller.HandleInput(new KeyEvent(KeyCode.Paste, Text: ""));

        Assert.Equal("/", model.Value);
        Assert.Equal("/", model.SubmittedValue);
    }

    [Fact]
    public void PromptController_WhitespacePasteDoesNotCorruptSlashCommandDraft()
    {
        var model = new PromptModel();
        var controller = new PromptController(model);

        controller.HandleInput(new KeyEvent(KeyCode.Character, new Rune('/')));
        controller.HandleInput(new KeyEvent(KeyCode.Paste, Text: "\n"));

        Assert.Equal("/", model.Value);
        Assert.Equal("/", model.SubmittedValue);
    }

    [Fact]
    public void PromptController_PasteMarkerEditsAsAtomicPart()
    {
        var model = new PromptModel();
        var controller = new PromptController(model);

        controller.HandleInput(new KeyEvent(KeyCode.Paste, Text: "alpha beta"));
        controller.HandleInput(new KeyEvent(KeyCode.LeftArrow));
        controller.HandleInput(new KeyEvent(KeyCode.RightArrow));
        controller.HandleInput(new KeyEvent(KeyCode.Backspace));

        Assert.Equal("", model.Value);
        Assert.Equal("", model.SubmittedValue);
    }

    [Fact]
    public void SlashCommandAutocompleteProvider_CompletesCommandName()
    {
        var model = new PromptModel();
        model.SetText("/he");
        var autocomplete = new AutocompleteController()
            .Register(new SlashCommandAutocompleteProvider(
            [
                new TuiSlashCommand("help", "Show help")
            ]));

        var active = autocomplete.Refresh(model);
        Assert.True(autocomplete.SelectedSuggestion?.SubmitOnAccept);
        var accepted = autocomplete.Accept(model);

        Assert.True(active);
        Assert.True(accepted);
        Assert.Equal("/help", model.Value);
    }

    [Fact]
    public void SlashCommandAutocompleteProvider_CompletesCommandArguments()
    {
        var model = new PromptModel();
        model.SetText("/model dee");
        var autocomplete = new AutocompleteController()
            .Register(new SlashCommandAutocompleteProvider(
            [
                new TuiSlashCommand(
                    "model",
                    "Select model",
                    CompleteArgumentsAsync: static (context, _) =>
                    {
                        context.Suggestions.Add(new AutocompleteSuggestion(
                            "deepseek-chat",
                            "deepseek-chat",
                            ReplacementStart: context.Request.Cursor - context.ArgumentLength,
                            ReplacementLength: context.ArgumentLength));
                        return ValueTask.CompletedTask;
                    })
            ]));

        var active = autocomplete.Refresh(model);
        var accepted = autocomplete.Accept(model);

        Assert.True(active);
        Assert.True(accepted);
        Assert.Equal("/model deepseek-chat", model.Value);
    }

    [Fact]
    public void PromptView_RendersPlaceholderAndCursor()
    {
        var model = new PromptModel { Placeholder = "Ask anything" };
        var controller = new PromptController(model);
        var view = new PromptView(model, controller) { IsFocused = true };
        var context = new RenderContext(12, 1, Theme.Default);
        using var grid = new TerminalGrid(12, 1);
        var writer = new DisplayListBuilder(grid, grid.Width);

        view.Render(in context, 12, ref writer);

        Assert.Equal("Ask anything", ReadLine(grid, 0));
        Assert.True(grid.HasTerminalCursor);
        Assert.Equal(0, grid.TerminalCursorX);
    }

    [Fact]
    public void PromptView_RendersVisualCursorWhenEnabled()
    {
        var model = new PromptModel { Placeholder = "Ask anything", ShowVisualCursor = true };
        var controller = new PromptController(model);
        var view = new PromptView(model, controller) { IsFocused = true };
        var context = new RenderContext(12, 1, Theme.Default);
        using var grid = new TerminalGrid(12, 1);
        var writer = new DisplayListBuilder(grid, grid.Width);

        view.Render(in context, 12, ref writer);

        Assert.Equal("|Ask anythin", ReadLine(grid, 0));
    }

    [Fact]
    public void PromptView_RendersVisualCursorAtTextCursor()
    {
        var model = new PromptModel { ShowVisualCursor = true };
        model.SetText("hello");
        model.Cursor = 2;
        var controller = new PromptController(model);
        var view = new PromptView(model, controller) { IsFocused = true };
        var context = new RenderContext(8, 1, Theme.Default);
        using var grid = new TerminalGrid(8, 1);
        var writer = new DisplayListBuilder(grid, grid.Width);

        view.Render(in context, 8, ref writer);

        Assert.Equal("he|llo  ", ReadLine(grid, 0));
    }

    [Fact]
    public void PromptView_RendersPromptStylesAcrossInputRow()
    {
        var background = new Color(10, 20, 30);
        var foreground = new Color(220, 230, 240);
        var model = new PromptModel
        {
            Placeholder = "Ask",
            Prefix = "> ",
            ExpandToWidth = true,
            FillStyle = new Style(Color.Default, background),
            PrefixStyle = new Style(Color.Cyan, background),
            PlaceholderStyle = new Style(foreground, background),
        };
        var controller = new PromptController(model);
        var view = new PromptView(model, controller) { IsFocused = true };
        var context = new RenderContext(8, 1, Theme.Default);
        using var grid = new TerminalGrid(8, 1);
        var writer = new DisplayListBuilder(grid, grid.Width);

        view.Render(in context, 8, ref writer);

        Assert.Equal("> Ask   ", ReadLine(grid, 0));
        Assert.Equal(background, grid.GetCell(0, 0).Style.Background);
        Assert.Equal(Color.Cyan, grid.GetCell(0, 0).Style.Foreground);
        Assert.Equal(background, grid.GetCell(2, 0).Style.Background);
        Assert.Equal(foreground, grid.GetCell(2, 0).Style.Foreground);
        Assert.Equal(background, grid.GetCell(7, 0).Style.Background);
        Assert.True(grid.HasTerminalCursor);
        Assert.Equal(2, grid.TerminalCursorX);
    }

    [Fact]
    public void PromptView_RendersVerticalPaddingWithFillStyle()
    {
        var background = new Color(10, 20, 30);
        var fill = new Style(Color.Default, background);
        var model = new PromptModel
        {
            Placeholder = "Ask",
            Prefix = "> ",
            ExpandToWidth = true,
            FillStyle = fill,
            PrefixStyle = new Style(Color.Cyan, background),
            PlaceholderStyle = new Style(Color.Gray, background),
            PaddingTop = 1,
            PaddingBottom = 1
        };
        var controller = new PromptController(model);
        var view = new PromptView(model, controller) { IsFocused = true };
        var context = new RenderContext(8, 3, Theme.Default);
        using var grid = new TerminalGrid(8, 3);
        var writer = new DisplayListBuilder(grid, grid.Width);

        var measurement = view.Measure(in context, 8);
        view.Render(in context, 8, ref writer);

        Assert.Equal(3, measurement.Height);
        Assert.Equal("        ", ReadLine(grid, 0));
        Assert.Equal("> Ask   ", ReadLine(grid, 1));
        Assert.Equal("        ", ReadLine(grid, 2));
        Assert.Equal(background, grid.GetCell(0, 0).Style.Background);
        Assert.Equal(background, grid.GetCell(7, 2).Style.Background);
        Assert.True(grid.HasTerminalCursor);
        Assert.Equal(2, grid.TerminalCursorX);
        Assert.Equal(1, grid.TerminalCursorY);
    }

    [Fact]
    public void PromptView_WrapsLongInputAtBoundary()
    {
        var model = new PromptModel();
        model.SetText("abcdefghijkl");
        var controller = new PromptController(model);
        var view = new PromptView(model, controller) { IsFocused = true };
        var context = new RenderContext(5, 3, Theme.Default);
        using var grid = new TerminalGrid(5, 3);
        var writer = new DisplayListBuilder(grid, grid.Width);

        var measurement = view.Measure(in context, 5);
        view.Render(in context, 5, ref writer);

        Assert.Equal(3, measurement.Height);
        Assert.Equal("abcde", ReadLine(grid, 0));
        Assert.Equal("fghij", ReadLine(grid, 1));
        Assert.Equal("kl   ", ReadLine(grid, 2));
        Assert.Equal(2, grid.TerminalCursorX);
        Assert.Equal(2, grid.TerminalCursorY);
    }

    [Fact]
    public void PromptView_LimitsAutocompleteRowsAndKeepsSelectionVisible()
    {
        var suggestions = Enumerable.Range(0, 12)
            .Select(index => new AutocompleteSuggestion($"item-{index}", $"/item-{index}"))
            .ToArray();
        var autocomplete = new AutocompleteController()
            .Register(new StaticAutocompleteProvider('/', suggestions));
        var model = new PromptModel();
        model.SetText("/");
        autocomplete.Refresh(model);
        autocomplete.Move(10);
        var controller = new PromptController(model) { Autocomplete = autocomplete };
        var view = new PromptView(model, controller) { MaximumSuggestionRows = 4 };
        var context = new RenderContext(20, 5, Theme.Default);
        using var grid = new TerminalGrid(20, 5);
        var writer = new DisplayListBuilder(grid, grid.Width);

        var measurement = view.Measure(in context, 20);
        view.Render(in context, 20, ref writer);

        Assert.Equal(5, measurement.Height);
        Assert.Contains("item-7", ReadLine(grid, 1));
        Assert.Contains("> item-10", ReadLine(grid, 4));
        Assert.DoesNotContain("item-6", string.Join('\n', Enumerable.Range(0, 5).Select(row => ReadLine(grid, row))));
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
            buffer[x] = (char)grid.GetLeadingRune(grid.GetCell(x, y)).Value;
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

        public ValueTask GetSuggestionsAsync(
            AutocompleteRequest request,
            IAutocompleteSuggestionSink suggestions,
            CancellationToken cancellationToken = default)
        {
            if (request.Trigger is not { } trigger || trigger.Marker != _marker)
            {
                return ValueTask.CompletedTask;
            }

            foreach (var suggestion in _suggestions)
            {
                if (request.SliceIsPrefixOf(trigger.QueryStart, trigger.QueryLength, suggestion.Title, StringComparison.OrdinalIgnoreCase))
                {
                    suggestions.Add(suggestion);
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
