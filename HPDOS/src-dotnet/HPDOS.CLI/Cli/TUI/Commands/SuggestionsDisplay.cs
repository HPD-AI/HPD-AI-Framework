using Spectre.Console;
using Spectre.Console.Rendering;

namespace HPDOS.Shell.Cli.TUI.Commands;

/// <summary>
/// Displays command suggestions with fuzzy match highlighting.
/// </summary>
public class SuggestionsDisplay
{
    public List<CommandSuggestion> Suggestions { get; set; } = new();
    public int ActiveIndex { get; set; } = 0;
    public int MaxVisible { get; set; } = 8;

    public IRenderable Render()
    {
        if (Suggestions.Count == 0) return new Text("");

        var rows = new List<IRenderable>();
        int scrollOffset = Math.Max(0, ActiveIndex - MaxVisible + 1);
        if (ActiveIndex < scrollOffset) scrollOffset = ActiveIndex;
        int endIndex = Math.Min(scrollOffset + MaxVisible, Suggestions.Count);

        if (scrollOffset > 0)
            rows.Add(new Markup($"[grey50]▲ ({scrollOffset} more above)[/]"));

        for (int i = 0; i < endIndex - scrollOffset; i++)
        {
            var originalIndex = scrollOffset + i;
            rows.Add(RenderSuggestion(Suggestions[originalIndex], originalIndex == ActiveIndex));
        }

        if (endIndex < Suggestions.Count)
            rows.Add(new Markup($"[grey50]▼ ({Suggestions.Count - endIndex} more below)[/]"));

        if (Suggestions.Count > MaxVisible)
            rows.Add(new Markup($"[grey50]({ActiveIndex + 1}/{Suggestions.Count})[/]"));

        return new Panel(new Rows(rows))
            .Header("[yellow]Commands[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow)
            .Padding(1, 0);
    }

    private static IRenderable RenderSuggestion(CommandSuggestion suggestion, bool isActive)
    {
        var color = isActive ? "cyan1" : "grey";
        var bgMark = isActive ? "[on grey23]" : "";
        var bgEnd = isActive ? "[/]" : "";
        var prefix = isActive ? "●" : "○";

        var highlighted = HighlightMatches(suggestion.DisplayName, suggestion.MatchedIndices, color);
        var commandPart = $"[{color}]{prefix}[/] [{color}]/{highlighted}[/]";

        if (!string.IsNullOrEmpty(suggestion.Command.Description))
        {
            var desc = suggestion.Command.Description;
            if (desc.Length > 50) desc = desc[..47] + "...";
            commandPart += $" [grey50]- {Markup.Escape(desc)}[/]";
        }

        return new Markup($"{bgMark}{commandPart}{bgEnd}");
    }

    private static string HighlightMatches(string text, List<int> matchedIndices, string baseColor)
    {
        if (matchedIndices.Count == 0) return Markup.Escape(text);

        var result = "";
        for (int i = 0; i < text.Length; i++)
        {
            var escaped = Markup.Escape(text[i].ToString());
            result += matchedIndices.Contains(i)
                ? $"[bold yellow]{escaped}[/]"
                : $"[{baseColor}]{escaped}[/]";
        }
        return result;
    }
}

/// <summary>
/// Manages command suggestion state and keyboard navigation.
/// </summary>
public class SuggestionManager
{
    private readonly CommandRegistry _registry;
    private List<CommandSuggestion> _currentSuggestions = new();
    private int _activeIndex = 0;

    public bool HasSuggestions => _currentSuggestions.Count > 0;
    public int ActiveIndex => _activeIndex;
    public List<CommandSuggestion> Suggestions => _currentSuggestions;

    public SuggestionManager(CommandRegistry registry) => _registry = registry;

    public void UpdateQuery(string query)
    {
        query = query.TrimStart('/');
        _currentSuggestions = _registry.FindSuggestions(query, maxResults: 20);
        _activeIndex = _currentSuggestions.Count > 0 ? 0 : -1;
    }

    public void Clear()
    {
        _currentSuggestions.Clear();
        _activeIndex = -1;
    }

    public void NavigateUp()
    {
        if (_currentSuggestions.Count == 0) return;
        _activeIndex--;
        if (_activeIndex < 0) _activeIndex = _currentSuggestions.Count - 1;
    }

    public void NavigateDown()
    {
        if (_currentSuggestions.Count == 0) return;
        _activeIndex++;
        if (_activeIndex >= _currentSuggestions.Count) _activeIndex = 0;
    }

    public CommandSuggestion? GetSelected() =>
        (_activeIndex < 0 || _activeIndex >= _currentSuggestions.Count) ? null : _currentSuggestions[_activeIndex];

    public string? GetCompletedText() => GetSelected() is { } s ? "/" + s.DisplayName : null;

    public IRenderable Render()
    {
        if (_currentSuggestions.Count == 0) return new Spectre.Console.Text("");
        return new SuggestionsDisplay { Suggestions = _currentSuggestions, ActiveIndex = _activeIndex }.Render();
    }
}
