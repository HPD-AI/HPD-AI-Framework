namespace HPD.Agent.ToolHarness.Coding.TUI.Commands;

internal sealed class CodingCommandExecutionStore
{
    public const string StateKey = "hpd.coding.commands";

    private readonly Dictionary<string, CodingCommandExecutionState> _byCommandId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _commandIdByToolCallId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _commandIdByOperationId = new(StringComparer.Ordinal);

    public CodingCommandExecutionState GetOrCreate(ExecuteCommandEvent evt)
    {
        if (!_byCommandId.TryGetValue(evt.CommandId, out var state))
        {
            state = new CodingCommandExecutionState(evt);
            _byCommandId[evt.CommandId] = state;
        }
        else
        {
            state.ApplyBase(evt);
        }

        _commandIdByToolCallId[evt.ToolCallId] = evt.CommandId;
        if (state.OperationId is not null)
        {
            _commandIdByOperationId[state.OperationId] = evt.CommandId;
        }

        return state;
    }

    public bool TryGetByCommandId(string commandId, out CodingCommandExecutionState state)
        => _byCommandId.TryGetValue(commandId, out state!);

    public CodingCommandExecutionState GetOrCreateSynthetic(
        string commandId,
        string toolCallId,
        string functionName,
        string command,
        string baseCommand,
        ExecuteCommandCategory category,
        string workingDirectory)
    {
        if (!_byCommandId.TryGetValue(commandId, out var state))
        {
            state = new CodingCommandExecutionState(
                commandId,
                toolCallId,
                functionName,
                command,
                baseCommand,
                category,
                workingDirectory);
            _byCommandId[commandId] = state;
        }

        _commandIdByToolCallId[toolCallId] = commandId;
        return state;
    }

    public bool TryGetByToolCallId(string toolCallId, out CodingCommandExecutionState state)
    {
        if (_commandIdByToolCallId.TryGetValue(toolCallId, out var commandId))
        {
            return TryGetByCommandId(commandId, out state);
        }

        state = null!;
        return false;
    }

    public bool TryGetByOperationId(string operationId, out CodingCommandExecutionState state)
    {
        if (_commandIdByOperationId.TryGetValue(operationId, out var commandId))
        {
            return TryGetByCommandId(commandId, out state);
        }

        state = null!;
        return false;
    }

    public void IndexOperation(CodingCommandExecutionState state)
    {
        if (state.OperationId is not null)
        {
            _commandIdByOperationId[state.OperationId] = state.CommandId;
        }
    }

    public IReadOnlyList<CodingCommandExecutionState> ActiveForeground =>
        _byCommandId.Values
            .Where(command => command.DisplayState == CodingCommandDisplayState.Running && !command.IsBackground)
            .ToArray();

    public IReadOnlyList<CodingCommandExecutionState> ActiveBackground =>
        _byCommandId.Values
            .Where(command => (command.DisplayState == CodingCommandDisplayState.Backgrounded || command.IsBackground) &&
                              command.IsActive)
            .ToArray();

    public IReadOnlyList<CodingCommandExecutionState> RecentCompleted =>
        _byCommandId.Values
            .Where(command => !command.IsActive)
            .OrderByDescending(command => command.CompletedAt)
            .Take(10)
            .ToArray();
}
