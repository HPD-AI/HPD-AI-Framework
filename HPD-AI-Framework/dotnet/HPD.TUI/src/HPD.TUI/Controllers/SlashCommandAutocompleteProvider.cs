namespace HPD.TUI.Controllers;

public sealed class SlashCommandAutocompleteProvider : IAutocompleteProvider
{
    private readonly IReadOnlyList<TuiSlashCommand> _commands;

    public SlashCommandAutocompleteProvider(IEnumerable<TuiSlashCommand> commands)
    {
        _commands = commands?.ToArray() ?? throw new ArgumentNullException(nameof(commands));
    }

    public async ValueTask GetSuggestionsAsync(
        AutocompleteRequest request,
        IAutocompleteSuggestionSink suggestions,
        CancellationToken cancellationToken = default)
    {
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
            var command = _commands.FirstOrDefault(candidate =>
                request.SliceEquals(commandNameStart, commandNameLength, candidate.Name, StringComparison.OrdinalIgnoreCase));

            if (command.CompleteArgumentsAsync is null)
            {
                return;
            }

            await command.CompleteArgumentsAsync(
                new TuiSlashCommandCompletionContext(command, argumentStart, argumentLength, request, suggestions),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var queryStart = 1;
        var queryLength = request.Cursor - queryStart;

        foreach (var command in _commands)
        {
            if (command.Hidden)
            {
                continue;
            }

            if (request.SliceEquals(queryStart, queryLength, command.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!request.SliceIsPrefixOf(queryStart, queryLength, command.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var description = command.ArgumentHint is { Length: > 0 } hint
                ? string.IsNullOrWhiteSpace(command.Description)
                    ? hint
                    : hint + " - " + command.Description
                : command.Description;

            suggestions.Add(
                new AutocompleteSuggestion(
                "/" + command.Name,
                "/" + command.Name,
                description,
                SubmitOnAccept: true),
                new AutocompleteReplacement(0, request.Cursor));
        }
    }
}

public readonly record struct TuiSlashCommand(
    string Name,
    string? Description = null,
    string? ArgumentHint = null,
    bool Hidden = false,
    Func<TuiSlashCommandCompletionContext, CancellationToken, ValueTask>? CompleteArgumentsAsync = null);

public sealed class TuiSlashCommandCompletionContext
{
    public TuiSlashCommandCompletionContext(
        TuiSlashCommand command,
        int argumentStart,
        int argumentLength,
        AutocompleteRequest request,
        IAutocompleteSuggestionSink suggestions)
    {
        Command = command;
        ArgumentStart = argumentStart;
        ArgumentLength = argumentLength;
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Suggestions = suggestions ?? throw new ArgumentNullException(nameof(suggestions));
    }

    public TuiSlashCommand Command { get; }

    public int ArgumentStart { get; }

    public int ArgumentLength { get; }

    public string ArgumentText => Request.GetText(ArgumentStart, ArgumentLength);

    public AutocompleteRequest Request { get; }

    public IAutocompleteSuggestionSink Suggestions { get; }
}
