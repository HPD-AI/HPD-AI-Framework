using System.Text.Json;
using System.Xml.Linq;
using HPD.Agent;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugToolCallTuiCoordinator : IAgentTuiEventHandler, IAgentTuiToolCallHandler
{
    private const string ToolName = "Debug";

    public bool CanHandle(AgentEvent evt)
        => evt switch
        {
            ToolCallStartEvent started => IsDebug(started.Name),
            ToolCallArgsEvent args => StoreMayContain(args.CallId),
            ToolCallResultEvent result => IsDebug(result.Name) || StoreMayContain(result.CallId),
            ToolCallEndEvent ended => IsDebug(ended.Name) || StoreMayContain(ended.CallId),
            _ => false
        };

    // CanHandle has no session context. Args and end events are safely ignored in HandleAsync
    // when no Debug start/result established ownership.
    private static bool StoreMayContain(string _) => true;

    public bool CanHandleToolCall(string? toolHarnessName, string toolName, ToolCallType? callType)
        => IsDebug(toolName);

    public ValueTask HandleAsync(
        AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var store = Store(context);
        switch (evt)
        {
            case ToolCallStartEvent started when IsDebug(started.Name):
            {
                var state = store.Start(started.CallId);
                Apply(context, state, started, active: true);
                break;
            }
            case ToolCallArgsEvent args when store.TryGet(args.CallId, out var state):
                BindAction(state, args.ArgsJson);
                Apply(context, state, args, active: true);
                break;
            case ToolCallResultEvent result
                when (IsDebug(result.Name) || store.TryGet(result.CallId, out _)):
            {
                var resultState = store.Start(result.CallId);
                resultState.ResultObserved = true;
                resultState.ResultText = ResultText(result.Result);
                resultState.Succeeded = IsSuccess(resultState.ResultText);
                if (resultState.Claim is DebugPresentationClaim.Lifecycle
                    or DebugPresentationClaim.Breakpoint
                    or DebugPresentationClaim.Execution
                    or DebugPresentationClaim.Inspection
                    or DebugPresentationClaim.Mutation)
                {
                    context.Shell.Transcript.RemoveLive(EntryKey(resultState.ToolCallId));
                    if (resultState.Claim == DebugPresentationClaim.Lifecycle &&
                        resultState.ResultText is { Length: > 0 } lifecycleResult)
                        DebugLifecycleTuiHandler.ObserveToolResult(
                            context,
                            resultState.ToolCallId,
                            lifecycleResult,
                            result);
                }
                else if (resultState.Policy?.FoldOnSuccess == true && resultState.Succeeded)
                {
                    resultState.Claim = DebugPresentationClaim.Folded;
                    context.Shell.Transcript.RemoveLive(EntryKey(resultState.ToolCallId));
                }
                else
                {
                    resultState.Claim = DebugPresentationClaim.Fallback;
                    Apply(context, resultState, result, active: false);
                }
                break;
            }
            case ToolCallEndEvent ended when store.TryGet(ended.CallId, out var state):
                state.EndObserved = true;
                break;
        }
        return ValueTask.CompletedTask;
    }

    internal static DebugToolCallPresentationStore Store(AgentTuiEventContext context)
        => context.State.GetOrCreate(
            DebugToolCallPresentationStore.StateKey,
            static () => new DebugToolCallPresentationStore());

    internal static void Claim(
        AgentTuiEventContext context,
        string toolCallId,
        DebugPresentationClaim claim)
    {
        var store = Store(context);
        var state = store.Start(toolCallId);
        state.Claim = claim;
        var entryKey = EntryKey(toolCallId);
        context.Shell.Transcript.RemoveWhere(entry =>
            string.Equals(entry.EntryKey, entryKey, StringComparison.Ordinal));
    }

    private static void BindAction(DebugToolCallPresentationState state, string argsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argsJson);
            var root = document.RootElement;
            var request = root.TryGetProperty("request", out var nested) ? nested : root;
            if (!request.TryGetProperty("action", out var actionProperty) ||
                actionProperty.ValueKind != JsonValueKind.String)
                return;
            state.Action = actionProperty.GetString() ?? "unknown";
            if (DebugActionPresentationPolicy.TryGet(state.Action, out var policy))
                state.Policy = policy;
        }
        catch (JsonException)
        {
            state.Action = "invalid request";
        }
    }

    private static void Apply(
        AgentTuiEventContext context,
        DebugToolCallPresentationState state,
        AgentEvent evt,
        bool active)
    {
        var label = Label(state, active);
        var lines = Lines(state, active);
        var entry = new TranscriptEntry(
            Id: $"debug-activity-{state.ToolCallId}",
            EntryKey: EntryKey(state.ToolCallId),
            Cell: new DebugActivityCell(
                state.ToolCallId,
                state.Action,
                label,
                lines,
                active,
                !active && !state.Succeeded),
            Metadata: TranscriptEntryMetadata.FromEvent(evt),
            VerticalSpacing: 1);
        if (active) context.Shell.Transcript.UpsertLive(entry.AsLive());
        else context.Shell.Transcript.FinalizeLive(entry.EntryKey, entry.AsFinal());
    }

    private static string Label(DebugToolCallPresentationState state, bool active)
    {
        if (active)
            return state.Action == "unknown" ? "• Debugging" : $"• Debug · {DisplayAction(state.Action)}";
        return state.Succeeded
            ? $"• Debug · {DisplayAction(state.Action)}"
            : $"• Debug failed · {DisplayAction(state.Action)}";
    }

    private static IReadOnlyList<string> Lines(DebugToolCallPresentationState state, bool active)
    {
        if (active)
            return [state.Action == "unknown" ? "Preparing debugger request…" : "Running…"];
        var text = state.ResultText;
        if (string.IsNullOrWhiteSpace(text))
            return ["The debugger returned an empty result."];
        if (TryValidationLines(text, out var validationLines))
            return validationLines;
        try
        {
            var root = XDocument.Parse(text).Root;
            if (root is null) return [Bound(text)];
            var message = string.Join(" ", root.Nodes().OfType<XText>()
                .Select(node => node.Value.Trim())
                .Where(value => value.Length > 0));
            if (message.Length > 0) return [Bound(message)];
            var attributes = root.Attributes()
                .Where(attribute => attribute.Name.LocalName is not ("tool" or "action" or "success"))
                .Take(4)
                .Select(attribute => $"{DisplayName(attribute.Name.LocalName)}: {attribute.Value}")
                .ToArray();
            return attributes.Length == 0 ? ["Completed."] : attributes;
        }
        catch
        {
            return [Bound(text)];
        }
    }

    private static bool TryValidationLines(
        string text,
        out IReadOnlyList<string> lines)
    {
        lines = [];
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (!root.TryGetProperty("error_type", out var type) ||
                type.GetString() != "validation_error")
                return false;
            if (!root.TryGetProperty("errors", out var errors) ||
                errors.ValueKind != JsonValueKind.Array)
            {
                lines = ["The debugger request is invalid."];
                return true;
            }
            lines = errors.EnumerateArray()
                .Take(4)
                .Select(error =>
                {
                    var property = error.TryGetProperty("property", out var propertyNode)
                        ? propertyNode.GetString()
                        : null;
                    var message = error.TryGetProperty("error_message", out var messageNode)
                        ? messageNode.GetString()
                        : "Invalid value.";
                    return Bound(string.IsNullOrWhiteSpace(property)
                        ? message ?? "Invalid value."
                        : $"{property}: {message}");
                })
                .ToArray();
            if (lines.Count == 0) lines = ["The debugger request is invalid."];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSuccess(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var root = XDocument.Parse(text).Root;
            return root?.Name.LocalName == "debug" &&
                !string.Equals(root.Attribute("success")?.Value, "false", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResultText(ToolResultPayload payload)
    {
        if (!string.IsNullOrWhiteSpace(payload.Text)) return payload.Text;
        if (payload.Json is not { } json) return null;
        return json.ValueKind == JsonValueKind.String ? json.GetString() : json.GetRawText();
    }

    private static bool IsDebug(string? name)
        => string.Equals(name, ToolName, StringComparison.Ordinal);

    private static string EntryKey(string callId) => $"hpd.coding.debug:activity:{callId}";

    private static string DisplayAction(string action)
        => action.Length == 0 ? "operation" :
            string.Concat(action.Select((character, index) =>
                index > 0 && char.IsUpper(character) ? $" {char.ToLowerInvariant(character)}" : character.ToString()));

    private static string DisplayName(string value)
        => value.Replace('_', ' ');

    private static string Bound(string value)
    {
        var compact = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 240 ? compact : compact[..237] + "…";
    }
}
