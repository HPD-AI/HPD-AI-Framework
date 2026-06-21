using HPD.Agent.TUI.Composition;
using HPD.TUI.Controllers;

namespace HPD.Agent.TUI.Commands;

public sealed class SlashCommandAgentAutocompleteProvider : IAgentTuiAutocompleteProvider
{
    private readonly HpdAgentTuiRegistry _registry;

    public SlashCommandAgentAutocompleteProvider(HpdAgentTuiRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public bool CanProvide(AgentTuiAutocompleteContext context)
        => context.Request.TextLength > 0 && context.Request[0] == '/';

    public async ValueTask GetSuggestionsAsync(
        AgentTuiAutocompleteContext context,
        IAutocompleteSuggestionSink suggestions,
        CancellationToken cancellationToken = default)
    {
        var request = context.Request;
        if (request.TextLength == 0 || request[0] != '/' || request.Cursor <= 0)
        {
            return;
        }

        var spaceIndex = request.IndexOf(' ', 0, request.Cursor);
        if (spaceIndex >= 0)
        {
            var commandNameStart = 1;
            var commandNameLength = spaceIndex - commandNameStart;
            var argumentStart = spaceIndex + 1;
            var argumentLength = request.Cursor - argumentStart;
            var command = _registry.Commands.FirstOrDefault(candidate =>
                request.SliceEquals(commandNameStart, commandNameLength, candidate.SlashName, StringComparison.OrdinalIgnoreCase) ||
                request.SliceEquals(commandNameStart, commandNameLength, candidate.Name, StringComparison.OrdinalIgnoreCase));

            if (command?.CompleteArgumentsAsync is null)
            {
                return;
            }

            await command.CompleteArgumentsAsync(
                new AgentTuiCommandCompletionContext(argumentStart, argumentLength, context, suggestions)).ConfigureAwait(false);
            return;
        }

        var queryStart = 1;
        var queryLength = request.Cursor - queryStart;
        foreach (var command in _registry.Commands)
        {
            if (command.Hidden)
            {
                continue;
            }

            if (request.SliceEquals(queryStart, queryLength, command.SlashName, StringComparison.OrdinalIgnoreCase) ||
                request.SliceEquals(queryStart, queryLength, command.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!request.SliceIsPrefixOf(queryStart, queryLength, command.SlashName, StringComparison.OrdinalIgnoreCase) &&
                !request.SliceIsPrefixOf(queryStart, queryLength, command.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            suggestions.Add(
                new AutocompleteSuggestion(
                "/" + command.SlashName,
                "/" + command.SlashName,
                command.Description,
                SubmitOnAccept: true),
                new AutocompleteReplacement(0, request.Cursor));
        }
    }
}
