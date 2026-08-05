using System.Globalization;
using System.Xml.Linq;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

internal sealed class ExecuteCommandResultTuiHandler : AgentTuiEventHandler<ToolCallResultEvent>, IAgentTuiToolCallHandler
{
    public bool CanHandleToolCall(string? toolHarnessName, string toolName, ToolCallType? callType)
        => string.Equals(toolName, "ExecuteCommand", StringComparison.Ordinal);

    public override ValueTask HandleAsync(
        ToolCallResultEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(evt.Name, "ExecuteCommand", StringComparison.Ordinal) ||
            !TryParseResult(evt, out var document))
        {
            return ValueTask.CompletedTask;
        }

        var root = document.Root;
        if (root is null)
        {
            return ValueTask.CompletedTask;
        }

        var store = context.State.GetOrCreate(
            CodingCommandExecutionStore.StateKey,
            static () => new CodingCommandExecutionStore());

        switch (root.Name.LocalName)
        {
            case "execute_command_stop":
                HandleStop(evt, context, store, root);
                break;
            case "execute_command_output":
                HandleOutput(evt, context, store, root);
                break;
            case "execute_command_background":
                HandleBackgroundList(evt, context, store, root);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private static void HandleStop(
        ToolCallResultEvent evt,
        AgentTuiEventContext context,
        CodingCommandExecutionStore store,
        XElement root)
    {
        var backgroundHandleId = Attr(root, "background_handle_id");
        var command = Attr(root, "command") ?? "Stop background command";
        var workingDirectory = Attr(root, "cwd") ?? "";
        var state = !string.IsNullOrWhiteSpace(backgroundHandleId) &&
                    store.TryGetByBackgroundHandleId(backgroundHandleId, out var existing)
            ? existing
            : store.GetOrCreateSynthetic(
                backgroundHandleId ?? $"stop-{evt.CallId}",
                evt.CallId,
                "ExecuteCommand",
                command,
                GetBaseCommand(command),
                ExecuteCommandCategory.Server,
                workingDirectory);

        state.IsBackground = true;
        state.BackgroundHandleId = backgroundHandleId ?? state.BackgroundHandleId;
        state.CompletedAt = DateTimeOffset.UtcNow;
        state.ExitCode = ParseInt(Attr(root, "exit_code"));
        state.CompletionKind = ParseCompletionKind(Attr(root, "completion_kind"));
        state.DisplayState = ResolveDisplayState(Attr(root, "status"), state.CompletionKind, state.ExitCode);
        ApplyOutputHandle(state, root.Element("combined_output"));
        store.IndexBackgroundHandle(state);

        ExecuteCommandTuiHandlerBase<ExecuteCommandProcessStartedEvent>.UpdateTranscript(context, state, evt);
    }

    private static void HandleOutput(
        ToolCallResultEvent evt,
        AgentTuiEventContext context,
        CodingCommandExecutionStore store,
        XElement root)
    {
        var backgroundHandleId = Attr(root, "background_handle_id");
        var command = Attr(root, "command") ?? "Read background output";
        var workingDirectory = Attr(root, "cwd") ?? "";
        var state = !string.IsNullOrWhiteSpace(backgroundHandleId) &&
                    store.TryGetByBackgroundHandleId(backgroundHandleId, out var existing)
            ? existing
            : store.GetOrCreateSynthetic(
                backgroundHandleId ?? $"output-{evt.CallId}",
                evt.CallId,
                "ExecuteCommand",
                command,
                GetBaseCommand(command),
                ExecuteCommandCategory.Server,
                workingDirectory);

        state.IsBackground = true;
        state.BackgroundHandleId = backgroundHandleId ?? state.BackgroundHandleId;
        state.ExitCode = ParseInt(Attr(root, "exit_code"));
        state.DisplayState = ResolveDisplayState(Attr(root, "status"), state.CompletionKind, state.ExitCode);

        var combined = root.Element("combined_output");
        if (combined is not null)
        {
            var text = combined.Value;
            if (!string.IsNullOrEmpty(text))
            {
                state.Output.Append(ExecuteCommandStreamKind.Stdout, text, suppressed: false, binary: false, truncated: false);
                state.OutputObserved = true;
            }

            ApplyOutputHandle(state, combined);
        }

        store.IndexBackgroundHandle(state);
        ExecuteCommandTuiHandlerBase<ExecuteCommandProcessStartedEvent>.UpdateTranscript(context, state, evt);
    }

    private static void HandleBackgroundList(
        ToolCallResultEvent evt,
        AgentTuiEventContext context,
        CodingCommandExecutionStore store,
        XElement root)
    {
        var state = store.GetOrCreateSynthetic(
            $"background-list-{evt.CallId}",
            evt.CallId,
            "ExecuteCommand",
            "List background commands",
            "background",
            ExecuteCommandCategory.Server,
            "");
        state.CompletedAt = DateTimeOffset.UtcNow;
        state.DisplayState = CodingCommandDisplayState.Completed;

        var rows = root.Elements("command")
            .Select(static command =>
            {
                var id = Attr(command, "background_handle_id") ?? "(unknown)";
                var status = Attr(command, "status") ?? "unknown";
                var text = Attr(command, "command") ?? "";
                return $"{status} {id} {text}".TrimEnd();
            })
            .ToArray();
        var output = rows.Length == 0
            ? "No background commands.\n"
            : string.Join('\n', rows) + "\n";
        state.Output.Append(ExecuteCommandStreamKind.Stdout, output, suppressed: false, binary: false, truncated: false);
        state.OutputObserved = true;

        ExecuteCommandTuiHandlerBase<ExecuteCommandProcessStartedEvent>.UpdateTranscript(context, state, evt);
    }

    private static void ApplyOutputHandle(CodingCommandExecutionState state, XElement? element)
    {
        if (element is null)
        {
            return;
        }

        state.Artifacts.CombinedOutputArtifactPath = Attr(element, "artifact_path");
        state.Artifacts.CombinedOutputContentId = Attr(element, "content_id");
        state.Artifacts.CombinedOutputLocalPath = Attr(element, "local_path");
    }

    private static CodingCommandDisplayState ResolveDisplayState(
        string? status,
        ExecuteCommandCompletionKind? completionKind,
        int? exitCode)
    {
        if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase))
        {
            return CodingCommandDisplayState.Backgrounded;
        }

