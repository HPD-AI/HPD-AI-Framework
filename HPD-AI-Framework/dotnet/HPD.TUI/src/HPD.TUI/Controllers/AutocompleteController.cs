using HPD.TUI.Models;

namespace HPD.TUI.Controllers;

public sealed class AutocompleteController
{
    private readonly List<IAutocompleteProvider> _providers = [];
    private readonly List<AutocompleteSuggestion> _suggestions = [];
    private AutocompleteTrigger? _activeTrigger;
    private int _selectedIndex;

    public IReadOnlyList<AutocompleteSuggestion> Suggestions => _suggestions;

    public int SelectedIndex => _selectedIndex;

    public AutocompleteSuggestion? SelectedSuggestion =>
        _suggestions.Count == 0 ? null : _suggestions[Math.Clamp(_selectedIndex, 0, _suggestions.Count - 1)];

    public AutocompleteController Register(IAutocompleteProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Add(provider);
        return this;
    }

    public bool Refresh(PromptModel prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        _suggestions.Clear();
        _activeTrigger = FindTrigger(prompt);
        _selectedIndex = 0;

        if (_activeTrigger is null)
        {
            return false;
        }

        foreach (var provider in _providers)
        {
            if (!provider.CanProvide(_activeTrigger.Value))
            {
                continue;
            }

            foreach (var suggestion in provider.GetSuggestions(_activeTrigger.Value))
            {
                _suggestions.Add(suggestion);
            }
        }

        return _suggestions.Count > 0;
    }

    public bool Move(int delta)
    {
        if (_suggestions.Count == 0 || delta == 0)
        {
            return false;
        }

        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _suggestions.Count - 1);
        return true;
    }

    public bool Accept(PromptModel prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        if (_activeTrigger is null || SelectedSuggestion is not { } suggestion)
        {
            return false;
        }

        var trigger = _activeTrigger.Value;
        prompt.Text.Remove(trigger.Start, trigger.Length);
        prompt.Text.Insert(trigger.Start, suggestion.InsertText);
        prompt.Cursor = trigger.Start + suggestion.InsertText.Length;
        Refresh(prompt);
        return true;
    }

    private static AutocompleteTrigger? FindTrigger(PromptModel prompt)
    {
        if (prompt.Cursor <= 0 || prompt.Cursor > prompt.Text.Length)
        {
            return null;
        }

        var index = prompt.Cursor - 1;
        while (index >= 0 && !char.IsWhiteSpace(prompt.Text[index]))
        {
            if (prompt.Text[index] is '/' or '@' or '#')
            {
                var queryStart = index + 1;
                var queryLength = prompt.Cursor - queryStart;
                return new AutocompleteTrigger(prompt.Text[index], index, prompt.Cursor - index, prompt.Text.ToString(queryStart, queryLength));
            }

            index--;
        }

        return null;
    }
}

public interface IAutocompleteProvider
{
    bool CanProvide(AutocompleteTrigger trigger);

    IEnumerable<AutocompleteSuggestion> GetSuggestions(AutocompleteTrigger trigger);
}

public readonly record struct AutocompleteTrigger(char Marker, int Start, int Length, string Query);

public readonly record struct AutocompleteSuggestion(string Title, string InsertText, string? Description = null);
