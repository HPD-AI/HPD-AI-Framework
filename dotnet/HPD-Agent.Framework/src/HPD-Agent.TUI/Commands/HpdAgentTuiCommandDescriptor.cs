using HPD.Agent.TUI.Composition;
using HPD.TUI.Controllers;

namespace HPD.Agent.TUI.Commands;

public sealed class HpdAgentTuiCommandDescriptor
{
    public HpdAgentTuiCommandDescriptor(
        string name,
        Action<AgentTuiCommandContext> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(execute);

        Name = name;
        SlashName = name;
        Title = name;
        ExecuteAsync = context =>
        {
            execute(context);
            return ValueTask.CompletedTask;
        };
    }

    public HpdAgentTuiCommandDescriptor(
        string name,
        Func<AgentTuiCommandContext, ValueTask> executeAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(executeAsync);

        Name = name;
        SlashName = name;
        Title = name;
        ExecuteAsync = executeAsync;
    }

    public string Name { get; }

    public string SlashName { get; init; }

    public string Title { get; init; }

    public string? Description { get; init; }

    public bool Hidden { get; init; }

    public Func<AgentTuiCommandCompletionContext, ValueTask>? CompleteArgumentsAsync { get; init; }

    public Func<AgentTuiCommandContext, ValueTask> ExecuteAsync { get; }
}

public sealed class AgentTuiCommandCompletionContext
{
    public AgentTuiCommandCompletionContext(
        int argumentStart,
        int argumentLength,
        AgentTuiAutocompleteContext autocomplete,
        IAutocompleteSuggestionSink suggestions)
    {
        ArgumentStart = argumentStart;
        ArgumentLength = argumentLength;
        Autocomplete = autocomplete ?? throw new ArgumentNullException(nameof(autocomplete));
        Suggestions = suggestions ?? throw new ArgumentNullException(nameof(suggestions));
    }

    public int ArgumentStart { get; }

    public int ArgumentLength { get; }

    public string ArgumentText => Autocomplete.Request.GetText(ArgumentStart, ArgumentLength);

    public AgentTuiAutocompleteContext Autocomplete { get; }

    public IAutocompleteSuggestionSink Suggestions { get; }
}