        return completionKind switch
        {
            ExecuteCommandCompletionKind.Completed when exitCode is 0 => CodingCommandDisplayState.Completed,
            ExecuteCommandCompletionKind.Completed => CodingCommandDisplayState.Failed,
            ExecuteCommandCompletionKind.TimedOut => CodingCommandDisplayState.TimedOut,
            ExecuteCommandCompletionKind.Cancelled or ExecuteCommandCompletionKind.Stopped => CodingCommandDisplayState.Cancelled,
            ExecuteCommandCompletionKind.FailedToStart or ExecuteCommandCompletionKind.Faulted => CodingCommandDisplayState.Failed,
            _ => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
                ? CodingCommandDisplayState.Completed
                : string.Equals(status, "stopped", StringComparison.OrdinalIgnoreCase)
                    ? CodingCommandDisplayState.Cancelled
                    : CodingCommandDisplayState.Exited
        };
    }

    private static ExecuteCommandCompletionKind? ParseCompletionKind(string? value)
        => Enum.TryParse<ExecuteCommandCompletionKind>(value, ignoreCase: true, out var result)
            ? result
            : null;

    private static int? ParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static string? Attr(XElement element, string name)
        => element.Attribute(name)?.Value;

    private static string GetBaseCommand(string command)
    {
        var trimmed = command.TrimStart();
        var end = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return end <= 0 ? trimmed : trimmed[..end];
    }

    private static bool TryParseResult(ToolCallResultEvent evt, out XDocument document)
    {
        document = null!;
        var text = evt.Result.Text?.TrimStart();
        if (string.IsNullOrWhiteSpace(text) ||
            !text.StartsWith("<execute_command_", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            document = XDocument.Parse(text);
            return document.Root?.Name.LocalName is
                "execute_command_stop" or
                "execute_command_output" or
                "execute_command_background";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Xml.XmlException)
        {
            return false;
        }
    }
}
