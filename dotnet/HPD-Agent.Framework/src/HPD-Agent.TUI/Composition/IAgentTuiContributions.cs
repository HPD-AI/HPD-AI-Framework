using HPD.TUI.Controllers;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public interface IAgentTuiShellComponent
{
    IComponent Create(AgentTuiShellContext context);
}

public interface IAgentTuiStatusItem
{
    IComponent Create(AgentTuiStatusContext context);
}

public interface IAgentTuiWidget
{
    IComponent Create(AgentTuiWidgetContext context);
}

public interface IAgentTuiAutocompleteProvider
{
    bool CanProvide(AgentTuiAutocompleteContext context);

    ValueTask GetSuggestionsAsync(
        AgentTuiAutocompleteContext context,
        IAutocompleteSuggestionSink suggestions,
        CancellationToken cancellationToken = default);

    ValueTask<AutocompleteEdit> ApplyCompletionAsync(
        AutocompleteCompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var replacement = context.Replacement;
        return ValueTask.FromResult(new AutocompleteEdit(
            replacement.Start,
            replacement.Length,
            context.Suggestion.InsertText,
            replacement.Start + context.Suggestion.InsertText.Length));
    }
}
