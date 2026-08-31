using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;

namespace HPD.Agent.TUI;

/// <summary>
/// Adds framework-owned subagent discovery and navigation to an agent TUI.
/// </summary>
public static class SubAgentTuiExtensions
{
    /// <summary>
    /// Registers the <c>/subagents</c> command. Selecting a child switches the complete
    /// runtime scope to its default agent, session, and durable thread.
    /// </summary>
    /// <param name="builder">The TUI builder to extend.</param>
    /// <returns>The same builder.</returns>
    public static HpdAgentTuiBuilder AddSubAgentTui(this HpdAgentTuiBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("subagents", ExecuteAsync)
        {
            Title = "/subagents",
            Description = "Open a durable subagent conversation from the current session."
        });
    }

    private static async ValueTask ExecuteAsync(AgentTuiCommandContext context)
    {
        if (context.Runtime is not IAgentTuiSessionThreadRuntime runtime)
        {
            AddNotice(context, "Subagent threads are not available.", TranscriptSeverity.Warning);
            return;
        }

        var graph = await runtime.GetThreadGraphAsync(context.Scope.SessionId, CancellationToken.None)
            .ConfigureAwait(false);
        var children = graph.RuntimeChildren
            .Where(child => string.Equals(child.ParentSessionId, context.Scope.SessionId, StringComparison.Ordinal))
            .Where(child => child.Kind == ThreadKind.SubAgent || !string.IsNullOrWhiteSpace(child.SubAgentName))
            .OrderByDescending(child => child.LastActivity)
            .ToArray();

        if (children.Length == 0)
        {
            AddNotice(context, "No subagents have run in this session.");
            return;
        }

        await context.Dialogs.RunFlowAsync<object?>(async (flow, cancellationToken) =>
        {
            var selected = await flow.SelectAsync(
                "Subagents",
                children,
                FormatChoice,
                cancellationToken).ConfigureAwait(false);
            if (selected.IsBack || selected.IsCanceled || selected.Value is null)
                return null;

            var child = selected.Value;
            await context.SwitchScopeAsync(
                new AgentTuiRuntimeScope(child.DefaultAgentId, child.SessionId, child.ThreadId),
                cancellationToken).ConfigureAwait(false);
            return null;
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private static string FormatChoice(AgentTuiThreadRuntimeChild child)
    {
        var task = child.SubAgentName ?? child.ThreadId;
        var messages = child.MessageCount == 1 ? "1 message" : $"{child.MessageCount} messages";
        return $"{task}  {child.Status ?? "unknown"}  {messages}";
    }

    private static void AddNotice(
        AgentTuiCommandContext context,
        string message,
        TranscriptSeverity severity = TranscriptSeverity.Info)
        => context.Shell.Transcript.AddFinal(new TranscriptEntry(
            $"subagents-{Guid.NewGuid():N}",
            null,
            new NoticeCell(message, Severity: severity),
            new TranscriptEntryMetadata(
                AgentId: context.Scope.AgentId,
                AgentName: "subagents",
                SessionId: context.Scope.SessionId,
                ThreadId: context.Scope.ThreadId)));
}
