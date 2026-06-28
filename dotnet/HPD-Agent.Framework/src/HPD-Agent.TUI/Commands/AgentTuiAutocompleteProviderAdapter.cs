using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.TUI.Controllers;

namespace HPD.Agent.TUI.Commands;

internal sealed class AgentTuiAutocompleteProviderAdapter : IAutocompleteProvider
{
    private readonly Func<HpdAgentTuiRegistry> _getRegistry;
    private readonly Func<AgentTuiSessionState?> _getState;

    public AgentTuiAutocompleteProviderAdapter(
        Func<HpdAgentTuiRegistry> getRegistry,
        Func<AgentTuiSessionState?> getState)
    {
        _getRegistry = getRegistry ?? throw new ArgumentNullException(nameof(getRegistry));
        _getState = getState ?? throw new ArgumentNullException(nameof(getState));
    }

    public async ValueTask GetSuggestionsAsync(
        AutocompleteRequest request,
        IAutocompleteSuggestionSink suggestions,
        CancellationToken cancellationToken = default)
    {
        var context = CreateContext(request);
        foreach (var provider in _getRegistry().AutocompleteProviders)
        {
            if (!provider.Value.CanProvide(context))
            {
                continue;
            }

            await provider.Value.GetSuggestionsAsync(context, suggestions, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<AutocompleteEdit> ApplyCompletionAsync(
        AutocompleteCompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var agentContext = CreateContext(context.Request);
        foreach (var provider in _getRegistry().AutocompleteProviders)
        {
            if (!provider.Value.CanProvide(agentContext))
            {
                continue;
            }

            return await provider.Value.ApplyCompletionAsync(context, cancellationToken).ConfigureAwait(false);
        }

        var replacement = context.Replacement;
        return new AutocompleteEdit(
            replacement.Start,
            replacement.Length,
            context.Suggestion.InsertText,
            replacement.Start + context.Suggestion.InsertText.Length);
    }

    private AgentTuiAutocompleteContext CreateContext(AutocompleteRequest request)
    {
        var state = _getState();
        return new AgentTuiAutocompleteContext(request, state?.Scope, state?.Shell);
    }
}
